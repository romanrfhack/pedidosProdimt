using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CustomerProductSetupTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly AdminActorContext Actor = new("fixture-general-admin", "Administración de prueba");

    [Fact]
    public async Task AdditionalCatalog_IsMinimalAndExcludesConfiguredInactiveMergedAndUnconfirmedUnits()
    {
        await using var f = await Fixture.Create();
        f.Db.Products.AddRange(new Product { Id = Guid.NewGuid(), Name = "Inactivo", UnitCode = "kg", IsActive = false },
            new Product { Id = Guid.NewGuid(), Name = "Sin unidad" },
            new Product { Id = Guid.NewGuid(), Name = "Equivalencia", UnitCode = "package", IsActive = false, MergedIntoProductId = f.Extra.Id });
        await f.Db.SaveChangesAsync();
        var response = await f.Setup.GetAdditionalAsync(f.Customer.Id, Ct);
        var item = Assert.Single(response); Assert.Equal(f.Extra.Id, item.ProductId); Assert.Equal("package", item.UnitCode);
        var json = JsonSerializer.Serialize(response);
        Assert.DoesNotContain("INTERNAL-FIXTURE", json); Assert.DoesNotContain("Machine", json); Assert.DoesNotContain("Price", json);
        Assert.Empty(await f.Db.CustomerProductSetupRequests.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_ContinuesWithoutPriceOrInheritedMachine_AndReplaysOneOrderAndOneFollowUp(bool admin)
    {
        await using var f = await Fixture.Create();
        var operation = Guid.NewGuid();
        var first = await f.Capture(admin, operation, withHabitual: true);
        var replay = await f.Capture(admin, operation, withHabitual: true);
        Assert.Equal(first, replay);
        f.Db.ChangeTracker.Clear();
        var order = await f.Db.Orders.Include(x => x.Lines).SingleAsync();
        Assert.False(order.RequiresAdminReview);
        var added = order.Lines.Single(x => x.ProductId == f.Extra.Id);
        Assert.Equal(2, added.Quantity); Assert.Equal("package", added.UnitCode);
        Assert.Null(added.UnitPriceSnapshot); Assert.Null(added.LineAmount); Assert.Null(added.AssignedMachineId);
        Assert.Equal(f.Machine.Id, order.Lines.Single(x => x.ProductId == f.Habitual.Id).AssignedMachineId);
        var followUp = await f.Db.CustomerProductSetupRequests.Include(x => x.Orders).SingleAsync();
        Assert.Equal(admin ? "Admin" : "Customer", Assert.Single(followUp.Orders).Source);
        Assert.Equal(f.Extra.Id, followUp.ProductId); Assert.False(followUp.IsResolved);
        var customer = await f.Db.Customers.SingleAsync();
        Assert.Equal(1, customer.OrderingProfileVersion); Assert.True(customer.IsOrderProfileApproved);
        Assert.Single(await f.Db.CustomerFrequentProducts.ToArrayAsync());
        Assert.Single(await f.Db.OrderOperations.ToArrayAsync());
        Assert.Single(await f.Db.OrderAuditLogs.Where(x => x.EventType == OrderAuditEventType.CustomerProductSetupRequested).ToArrayAsync());
    }

    [Fact]
    public async Task AnotherDelivery_JoinsTheSameCustomerProductFollowUpWithoutSummingOrders()
    {
        await using var f = await Fixture.Create();
        var first = await f.Capture(false);
        f.Clock.Now = f.Clock.Now.AddDays(1);
        var second = await f.Capture(false);
        Assert.NotEqual(first, second);
        var request = await f.Db.CustomerProductSetupRequests.Include(x => x.Orders).SingleAsync();
        Assert.Equal(2, request.Orders.Count); Assert.Equal(2, request.Version);
        Assert.Equal(2, await f.Db.Orders.CountAsync());
        var inbox = Assert.Single(await f.Setup.GetOpenAsync(Ct));
        Assert.Equal(2, inbox.Orders.Count); Assert.All(inbox.Orders, x => Assert.True(x.PricePending));
    }

    [Theory]
    [InlineData("kg", 2)]
    [InlineData("package", -1)]
    [InlineData("package", 1.234)]
    public async Task StaleUnitOrInvalidQuantity_FailsWithoutCreatingOrderOrRequest(string unit, double quantity)
    {
        await using var f = await Fixture.Create();
        var input = new SubmitCustomerOrderCaptureRequest(Guid.NewGuid(), 1, f.Clock.Today,
            Lines: [new(f.Extra.Id, (decimal)quantity, unit)]);
        if (unit != "package") await Assert.ThrowsAsync<CatalogConflictException>(() => f.CustomerCapture.SubmitAsync(f.Customer.Id, input, Ct));
        else await Assert.ThrowsAnyAsync<ArgumentException>(() => f.CustomerCapture.SubmitAsync(f.Customer.Id, input, Ct));
        Assert.Empty(await f.Db.Orders.ToArrayAsync()); Assert.Empty(await f.Db.CustomerProductSetupRequests.ToArrayAsync());
        Assert.Empty(await f.Db.OrderOperations.ToArrayAsync());
    }

    [Fact]
    public async Task DisabledCustomerRelationship_CannotBeBypassedThroughTheGlobalCatalog()
    {
        await using var f = await Fixture.Create();
        f.Db.CustomerFrequentProducts.Add(new() { CustomerId = f.Customer.Id, ProductId = f.Extra.Id, IsActive = false, SortOrder = 2 });
        await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Setup.GetAdditionalAsync(f.Customer.Id, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Capture(false));
        Assert.Empty(await f.Db.Orders.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FollowUpFailure_RollsBackTheOrderAuditAndReceiptTogether(bool admin)
    {
        await using var f = await Fixture.Create();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fixture_setup_failure BEFORE INSERT ON CustomerProductSetupRequests BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => f.Capture(admin));
        Assert.Empty(await f.Db.Orders.ToArrayAsync()); Assert.Empty(await f.Db.OrderLines.ToArrayAsync());
        Assert.Empty(await f.Db.OrderAuditLogs.ToArrayAsync()); Assert.Empty(await f.Db.OrderOperations.ToArrayAsync());
        Assert.Empty(await f.Db.CustomerProductSetupRequests.ToArrayAsync()); Assert.Empty(await f.Db.CustomerProductSetupOrders.ToArrayAsync());
    }

    [Fact]
    public async Task Completion_RequiresConfiguredProfileAndEffectivePrice_AndPreservesRealZeroSnapshots()
    {
        await using var f = await Fixture.Create();
        var originalOrder = await f.Capture(false);
        var setup = Assert.Single(await f.Setup.GetOpenAsync(Ct));
        var missing = await f.Setup.PreviewAsync(setup.Id, Ct);
        Assert.False(missing.CanComplete); Assert.Contains(missing.Blockers, x => x.Contains("perfil", StringComparison.OrdinalIgnoreCase) || x.Contains("configuración"));
        await f.ConfigureExtra(0);
        f.Clock.Now = f.Clock.Now.AddDays(1);
        var alreadyPricedOrder = await f.Capture(false);
        await f.Price(22, expectedVersion: 1, effectiveFrom: f.Clock.Today.AddDays(-1));
        var preview = await f.Setup.PreviewAsync(setup.Id, Ct);
        Assert.True(preview.CanComplete); Assert.Equal(2, preview.Lines.Count);
        Assert.True(preview.Lines.Single(x => x.OrderId == originalOrder).ApplyPrice);
        Assert.False(preview.Lines.Single(x => x.OrderId == alreadyPricedOrder).ApplyPrice);
        var request = new CompleteProductSetupRequest(Guid.NewGuid(), preview.Version, preview.Fingerprint, "Tarifa y perfil ficticios revisados");
        var result = await f.Setup.CompleteAsync(setup.Id, request, Actor, Ct);
        var replay = await f.Setup.CompleteAsync(setup.Id, request, Actor, Ct);
        Assert.True(replay.Replayed); Assert.False(result.Replayed); Assert.Equal(1, result.PricedLines);
        f.Db.ChangeTracker.Clear();
        var first = await f.Db.OrderLines.SingleAsync(x => x.OrderId == originalOrder);
        var existing = await f.Db.OrderLines.SingleAsync(x => x.OrderId == alreadyPricedOrder);
        Assert.Equal(22, first.UnitPriceSnapshot); Assert.Equal(44, first.LineAmount);
        Assert.Equal(0, existing.UnitPriceSnapshot); Assert.Equal(0, existing.LineAmount);
        Assert.Null(first.AssignedMachineId); Assert.Null(existing.AssignedMachineId);
        Assert.Empty(await f.Setup.GetOpenAsync(Ct));
        Assert.Single(await f.Db.OrderAuditLogs.Where(x => x.EventType == OrderAuditEventType.OrderLineInitialPriceResolved).ToArrayAsync());
        var reviewAudit = await f.Db.AuditLogs.SingleAsync(x => x.EventType == "CustomerProductSetupCompleted");
        using var metadata = JsonDocument.Parse(reviewAudit.MetadataJson!);
        Assert.Equal(request.Reason, metadata.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, await f.Db.CustomerProductPriceRevisions.CountAsync());
        Assert.Empty(await f.Db.OrderChangeNotices.ToArrayAsync());
    }

    [Fact]
    public async Task ChangedTariffOrOrderAfterPreview_RequiresANewReviewWithoutPricingAnyLine()
    {
        await using var f = await Fixture.Create();
        await f.Capture(false); await f.ConfigureExtra(12);
        var setup = Assert.Single(await f.Setup.GetOpenAsync(Ct));
        var preview = await f.Setup.PreviewAsync(setup.Id, Ct);
        await f.Price(14, expectedVersion: 1);
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Setup.CompleteAsync(setup.Id,
            new(Guid.NewGuid(), preview.Version, preview.Fingerprint, "Vista previa anterior"), Actor, Ct));
        Assert.Null((await f.Db.OrderLines.SingleAsync()).UnitPriceSnapshot);
        Assert.False((await f.Db.CustomerProductSetupRequests.SingleAsync()).IsResolved);
    }

    [Fact]
    public async Task ReplacingAnEditedOrderLine_RetainsFollowUpAndPricesOnlyTheCurrentLine()
    {
        await using var f = await Fixture.Create();
        var orderId = await f.Capture(false);
        await f.ConfigureExtra(12);
        var previous = await f.Db.OrderLines.SingleAsync();
        f.Db.OrderLines.Remove(previous); await f.Db.SaveChangesAsync();
        var current = new OrderLine { Id = Guid.NewGuid(), OrderId = orderId, ProductId = f.Extra.Id,
            ProductNameSnapshot = f.Extra.Name, UnitCode = "package", Quantity = 5 };
        f.Db.OrderLines.Add(current); (await f.Db.Orders.SingleAsync()).Version++;
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var setup = Assert.Single(await f.Setup.GetOpenAsync(Ct));
        Assert.Equal(5, Assert.Single(setup.Orders).Quantity);
        var preview = await f.Setup.PreviewAsync(setup.Id, Ct);
        Assert.Equal(current.Id, Assert.Single(preview.Lines).OrderLineId);
        await f.Setup.CompleteAsync(setup.Id, new(Guid.NewGuid(), preview.Version, preview.Fingerprint,
            "Revisión de partida corregida ficticia"), Actor, Ct);
        var priced = await f.Db.OrderLines.SingleAsync();
        Assert.Equal(current.Id, priced.Id); Assert.Equal(60, priced.LineAmount);
    }

    [Fact]
    public async Task OlderProfileRequest_OmittingInheritanceFlagPreservesAnExplicitNoMachineRelationship()
    {
        await using var f = await Fixture.Create();
        await f.ConfigureExtra(12);
        var service = new AdminCustomerCatalogService(new EfCustomerRepository(f.Db), new EfProductRepository(f.Db),
            new EfMachineRepository(f.Db), new EfAuditLogRepository(f.Db), f.Clock);
        await service.ReplaceFrequentProductsAsync(f.Customer.Id, new([
            new(f.Habitual.Id, null, 1, true), new(f.Extra.Id, null, 2, true)], false, f.Customer.OrderingProfileVersion), Actor, Ct);
        f.Db.ChangeTracker.Clear();
        var rows = await f.Db.CustomerFrequentProducts.ToArrayAsync();
        Assert.True(rows.Single(x => x.ProductId == f.Habitual.Id).UseCustomerDefaultMachine);
        Assert.False(rows.Single(x => x.ProductId == f.Extra.Id).UseCustomerDefaultMachine);
        Assert.Null(rows.Single(x => x.ProductId == f.Extra.Id).PreferredMachineId);
    }

    [Fact]
    public async Task CompletionAuditFailure_RollsBackInitialPriceAndRequestResolution()
    {
        await using var f = await Fixture.Create();
        await f.Capture(false); await f.ConfigureExtra(12);
        var setup = Assert.Single(await f.Setup.GetOpenAsync(Ct));
        var preview = await f.Setup.PreviewAsync(setup.Id, Ct);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fixture_resolution_failure BEFORE INSERT ON AuditLogs WHEN NEW.EventType = 'CustomerProductSetupCompleted' BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => f.Setup.CompleteAsync(setup.Id,
            new(Guid.NewGuid(), preview.Version, preview.Fingerprint, "Revisión ficticia"), Actor, Ct));
        Assert.Null((await f.Db.OrderLines.SingleAsync()).UnitPriceSnapshot);
        Assert.False((await f.Db.CustomerProductSetupRequests.SingleAsync()).IsResolved);
        Assert.Empty(await f.Db.OrderAuditLogs.Where(x => x.EventType == OrderAuditEventType.OrderLineInitialPriceResolved).ToArrayAsync());
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-02T08:00:00-06:00");
        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public PedidosDbContext Db { get; }
        public Clock Clock { get; } = new();
        public Customer Customer { get; } = new() { Id = Guid.NewGuid(), Name = "Cliente de prueba", IsActive = true, OrderingProfileVersion = 1, IsOrderProfileApproved = true };
        public Product Habitual { get; } = new() { Id = Guid.NewGuid(), Name = "Tortilla habitual de prueba", UnitCode = "kg", IsActive = true };
        public Product Extra { get; } = new() { Id = Guid.NewGuid(), Name = "Burrera de prueba", UnitCode = "package", IsActive = true, Description = "INTERNAL-FIXTURE only" };
        public Machine Machine { get; } = Machine.Create(17, "Máquina ficticia");
        public CustomerProductSetupService Setup { get; }
        public CustomerOrderCaptureService CustomerCapture { get; }
        public AdminOrderCaptureService AdminCapture { get; }
        private CustomerProductPricingService Pricing { get; }
        private Fixture(SqliteConnection connection, PedidosDbContext db)
        {
            this.connection = connection; Db = db;
            var customers = new EfCustomerRepository(db); var products = new EfProductRepository(db);
            var commercial = new EfCommercialCatalogRepository(db); var orders = new EfOrderRepository(db);
            var machines = new EfMachineRepository(db); var channels = new EfSalesChannelRepository(db);
            var audit = new EfOrderAuditLogRepository(db); var catalogAudit = new EfAuditLogRepository(db);
            var operations = new EfOrderOperationStore(db);
            Setup = new(customers, products, new EfCustomerProductSetupRepository(db), commercial, audit, catalogAudit, operations, Clock);
            var notices = new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(db), machines, customers);
            var service = new AdminOrderService(orders, audit, customers, products, machines, channels, Clock, notices, commercial);
            AdminCapture = new(customers, orders, audit, operations, Clock, service, Setup);
            CustomerCapture = new(customers, products, machines, channels, orders, audit, operations, Clock, commercial, Setup);
            Pricing = new(customers, products, commercial, catalogAudit, Clock);
        }
        public async Task<Guid> Capture(bool admin, Guid? operation = null, bool withHabitual = false)
        {
            var version = await Db.Customers.AsNoTracking().Select(x => x.OrderingProfileVersion).SingleAsync();
            var lines = new List<ManualOrderCaptureLine> { new(Extra.Id, 2, "package") };
            if (withHabitual) lines.Add(new(Habitual.Id, 10, "kg"));
            if (admin) return (await AdminCapture.SubmitAsync(Customer.Id,
                new(operation ?? Guid.NewGuid(), version, Clock.Today, Lines: lines), Actor, Ct)).Order.OrderId;
            return (await CustomerCapture.SubmitAsync(Customer.Id,
                new(operation ?? Guid.NewGuid(), version, Clock.Today, Lines: lines), Ct)).Order.OrderId;
        }
        public async Task ConfigureExtra(decimal price)
        {
            Db.CustomerFrequentProducts.Add(new() { CustomerId = Customer.Id, ProductId = Extra.Id, IsActive = true,
                SortOrder = 2, UseCustomerDefaultMachine = false });
            Customer.RecordOrderingProfileChange(false, Clock.Now); await Db.SaveChangesAsync();
            await Price(price);
        }
        public Task<CustomerProductPriceResponse> Price(decimal amount, int expectedVersion = 0, DateOnly? effectiveFrom = null)
            => Pricing.ChangeAsync(Customer.Id, Extra.Id, new(amount, effectiveFrom ?? Clock.Today,
                "Tarifa ficticia", expectedVersion, Guid.NewGuid(), "package"), Actor, Ct);
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync(); var fixture = new Fixture(connection, db);
            db.Customers.Add(fixture.Customer); db.Products.AddRange(fixture.Habitual, fixture.Extra); db.Machines.Add(fixture.Machine);
            db.CustomerFrequentProducts.Add(new() { CustomerId = fixture.Customer.Id, ProductId = fixture.Habitual.Id,
                IsActive = true, SortOrder = 1, InterpretationPosition = 1 });
            db.CustomerMachineAssignments.Add(new() { CustomerId = fixture.Customer.Id, MachineId = fixture.Machine.Id, IsActive = true, IsDefault = true });
            db.SalesChannels.AddRange(new SalesChannel { Id = Guid.NewGuid(), Name = "Cliente ficticio", Type = SalesChannelType.Customer },
                new SalesChannel { Id = Guid.NewGuid(), Name = "Administración ficticia", Type = SalesChannelType.AdminManualCapture, IsInternal = true });
            await db.SaveChangesAsync(); return fixture;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
