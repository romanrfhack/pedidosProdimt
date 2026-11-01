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

public sealed class CommercialCatalogTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateOnly Day = new(2026, 11, 1);
    private static readonly AdminActorContext Actor = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Administracion de prueba");
    private static ChangeCustomerProductPriceRequest Change(decimal? amount, int version = 0, DateOnly? date = null, Guid? operation = null)
        => new(amount, date ?? Day, "Cambio confirmado en prueba ficticia", version, operation ?? Guid.NewGuid(), "kg");

    [Fact]
    public async Task PricesArePerCustomer_AndFutureEffectiveDateDoesNotOverwriteTodayOrPriorRevisions()
    {
        await using var f = await Fixture.Create();
        f.Db.CustomerFrequentProducts.Add(new() { CustomerId = f.B.Id, ProductId = f.P.Id, SortOrder = 2 });
        await f.Db.SaveChangesAsync();
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(20), Actor, Ct);
        f.Clock.Now = f.Clock.Now.AddMinutes(5);
        var otherActor = new AdminActorContext("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Otro administrador de prueba");
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(22, 1, Day.AddDays(4)), otherActor, Ct);
        await f.Prices.ChangeAsync(f.B.Id, f.P.Id, Change(25), Actor, Ct);
        var today = (await f.Prices.GetAsync(f.A.Id, Day, Ct)).Single();
        var future = (await f.Prices.GetAsync(f.A.Id, Day.AddDays(4), Ct)).Single();
        Assert.Equal(20, today.UnitPrice); Assert.Equal(22, future.UnitPrice);
        Assert.Equal(25, (await f.Prices.GetAsync(f.B.Id, Day, Ct)).Single(x => x.ProductId == f.P.Id).UnitPrice);
        Assert.Equal(2, today.History.Count);
        Assert.Equal(otherActor.ActorId, today.History[0].ActorId);
        Assert.Equal(Actor.ActorId, today.History[1].ActorId);
        Assert.Equal(Day.AddDays(3), today.History[1].EffectiveUntil);
        Assert.True(today.History[0].RecordedAt > today.History[1].RecordedAt);
        Assert.Equal(20, (await f.Db.CustomerProductPriceRevisions.AsNoTracking().SingleAsync(x => x.Version == 1 && x.ActorId == Actor.ActorId && x.PriceBookId == f.Db.CustomerProductPriceBooks.Single(b => b.CustomerId == f.A.Id).Id)).UnitPrice);
    }

    [Fact]
    public async Task SameDayCorrectionAppendsHistory_AndSameOperationIsIdempotentWithConflictOnDifferentPayload()
    {
        await using var f = await Fixture.Create();
        var request = Change(20);
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, request, Actor, Ct);
        Assert.True((await f.Prices.ChangeAsync(f.A.Id, f.P.Id, request, Actor, Ct)).Replayed);
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Prices.ChangeAsync(f.A.Id, f.P.Id, request with { UnitPrice = 21 }, Actor, Ct));
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(21, 0), Actor, Ct));
        var changed = await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(21, 1), Actor, Ct);
        Assert.Equal(21, changed.UnitPrice); Assert.Equal(2, changed.History.Count);
        Assert.True(changed.History.Single(x => x.Version == 1).SupersededForDate);
        Assert.False(changed.History.Single(x => x.Version == 2).SupersededForDate);
        Assert.Equal(2, await f.Db.CustomerProductPriceRevisions.CountAsync());
        Assert.Equal(2, await f.Db.AuditLogs.CountAsync(x => x.EventType == "CustomerProductPriceChanged"));
    }

    [Fact]
    public async Task SavingIdenticalPriceDateAndReasonAgainDoesNotInventAnotherChange()
    {
        await using var f = await Fixture.Create();
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(20), Actor, Ct);
        var same = await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(20, 1), Actor, Ct);
        Assert.True(same.Unchanged); Assert.Equal(1, same.Version);
        Assert.Single(await f.Db.CustomerProductPriceRevisions.ToArrayAsync());
    }

    [Fact]
    public async Task CapturedPriceSurvivesLaterPriceChangeAndQuantityCorrection_AndNewOrdersUseNewPrice()
    {
        await using var f = await Fixture.Create();
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(20), Actor, Ct);
        var original = await f.Submit(f.A.Id, f.P.Id, 10);
        var oldRevision = original.Lines.Single().PriceRevisionId;
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(25, 1), Actor, Ct);
        Assert.Equal(200, (await f.Orders.GetDetailAsync(original.OrderId, Ct)).TotalAmount);
        Assert.Equal(250, (await f.Submit(f.A.Id, f.P.Id, 10)).TotalAmount);
        var corrected = await f.Daily.CorrectAsync(original.OrderId, new(Guid.NewGuid().ToString(), original.Version,
            "Cliente ajusta cantidad", [new(f.P.Id, 12, null)]), Actor, Ct);
        Assert.Equal(240, corrected.TotalAmount);
        Assert.Equal(20, corrected.Lines.Single().UnitPrice);
        Assert.Equal(oldRevision, corrected.Lines.Single().PriceRevisionId);
        var log = await f.Db.OrderAuditLogs.SingleAsync(x => x.OrderId == original.OrderId && x.EventType == OrderAuditEventType.OrderCorrected);
        Assert.Contains("unitPriceSnapshot", log.MetadataJson);
    }

    [Fact]
    public async Task PriceUsesServiceDate_AndExplicitZeroIsDifferentFromUnpricedOrWithdrawn()
    {
        await using var f = await Fixture.Create();
        Assert.Null((await f.Submit(f.A.Id, f.P.Id, 1)).TotalAmount);
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(0), Actor, Ct);
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(30, 1, Day.AddDays(1)), Actor, Ct);
        Assert.Equal(0, (await f.Submit(f.A.Id, f.P.Id, 2)).TotalAmount);
        Assert.Equal(60, (await f.Submit(f.A.Id, f.P.Id, 2, Day.AddDays(1))).TotalAmount);
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(null, 2, Day.AddDays(2)), Actor, Ct);
        var unknown = await f.Submit(f.A.Id, f.P.Id, 2, Day.AddDays(2));
        Assert.True(unknown.HasUnpricedLines); Assert.Null(unknown.TotalAmount);
        Assert.Null(unknown.Lines.Single().PriceRevisionId);
    }

    [Fact]
    public async Task NewUnitDoesNotApplyTheOldUnitPrice_AndUnitObservedByEditorMustMatch()
    {
        await using var f = await Fixture.Create();
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(20), Actor, Ct);
        f.P.SetUnitCode("piece"); await f.Db.SaveChangesAsync();
        Assert.Null((await f.Prices.GetAsync(f.A.Id, Day, Ct)).Single().UnitPrice);
        Assert.Null((await f.Submit(f.A.Id, f.P.Id, 2)).TotalAmount);
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(2, 1), Actor, Ct));
        Assert.Single(await f.Db.CustomerProductPriceRevisions.ToArrayAsync());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.00001")]
    [InlineData("1000000000")]
    public async Task InvalidPriceDoesNotCreateAssignmentOrHistory(string amount)
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Prices.ChangeAsync(f.A.Id, f.P.Id,
            Change(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)), Actor, Ct));
        Assert.Equal(0, await f.Db.CustomerProductPriceBooks.CountAsync());
        Assert.Equal(0, await f.Db.CustomerProductPriceRevisions.CountAsync());
    }

    [Fact]
    public async Task PriceAuditFailureDoesNotPersistPrice_AndMissingActorOrReasonIsRejected()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(20), null, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(20) with { Reason = " " }, Actor, Ct));
        var failed = new CustomerProductPricingService(f.Customers, f.Products, f.Commercial, new FailedAudit(), f.Clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.ChangeAsync(f.A.Id, f.P.Id, Change(20), Actor, Ct));
        Assert.Equal(0, await f.Db.CustomerProductPriceBooks.CountAsync());
        Assert.Equal(0, await f.Db.CustomerProductPriceRevisions.CountAsync());
    }

    [Fact]
    public async Task ConsolidationKeepsEachCustomerConfigurationAndPriceHistory_WithoutRewritingOrders()
    {
        await using var f = await Fixture.Create();
        Assert.Single(await f.Merge.GetCandidatesAsync(Ct));
        await f.Prices.ChangeAsync(f.A.Id, f.P.Id, Change(20), Actor, Ct);
        await f.Prices.ChangeAsync(f.B.Id, f.Q.Id, Change(22), Actor, Ct);
        var original = await f.Submit(f.B.Id, f.Q.Id, 10);
        var originalBook = await f.Db.CustomerProductPriceBooks.AsNoTracking().SingleAsync(x => x.CustomerId == f.B.Id);
        var originalRevision = await f.Db.CustomerProductPriceRevisions.AsNoTracking().SingleAsync(x => x.PriceBookId == originalBook.Id);
        var before = (await f.Customers.GetAllFrequentProductsAsync(f.B.Id, Ct)).Single();
        var preview = await f.Merge.PreviewAsync(f.Selection, Ct);
        Assert.True(preview.CanApply); Assert.Equal(2, preview.Customers.Count);
        var request = f.Apply(preview);
        var merged = await f.Merge.ApplyAsync(request, Actor, Ct);
        Assert.Equal(1, merged.ArchivedProducts); Assert.Equal(2, merged.UpdatedProfiles);
        Assert.True((await f.Merge.ApplyAsync(request, Actor, Ct)).Replayed);
        var configured = (await f.Customers.GetAllFrequentProductsAsync(f.B.Id, Ct)).Single();
        Assert.Equal(f.P.Id, configured.ProductId); Assert.Equal(before.DefaultQuantity, configured.DefaultQuantity);
        Assert.Equal(before.SortOrder, configured.SortOrder); Assert.Equal(before.InterpretationPosition, configured.InterpretationPosition);
        Assert.Equal(before.CaptureAlias, configured.CaptureAlias); Assert.Equal(before.PreferredMachineId, configured.PreferredMachineId);
        var archived = await f.Db.Products.AsNoTracking().SingleAsync(x => x.Id == f.Q.Id);
        Assert.False(archived.IsActive); Assert.Equal(f.P.Id, archived.MergedIntoProductId);
        Assert.Throws<ArgumentException>(() => archived.Activate());
        var currentB = (await f.Prices.GetAsync(f.B.Id, Day, Ct)).Single();
        Assert.Equal(f.P.Id, currentB.ProductId); Assert.Equal(22, currentB.UnitPrice);
        Assert.Equal(originalRevision.ProductNameSnapshot, currentB.History.Single().ProductName);
        Assert.Equal(originalBook.Id, (await f.Db.CustomerProductPriceBooks.AsNoTracking().SingleAsync(x => x.CustomerId == f.B.Id)).Id);
        var historical = await f.Orders.GetDetailAsync(original.OrderId, Ct);
        Assert.Equal(f.Q.Id, historical.Lines.Single().ProductId);
        Assert.Equal(original.Lines.Single().ProductName, historical.Lines.Single().ProductName);
        Assert.Equal(220, historical.TotalAmount);
        Assert.False((await f.Customers.GetByIdAsync(f.B.Id, Ct))!.IsOrderProfileApproved);
        Assert.Single(await f.Db.ProductConsolidationOperations.ToArrayAsync());
        Assert.Empty(await f.Merge.GetCandidatesAsync(Ct));
    }

    [Fact]
    public async Task ExistingCanonicalCustomersKeepTheirProfileVersionWhenTheirConfigurationDoesNotChange()
    {
        await using var f = await Fixture.Create();
        f.P.Update("Molde 17", null); await f.Db.SaveChangesAsync();
        var originalVersion = f.A.OrderingProfileVersion;
        var preview = await f.Merge.PreviewAsync(f.Selection, Ct);
        Assert.Single(preview.Customers);
        var result = await f.Merge.ApplyAsync(f.Apply(preview), Actor, Ct);
        Assert.Equal(1, result.UpdatedProfiles);
        var current = (await f.Customers.GetByIdAsync(f.A.Id, Ct))!;
        Assert.Equal(originalVersion, current.OrderingProfileVersion); Assert.True(current.IsOrderProfileApproved);
    }

    [Fact]
    public async Task CorrectionUsingCanonicalIdPreservesTheOriginalHistoricalPriceAndIdentity()
    {
        await using var f = await Fixture.Create();
        await f.Prices.ChangeAsync(f.B.Id, f.Q.Id, Change(22), Actor, Ct);
        var original = await f.Submit(f.B.Id, f.Q.Id, 10);
        await f.Merge.ApplyAsync(f.Apply(await f.Merge.PreviewAsync(f.Selection, Ct)), Actor, Ct);
        await f.Prices.ChangeAsync(f.B.Id, f.P.Id, Change(25, 1), Actor, Ct);
        var corrected = await f.Daily.CorrectAsync(original.OrderId, new(Guid.NewGuid().ToString(), original.Version,
            "Ajuste conservando precio historico", [new(f.P.Id, 12, null)]), Actor, Ct);
        Assert.Equal(f.Q.Id, corrected.Lines.Single().ProductId);
        Assert.Equal(22, corrected.Lines.Single().UnitPrice); Assert.Equal(264, corrected.TotalAmount);
    }

    [Fact]
    public async Task UnitOrProfileCollisionBlocksConsolidationWithoutGuessingQuantitiesOrPositions()
    {
        await using var f = await Fixture.Create();
        f.Q.SetUnitCode("piece"); await f.Db.SaveChangesAsync();
        Assert.False((await f.Merge.PreviewAsync(f.Selection, Ct)).CanApply);
        f.Q.SetUnitCode("kg");
        f.Db.CustomerFrequentProducts.Add(new() { CustomerId = f.B.Id, ProductId = f.P.Id, SortOrder = 3, InterpretationPosition = 2 });
        await f.Db.SaveChangesAsync();
        var preview = await f.Merge.PreviewAsync(f.Selection, Ct);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, x => x.Contains(f.B.Name));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Merge.ApplyAsync(f.Apply(preview), Actor, Ct));
        Assert.Equal(0, await f.Db.ProductConsolidationOperations.CountAsync());
        Assert.True((await f.Db.Products.AsNoTracking().SingleAsync(x => x.Id == f.Q.Id)).IsActive);
    }

    [Fact]
    public async Task TwoHistoricalPriceAssignmentsForSameCustomerBlockAutomaticMerge()
    {
        await using var f = await Fixture.Create();
        f.Db.CustomerFrequentProducts.Add(new() { CustomerId = f.B.Id, ProductId = f.P.Id, SortOrder = 3 });
        await f.Db.SaveChangesAsync();
        await f.Prices.ChangeAsync(f.B.Id, f.P.Id, Change(20), Actor, Ct);
        await f.Prices.ChangeAsync(f.B.Id, f.Q.Id, Change(22), Actor, Ct);
        f.Db.CustomerFrequentProducts.Remove(await f.Db.CustomerFrequentProducts.SingleAsync(x => x.CustomerId == f.B.Id && x.ProductId == f.P.Id));
        await f.Db.SaveChangesAsync();
        var preview = await f.Merge.PreviewAsync(f.Selection, Ct);
        Assert.False(preview.CanApply); Assert.Contains(preview.Errors, x => x.Contains("historiales de precio"));
        Assert.Equal(2, await f.Db.CustomerProductPriceBooks.CountAsync());
    }

    [Fact]
    public async Task ProfileOrPriceChangedAfterPreviewRequiresAReviewedPreviewAgain()
    {
        await using var f = await Fixture.Create();
        var preview = await f.Merge.PreviewAsync(f.Selection, Ct);
        await f.Prices.ChangeAsync(f.B.Id, f.Q.Id, Change(20), Actor, Ct);
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Merge.ApplyAsync(f.Apply(preview), Actor, Ct));
        preview = await f.Merge.PreviewAsync(f.Selection, Ct);
        f.B.RecordOrderingProfileChange(false, f.Clock.Now); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<CatalogConflictException>(() => f.Merge.ApplyAsync(f.Apply(preview), Actor, Ct));
        Assert.Equal(0, await f.Db.ProductConsolidationOperations.CountAsync());
    }

    [Fact]
    public async Task ConsolidatedExternalCodeRemainsAnAliasForDailyImports()
    {
        await using var f = await Fixture.Create();
        await f.Merge.ApplyAsync(f.Apply(await f.Merge.PreviewAsync(f.Selection, Ct)), Actor, Ct);
        var csv = "externalOrderKey,customerExternalCode,serviceDate,action,productExternalCode,quantity,unitCode,notes\n"
            + "FAKE-ORDER,FAKE-B,2026-11-01,order,FAKE-Q,2,kg,\n";
        var result = await f.Daily.ApplyAsync(new(csv, "fixture", "batch-1"), Actor, Ct);
        Assert.True(result.Applied);
        Assert.Equal(f.P.Id, result.Orders.Single().Lines.Single().ProductId);
    }

    private sealed class FailedAudit : IAuditLogRepository
    {
        public Task AddAsync(AuditLog log, CancellationToken token) => throw new InvalidOperationException("Fallo de auditoria ficticio");
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 15, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(Now.AddHours(-6).DateTime);
        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.AddHours(-6).DateTime);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public PedidosDbContext Db { get; }
        public TestClock Clock { get; } = new();
        public Customer A { get; } = new() { Id = Guid.NewGuid(), Name = "Cliente Alpha", ExternalCode = "FAKE-A", IsActive = true, OrderingProfileVersion = 1, IsOrderProfileApproved = true };
        public Customer B { get; } = new() { Id = Guid.NewGuid(), Name = "Cliente Beta", ExternalCode = "FAKE-B", IsActive = true, OrderingProfileVersion = 1, IsOrderProfileApproved = true };
        public Product P { get; } = Product.Create("Molde 17 (Cliente Alpha)", "Producto de prueba");
        public Product Q { get; } = Product.Create("Molde 17 (Cliente Beta)", "Producto de prueba");
        public EfCustomerRepository Customers { get; }
        public EfProductRepository Products { get; }
        public EfCommercialCatalogRepository Commercial { get; }
        public CustomerProductPricingService Prices { get; }
        public ProductConsolidationService Merge { get; }
        public AdminOrderService Orders { get; }
        public AdminDailyOrderService Daily { get; }
        public PreviewProductConsolidationRequest Selection => new(P.Id, [Q.Id], "Molde 17", null);
        public ApplyProductConsolidationRequest Apply(ProductConsolidationPreview preview) => new(P.Id, [Q.Id], "Molde 17", null, preview.Fingerprint, "Mismo producto confirmado", Guid.NewGuid());
        private Fixture(SqliteConnection connection, PedidosDbContext db)
        {
            this.connection = connection; Db = db;
            Customers = new(db); Products = new(db); Commercial = new(db);
            var audit = new EfAuditLogRepository(db);
            Prices = new(Customers, Products, Commercial, audit, Clock);
            Merge = new(Products, Customers, Commercial, audit, Clock);
            var orderRepo = new EfOrderRepository(db); var orderAudit = new EfOrderAuditLogRepository(db);
            var machines = new EfMachineRepository(db); var channels = new EfSalesChannelRepository(db);
            var notices = new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(db), machines, Customers);
            Orders = new(orderRepo, orderAudit, Customers, Products, machines, channels, Clock, notices, Commercial);
            Daily = new(orderRepo, orderAudit, Customers, Products, machines, channels, Clock, new EfOrderOperationStore(db), notices, Commercial);
        }
        public async Task<AdminOrderDetailResponse> Submit(Guid customerId, Guid productId, decimal quantity, DateOnly? date = null)
        {
            var submitted = await Orders.SubmitCustomerOrderAsync(customerId,
                new([new(productId, quantity, null)], null, null, null, null, null, date ?? Day), Actor, Ct);
            return await Orders.GetDetailAsync(submitted.OrderId, Ct);
        }

        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var f = new Fixture(connection, db);
            f.P.SetUnitCode("kg"); f.Q.SetUnitCode("kg"); f.P.SetExternalCode("FAKE-P"); f.Q.SetExternalCode("FAKE-Q");
            db.Customers.AddRange(f.A, f.B); db.Products.AddRange(f.P, f.Q);
            var machine = Machine.Create(99, "Maquina de prueba"); db.Machines.Add(machine);
            db.SalesChannels.AddRange(new SalesChannel { Id = Guid.NewGuid(), Type = SalesChannelType.Customer, Name = "Cliente", IsInternal = false },
                new SalesChannel { Id = Guid.NewGuid(), Type = SalesChannelType.AdminManualCapture, Name = "Administracion", IsInternal = true });
            db.CustomerFrequentProducts.AddRange(
                new() { CustomerId = f.A.Id, ProductId = f.P.Id, SortOrder = 2, InterpretationPosition = 1, CaptureAlias = "AA", DefaultQuantity = 3, PreferredMachineId = machine.Id },
                new() { CustomerId = f.B.Id, ProductId = f.Q.Id, SortOrder = 3, InterpretationPosition = 1, CaptureAlias = "BB", DefaultQuantity = 4, PreferredMachineId = machine.Id });
            await db.SaveChangesAsync(); return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
