using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CustomerOrderCaptureTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Theory]
    [InlineData("2026-10-31T05:00:00Z", "2026-10-31")] // Friday 23:00 -> Saturday.
    [InlineData("2026-10-31T07:00:00Z", "2026-10-31")] // Saturday 01:00 -> Saturday.
    [InlineData("2026-10-31T23:59:59Z", "2026-10-31")]
    [InlineData("2026-11-01T00:00:00Z", "2026-11-01")] // Saturday 18:00 inclusive.
    [InlineData("2026-11-01T05:59:59Z", "2026-11-01")]
    [InlineData("2026-11-01T06:00:00Z", "2026-11-01")] // Midnight does not shift target a second time.
    public void DeliveryDate_UsesMexicoCityCutoffInclusiveAndHandlesMidnight(string now, string expected)
        => Assert.Equal(DateOnly.Parse(expected), DeliveryDatePolicy.GetDate(DateTimeOffset.Parse(now)));

    [Fact]
    public async Task QuickCapture_PersistsPublicOrderAndOriginalText_AndReplaysAfterCutoff()
    {
        await using var f = await Fixture.Create();
        f.Clock.Now = DateTimeOffset.Parse("2026-10-31T23:59:00Z");
        var next = await f.Service.GetNextAsync(f.Customer.Id, Ct);
        var preview = await f.Service.PreviewAsync(f.Customer.Id, new("13+7", next.DeliveryDate), Ct);
        Assert.Empty(await f.Db.Orders.ToArrayAsync());
        var request = new SubmitCustomerOrderCaptureRequest(Guid.NewGuid(), next.ProfileVersion,
            next.DeliveryDate, preview.Text, preview.Fingerprint, DeliveryNotes: "Dejar junto a la puerta");
        var saved = await f.Service.SubmitAsync(f.Customer.Id, request, Ct);
        f.Clock.Now = DateTimeOffset.Parse("2026-11-01T00:01:00Z");
        var replay = await f.Service.SubmitAsync(f.Customer.Id, request, Ct);
        Assert.False(saved.Replayed); Assert.True(replay.Replayed);
        Assert.Equal(saved.Order.OrderId, replay.Order.OrderId);
        f.Db.ChangeTracker.Clear();
        var order = await f.Db.Orders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 31), order.OrderDate);
        Assert.Equal(20, order.Lines.Sum(x => x.Quantity));
        Assert.Equal("Dejar junto a la puerta", order.DeliveryNotes);
        Assert.Null(order.InternalNotes);
        Assert.Equal(1, await f.Db.OrderOperations.CountAsync());
        var submitted = await f.Db.OrderAuditLogs.SingleAsync(x => x.EventType == OrderAuditEventType.OrderSubmitted);
        Assert.Equal(AuditActorType.Customer, submitted.ActorType);
        using var metadata = JsonDocument.Parse(submitted.MetadataJson!);
        Assert.Equal("13+7", metadata.RootElement.GetProperty("originalText").GetString());
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.SubmitAsync(f.Customer.Id,
            request with { DeliveryNotes = "contenido distinto" }, Ct));
        Assert.Equal(1, await f.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task NewCaptureAcrossCutoff_FailsWithoutMutation_ThenRequiresNewPreview()
    {
        await using var f = await Fixture.Create();
        f.Clock.Now = DateTimeOffset.Parse("2026-10-31T23:59:00Z");
        var preview = await f.Service.PreviewAsync(f.Customer.Id, new("16", f.Date), Ct);
        var request = new SubmitCustomerOrderCaptureRequest(Guid.NewGuid(), preview.ProfileVersion,
            preview.DeliveryDate, preview.Text, preview.Fingerprint);
        f.Clock.Now = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        var conflict = await Assert.ThrowsAsync<DeliveryDateChangedException>(() => f.Service.SubmitAsync(f.Customer.Id, request, Ct));
        Assert.Equal(new DateOnly(2026, 11, 1), conflict.DeliveryDate);
        Assert.Empty(await f.Db.Orders.ToArrayAsync());
        Assert.Empty(await f.Db.OrderAuditLogs.ToArrayAsync());
        Assert.Empty(await f.Db.OrderOperations.ToArrayAsync());
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Service.SubmitAsync(f.Customer.Id,
            request with { ExpectedDeliveryDate = f.Date }, Ct));
        var refreshed = await f.Service.PreviewAsync(f.Customer.Id, new(request.Text!, f.Date), Ct);
        var saved = await f.Service.SubmitAsync(f.Customer.Id, request with {
            ExpectedDeliveryDate = f.Date, PreviewFingerprint = refreshed.Fingerprint }, Ct);
        Assert.Equal(f.Date, saved.Order.OrderDate);
        Assert.False(saved.Order.IsLate); // Service tomorrow was captured before its 10:00 review cutoff.
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    [InlineData("no-order")]
    [InlineData("admin-no-order")]
    public async Task ConfirmationReceivedBeforeCutoff_KeepsDateAndTimestampWhenDatabaseWaitCrossesCutoff(string mode)
    {
        await using var f = await Fixture.Create(advanceClockOnOrderCount: true);
        var receivedAt = DateTimeOffset.Parse("2026-10-31T23:59:00Z"); // 17:59 in Mexico City.
        f.Clock.Now = receivedAt;
        var deliveryDate = f.Date;
        CustomerOrderResponse result;
        if (mode.StartsWith("admin", StringComparison.Ordinal))
        {
            var orderId = mode == "admin"
                ? (await f.Admin.SubmitAsync(f.Customer.Id, new SubmitOrderCaptureRequest(
                    Guid.NewGuid(), 1, Lines: [new(f.Products[0].Id, 10, "kg")],
                    AutoDeliveryDate: true, ExpectedDeliveryDate: deliveryDate), null, Ct)).Order.OrderId
                : (await f.AdminOrders.MarkNoOrderAsync(f.Customer.Id, new(null,
                    AutoDeliveryDate: true, ExpectedDeliveryDate: deliveryDate), null, Ct)).OrderId;
            var order = await f.Db.Orders.SingleAsync();
            Assert.Equal(orderId, order.Id);
            result = new(order.Id, f.Customer.Id, order.OrderDate, order.Status, order.SequenceNumber,
                order.SubmittedAt, order.IsLate, order.RequiresAdminReview, order.AdminReviewReason);
        }
        else if (mode == "no-order")
        {
            result = (await f.Service.MarkNoOrderAsync(f.Customer.Id,
                new(Guid.NewGuid(), deliveryDate), Ct)).Order;
        }
        else result = (await f.Service.SubmitAsync(f.Customer.Id, f.Manual(), Ct)).Order;

        Assert.Equal(DateTimeOffset.Parse("2026-11-01T00:01:00Z"), f.Clock.Now); // Count actually crossed 18:00.
        Assert.Equal(new DateOnly(2026, 10, 31), result.OrderDate);
        Assert.Equal(receivedAt, result.SubmittedAt);
        Assert.Equal(!mode.Contains("no-order", StringComparison.Ordinal), result.IsLate);
        if (mode != "admin-no-order") Assert.Equal(receivedAt, (await f.Db.OrderOperations.SingleAsync()).CreatedAt);
        Assert.Equal(1, await f.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task ManualAndQuick_ResolveSameQuantities_ManualDoesNotApproveProfile()
    {
        await using var quick = await Fixture.Create();
        await using var manual = await Fixture.Create(false);
        var p = await quick.Service.PreviewAsync(quick.Customer.Id, new("13+7", quick.Date), Ct);
        await quick.Service.SubmitAsync(quick.Customer.Id, new(Guid.NewGuid(), p.ProfileVersion, p.DeliveryDate, p.Text, p.Fingerprint), Ct);
        await manual.Service.SubmitAsync(manual.Customer.Id, manual.Manual(13, 7), Ct);
        var a = (await quick.Db.OrderLines.ToArrayAsync()).OrderBy(x => x.Quantity).Select(x => x.Quantity);
        var b = (await manual.Db.OrderLines.ToArrayAsync()).OrderBy(x => x.Quantity).Select(x => x.Quantity);
        Assert.Equal(a, b);
        Assert.False((await manual.Db.Customers.SingleAsync()).IsOrderProfileApproved);
        var unapprovedPreview = await manual.Service.PreviewAsync(manual.Customer.Id, new("13+7", manual.Date), Ct);
        await Assert.ThrowsAsync<CatalogConflictException>(() => manual.Service.SubmitAsync(manual.Customer.Id,
            new(Guid.NewGuid(), unapprovedPreview.ProfileVersion, unapprovedPreview.DeliveryDate,
                unapprovedPreview.Text, unapprovedPreview.Fingerprint), Ct));
    }

    [Fact]
    public async Task ManualRejectsUnconfiguredProductChangedUnitAndMixedModes()
    {
        await using var f = await Fixture.Create(false);
        var unrelated = Product.Create("Producto de otro cliente", null); unrelated.SetUnitCode("kg");
        f.Db.Add(unrelated); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SubmitAsync(f.Customer.Id,
            f.Manual() with { Lines = [new(unrelated.Id, 10, "kg")] }, Ct));
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Service.SubmitAsync(f.Customer.Id,
            f.Manual() with { Lines = [new(f.Products[0].Id, 10, "piece")] }, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SubmitAsync(f.Customer.Id,
            f.Manual() with { Text = "10" }, Ct));
        Assert.Empty(await f.Db.Orders.ToArrayAsync());
        Assert.Empty(await f.Db.OrderOperations.ToArrayAsync());
    }

    [Fact]
    public async Task ExistingOrderForTargetDate_BlocksNewAttemptAndIsReturnedWithoutInternalData()
    {
        await using var f = await Fixture.Create();
        await f.Service.SubmitAsync(f.Customer.Id, f.Manual(), Ct);
        var current = await f.Service.GetNextAsync(f.Customer.Id, Ct);
        Assert.True(current.HasActiveOrder);
        Assert.Single(current.CurrentOrder!.Lines);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.SubmitAsync(f.Customer.Id, f.Manual(), Ct));
        var json = JsonSerializer.Serialize(current);
        Assert.DoesNotContain("machine", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StoreKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await f.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task NoOrder_IsDurableAndExplicit_AndDoesNotPreventLaterOrder()
    {
        await using var f = await Fixture.Create();
        var request = new MarkNextCustomerNoOrderRequest(Guid.NewGuid(), f.Date);
        var first = await f.Service.MarkNoOrderAsync(f.Customer.Id, request, Ct);
        var replay = await f.Service.MarkNoOrderAsync(f.Customer.Id, request, Ct);
        var repeated = await f.Service.MarkNoOrderAsync(f.Customer.Id, request with { OperationId = Guid.NewGuid() }, Ct);
        Assert.Equal(OrderStatus.NoOrder, first.Order.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Order.OrderId, repeated.Order.OrderId);
        Assert.Equal(1, await f.Db.Orders.CountAsync());
        Assert.False((await f.Service.GetNextAsync(f.Customer.Id, Ct)).HasActiveOrder);
        await f.Service.SubmitAsync(f.Customer.Id, f.Manual(), Ct);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.MarkNoOrderAsync(f.Customer.Id,
            request with { OperationId = Guid.NewGuid() }, Ct));
        Assert.Equal(2, await f.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task AuditFailure_RollsBackOrderLinesAuditAndReceipt()
    {
        await using var f = await Fixture.Create();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_customer_audit BEFORE INSERT ON OrderAuditLogs WHEN NEW.EventType='OrderSubmitted' BEGIN SELECT RAISE(ABORT,'fictional failure'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.SubmitAsync(f.Customer.Id, f.Manual(), Ct));
        Assert.Empty(await f.Db.Orders.ToArrayAsync());
        Assert.Empty(await f.Db.OrderLines.ToArrayAsync());
        Assert.Empty(await f.Db.OrderAuditLogs.ToArrayAsync());
        Assert.Empty(await f.Db.OrderOperations.ToArrayAsync());
    }

    [Fact]
    public async Task AdminAutomaticDate_ValidatesCutoffWhileExplicitHistoricalDateRemainsSupported()
    {
        await using var f = await Fixture.Create();
        var auto = new SubmitOrderCaptureRequest(Guid.NewGuid(), 1, Lines: [new(f.Products[0].Id, 10, "kg")],
            AutoDeliveryDate: true, ExpectedDeliveryDate: f.Date);
        f.Clock.Now = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        await Assert.ThrowsAsync<DeliveryDateChangedException>(() => f.Admin.SubmitAsync(f.Customer.Id, auto, null, Ct));
        Assert.Empty(await f.Db.Orders.ToArrayAsync());
        var explicitDate = f.Date.AddDays(-3);
        var saved = await f.Admin.SubmitAsync(f.Customer.Id,
            auto with { AutoDeliveryDate = false, ExpectedDeliveryDate = null, ServiceDate = explicitDate }, null, Ct);
        Assert.Equal(explicitDate, saved.Order.OrderDate);
    }

    [Fact]
    public async Task AdminCapture_ExistingI07ReceiptRemainsReplayableAfterOptionalDtoFieldsWereAdded()
    {
        await using var f = await Fixture.Create();
        var request = new SubmitOrderCaptureRequest(Guid.NewGuid(), 1, f.Date,
            Lines: [new(f.Products[0].Id, 10, "kg")]);
        var saved = await f.Admin.SubmitAsync(f.Customer.Id, request, null, Ct);
        var originalI07Request = new { request.OperationId, request.ExpectedProfileVersion, request.ServiceDate,
            request.Text, request.PreviewFingerprint, request.Lines, request.ConfirmSeparateDelivery,
            request.DeliveryNotes, request.InternalNotes };
        var originalBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            customerId = f.Customer.Id, date = f.Date, request = originalI07Request, actorId = (string?)null
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var originalHash = Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant();
        var receipt = await f.Db.OrderOperations.SingleAsync();
        receipt.PayloadHash = originalHash;
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var replay = await f.Admin.SubmitAsync(f.Customer.Id, request, null, Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(saved.Order.OrderId, replay.Order.OrderId);
        Assert.Equal(1, await f.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task AdminNoOrder_WithAutomaticDateRejectsStaleConfirmationWithoutWriting()
    {
        await using var f = await Fixture.Create();
        var expected = f.Date;
        f.Clock.Now = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        await Assert.ThrowsAsync<DeliveryDateChangedException>(() => f.AdminOrders.MarkNoOrderAsync(f.Customer.Id,
            new(null, AutoDeliveryDate: true, ExpectedDeliveryDate: expected), null, Ct));
        Assert.Empty(await f.Db.Orders.ToArrayAsync());
        Assert.Empty(await f.Db.OrderAuditLogs.ToArrayAsync());
        var saved = await f.AdminOrders.MarkNoOrderAsync(f.Customer.Id,
            new(null, AutoDeliveryDate: true, ExpectedDeliveryDate: f.Date), null, Ct);
        Assert.Equal(f.Date, saved.OrderDate);
        Assert.Equal(OrderStatus.NoOrder, saved.Status);
    }

    [Fact]
    public async Task NextEndpoint_RequiresOwnCustomerAndReturnsOnlyPublicContract()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        var own = $"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/next";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(own)).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = DevelopmentAuthSeedValues.DefaultCustomerToken });
        var session = (await login.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/customer-orders/{DevelopmentSeedIds.DemoCustomer2Id}/next")).StatusCode);
        var response = await client.GetAsync(own);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("machine", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hasStoreKey", text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public PedidosDbContext Db { get; }
        public Customer Customer { get; }
        public Product[] Products { get; }
        public MutableClock Clock { get; } = new();
        public CustomerOrderCaptureService Service { get; }
        public AdminOrderCaptureService Admin { get; }
        public AdminOrderService AdminOrders { get; }
        public DateOnly Date => DeliveryDatePolicy.GetDate(Clock);
        private Fixture(SqliteConnection c, PedidosDbContext db, Customer customer, Product[] products,
            bool advanceClockOnOrderCount)
        {
            connection = c; Db = db; Customer = customer; Products = products;
            var customers = new EfCustomerRepository(db);
            IOrderRepository orders = new EfOrderRepository(db);
            if (advanceClockOnOrderCount)
                orders = new CountAdvancingOrderRepository(orders,
                    () => Clock.Now = DateTimeOffset.Parse("2026-11-01T00:01:00Z"));
            var audit = new EfOrderAuditLogRepository(db); var machines = new EfMachineRepository(db);
            var store = new EfOrderOperationStore(db); var channels = new EfSalesChannelRepository(db);
            Service = new(customers, new EfProductRepository(db), machines, channels, orders, audit, store, Clock);
            var admin = new AdminOrderService(orders, audit, customers, new EfProductRepository(db), machines,
                channels, Clock, new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(db), machines, customers));
            AdminOrders = admin;
            Admin = new(customers, orders, audit, store, Clock, admin);
        }
        public SubmitCustomerOrderCaptureRequest Manual(decimal first = 10, decimal? second = null)
            => new(Guid.NewGuid(), 1, Date, Lines: second is null
                ? [new(Products[0].Id, first, "kg")]
                : [new(Products[0].Id, first, "kg"), new(Products[1].Id, second.Value, "kg")]);

        public static async Task<Fixture> Create(bool approved = true, bool advanceClockOnOrderCount = false)
        {
            var c = new SqliteConnection("Data Source=:memory:"); await c.OpenAsync();
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(c).Options);
            await db.Database.EnsureCreatedAsync();
            var clock = new MutableClock();
            var customer = Customer.Create("Cliente ficticio I09", null, new TimeOnly(12, 30), null, null, null, clock.Now);
            customer.RecordOrderingProfileChange(approved, clock.Now);
            customer.InternalDeliveryInstructions = "Dato privado ficticio";
            var products = new[] { Product.Create("Producto ficticio A", null), Product.Create("Producto ficticio B", null) };
            foreach (var p in products) p.SetUnitCode("kg");
            db.AddRange(products); db.Add(customer);
            db.Add(new SalesChannel { Id = Guid.NewGuid(), Name = "Portal", Type = SalesChannelType.Customer, IsInternal = false });
            db.Add(new SalesChannel { Id = Guid.NewGuid(), Name = "Captura", Type = SalesChannelType.AdminManualCapture, IsInternal = true });
            for (var i = 0; i < products.Length; i++) db.Add(new CustomerFrequentProduct {
                CustomerId = customer.Id, ProductId = products[i].Id, SortOrder = i + 1, InterpretationPosition = i + 1 });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            return new(c, db, customer, products, advanceClockOnOrderCount);
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    // Exercise the real persistence transaction while simulating a wait after the
    // last count query; only time advances, not the repository's SQL behavior.
    private sealed class CountAdvancingOrderRepository(IOrderRepository inner, Action advance) : IOrderRepository
    {
        public async Task<int> CountCustomerOrdersAsync(Guid id, DateOnly date, CancellationToken token)
        {
            var count = await inner.CountCustomerOrdersAsync(id, date, token);
            advance();
            return count;
        }

        public Task<bool> HasActiveCustomerOrderAsync(Guid id, DateOnly date, CancellationToken token)
            => inner.HasActiveCustomerOrderAsync(id, date, token);
        public Task<Order?> GetLatestCustomerOrderAsync(Guid id, DateOnly date, CancellationToken token)
            => inner.GetLatestCustomerOrderAsync(id, date, token);
        public Task<IReadOnlySet<Guid>> GetCustomerIdsWithOrdersAsync(DateOnly date, CancellationToken token)
            => inner.GetCustomerIdsWithOrdersAsync(date, token);
        public Task AddAsync(Order order, CancellationToken token) => inner.AddAsync(order, token);
        public Task<IReadOnlyList<Order>> GetByDateAsync(DateOnly date, CancellationToken token)
            => inner.GetByDateAsync(date, token);
        public Task<IReadOnlyList<Order>> GetPendingReviewAsync(DateOnly date, CancellationToken token)
            => inner.GetPendingReviewAsync(date, token);
        public Task<Order?> GetByIdAsync(Guid id, CancellationToken token) => inner.GetByIdAsync(id, token);
        public Task<Order?> GetBySourceAsync(string source, string key, CancellationToken token)
            => inner.GetBySourceAsync(source, key, token);
        public Task ReplaceLinesAsync(Order order, IReadOnlyList<OrderLine> lines, CancellationToken token)
            => inner.ReplaceLinesAsync(order, lines, token);
        public Task SaveChangesAsync(CancellationToken token) => inner.SaveChangesAsync(token);
    }

    private sealed class MutableClock : IDateTimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-31T15:00:00Z");
        public DateOnly Today => DateOnly.FromDateTime(Now.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.ToOffset(TimeSpan.FromHours(-6)).DateTime);
    }
}
