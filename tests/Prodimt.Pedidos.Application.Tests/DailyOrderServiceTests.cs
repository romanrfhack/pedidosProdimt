using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class DailyOrderServiceTests
{
    private const string Header = "externalOrderKey,serviceDate,customerExternalCode,action,productExternalCode,quantity,unitCode,notes";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly AdminActorContext Actor = new("admin-test", "Administrador de prueba");

    [Fact]
    public async Task Preview_GroupsLinesAndPreservesUnits_WithoutWritingOrdersOrAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ValidateAsync(Request(
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,Primera variante",
            "ORDER-1,2026-10-25,C-TEST,order,P-PACK,2,package,Segunda variante"), Ct);

        Assert.True(result.CanApply);
        Assert.False(result.Applied);
        Assert.Empty(result.Errors);
        var order = Assert.Single(result.Orders);
        Assert.Equal(fixture.Customer.Id, order.CustomerId);
        Assert.Equal(new DateOnly(2026, 10, 25), order.ServiceDate);
        Assert.Equal(2, order.Lines.Count);
        Assert.Contains(order.Lines, x => x.UnitCode == "kg" && x.Quantity == 15);
        Assert.Contains(order.Lines, x => x.UnitCode == "package" && x.Quantity == 2);
        Assert.Empty(await fixture.Db.Orders.ToArrayAsync());
        Assert.Empty(await fixture.Db.OrderAuditLogs.ToArrayAsync());
        Assert.Equal(0, await fixture.OperationCountAsync());
    }

    [Theory]
    [InlineData("ORDER-1,2026-10-25,C-TEST,order,P-KG,5,package,")]
    [InlineData("ORDER-1,2026-10-25,C-TEST,order,P-KG,0,kg,")]
    [InlineData("ORDER-1,2026-10-25,C-TEST,order,P-KG,-1,kg,")]
    [InlineData("ORDER-1,2026-10-25,C-TEST,order,P-KG,X,kg,")]
    [InlineData("ORDER-1,2026-10-25,C-TEST,order,P-KG,1.234,kg,")]
    [InlineData("ORDER-1,25/10/2026,C-TEST,order,P-KG,5,kg,")]
    [InlineData("ORDER-1,2026-10-25,C-MISSING,order,P-KG,5,kg,")]
    [InlineData("ORDER-1,2026-10-25,C-TEST,order,P-MISSING,5,kg,")]
    [InlineData("ORDER-1,2026-10-25,C-TEST,no_order,P-KG,5,kg,")]
    public async Task InvalidRow_BlocksEntireApply_WithoutPartialWrites(string row)
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ApplyAsync(Request(
            "VALID-1,2026-10-25,C-OTHER,order,P-KG,3,kg,",
            row), Actor, Ct);

        Assert.False(result.CanApply);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(await fixture.Db.Orders.ToArrayAsync());
        Assert.Empty(await fixture.Db.OrderLines.ToArrayAsync());
        Assert.Empty(await fixture.Db.OrderAuditLogs.ToArrayAsync());
        Assert.Equal(0, await fixture.OperationCountAsync());
    }

    [Fact]
    public async Task MixedActionsOrCustomersUnderOneSourceKey_AreNotMerged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var mixedAction = await fixture.Service.ValidateAsync(Request(
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,5,kg,",
            "ORDER-1,2026-10-25,C-TEST,no_order,,,,"), Ct);
        var mixedCustomer = await fixture.Service.ValidateAsync(Request(
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,5,kg,",
            "ORDER-1,2026-10-25,C-OTHER,order,P-PACK,2,package,"), Ct);

        Assert.False(mixedAction.CanApply);
        Assert.False(mixedCustomer.CanApply);
        Assert.NotEmpty(mixedAction.Errors);
        Assert.NotEmpty(mixedCustomer.Errors);
        Assert.Empty(await fixture.Db.Orders.ToArrayAsync());
    }

    [Fact]
    public async Task DuplicateProductInsideOrder_IsNotSilentlySummed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ValidateAsync(Request(
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,5,kg,",
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,2,kg,"), Ct);

        Assert.False(result.CanApply);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Apply_PersistsExplicitServiceDateAndMachinePerProduct()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ApplyAsync(Request(
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,",
            "ORDER-1,2026-10-25,C-TEST,order,P-PACK,2,package,"), Actor, Ct);

        Assert.True(result.Applied);
        fixture.Db.ChangeTracker.Clear();
        var order = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 25), order.OrderDate);
        Assert.Equal("R-DEMO", order.RouteCode);
        Assert.Equal("Cliente Demo", order.CustomerNameSnapshot);
        Assert.Contains(order.Lines, x => x.ProductId == fixture.KgProduct.Id && x.AssignedMachineId == fixture.KgMachine.Id);
        Assert.Contains(order.Lines, x => x.ProductId == fixture.PackageProduct.Id && x.AssignedMachineId == fixture.PackageMachine.Id);
        Assert.NotEmpty(await fixture.Db.OrderAuditLogs.ToArrayAsync());
    }

    [Fact]
    public async Task ExplicitNoOrder_HasNoLinesAndIsNotAZeroQuantityOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ApplyAsync(Request("NONE-1,2026-10-25,C-TEST,no_order,,,,Hoy no requiere"), Actor, Ct);

        Assert.True(result.Applied);
        var order = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(OrderStatus.NoOrder, order.Status);
        Assert.Empty(order.Lines);
        Assert.False(order.RequiresAdminReview);
        Assert.Equal("Hoy no requiere", order.InternalNotes);
        var audit = await fixture.Db.OrderAuditLogs.SingleAsync();
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Hoy no requiere", metadata.RootElement.GetProperty("snapshot").GetProperty("internalNotes").GetString());
    }

    [Fact]
    public async Task TodayImportAfterCutoff_WarnsAndStaysPendingReview()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.SetTime(new TimeOnly(10, 1));
        var request = Request("LATE-1,2026-10-24,C-TEST,order,P-KG,5,kg,");

        var preview = await fixture.Service.ValidateAsync(request, Ct);
        Assert.True(preview.CanApply);
        Assert.Contains(preview.Warnings, x => x.Contains("tard", StringComparison.OrdinalIgnoreCase));
        var result = await fixture.Service.ApplyAsync(request, Actor, Ct);

        Assert.True(result.Applied);
        var order = await fixture.Db.Orders.SingleAsync();
        Assert.True(order.IsLate);
        Assert.True(order.RequiresAdminReview);
        Assert.Equal(OrderStatus.PendingAdminReview, order.Status);
        Assert.Equal(AdminReviewReason.LateSubmission, order.AdminReviewReason);
    }

    [Theory]
    [InlineData(23, true)]
    [InlineData(24, false)]
    [InlineData(25, false)]
    public async Task AdminCapture_EvaluatesCutoffAgainstServiceDate(int day, bool isLate)
    {
        await using var fixture = await Fixture.CreateAsync();
        var serviceDate = new DateOnly(2026, 10, day);
        var result = await fixture.AdminService.SubmitCustomerOrderAsync(fixture.Customer.Id,
            new AdminSubmitCustomerOrderRequest([new(fixture.KgProduct.Id, 5, null)], null, null, null, null, null, serviceDate), Actor, Ct);

        Assert.Equal(serviceDate, result.OrderDate);
        Assert.Equal(isLate, result.IsLate);
        Assert.Equal(isLate, result.RequiresAdminReview);
        Assert.Equal(isLate ? OrderStatus.PendingAdminReview : OrderStatus.Submitted, result.Status);
    }

    [Fact]
    public async Task AdminNoOrderForFutureDate_DoesNotPreventCurrentDateOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var noOrder = await fixture.AdminService.MarkNoOrderAsync(fixture.Customer.Id,
            new AdminMarkNoOrderRequest(null, new DateOnly(2026, 10, 25)), Actor, Ct);
        var current = await fixture.AdminService.SubmitCustomerOrderAsync(fixture.Customer.Id,
            new AdminSubmitCustomerOrderRequest([new(fixture.KgProduct.Id, 5, null)], null, null, null, null, null), Actor, Ct);

        Assert.Equal(new DateOnly(2026, 10, 25), noOrder.OrderDate);
        Assert.Equal(new DateOnly(2026, 10, 24), current.OrderDate);
        Assert.Equal(1, current.SequenceNumber);
        Assert.False(current.RequiresAdminReview);
        Assert.Equal(2, await fixture.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task RepeatedBatch_UsesPersistedReceiptAcrossContexts_WithoutDuplicateOrdersOrAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,");
        var first = await fixture.Service.ApplyAsync(request, Actor, Ct);
        var audits = await fixture.Db.OrderAuditLogs.CountAsync();
        await using var secondDb = fixture.NewContext();
        var replay = await fixture.NewService(secondDb).ApplyAsync(request, Actor, Ct);

        Assert.True(replay.Applied);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Orders.Single().OrderId, replay.Orders.Single().OrderId);
        Assert.Equal(1, await secondDb.Orders.CountAsync());
        Assert.Equal(audits, await secondDb.OrderAuditLogs.CountAsync());
        Assert.Equal(1, await fixture.OperationCountAsync());
    }

    [Fact]
    public async Task ReusingBatchKeyWithChangedPayload_ReturnsConflictAndKeepsOriginal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ApplyAsync(Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,"), Actor, Ct);

        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.Service.ApplyAsync(
            Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,17,kg,"), Actor, Ct));

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(15, (await fixture.Db.OrderLines.SingleAsync()).Quantity);
        Assert.Equal(1, await fixture.Db.Orders.CountAsync());
        Assert.Equal(1, await fixture.OperationCountAsync());
    }

    [Fact]
    public async Task ReplayOfAppliedBatch_DoesNotDependOnCurrentCatalogAvailability()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,");
        var first = await fixture.Service.ApplyAsync(request, Actor, Ct);
        var product = await fixture.Db.Products.SingleAsync(x => x.Id == fixture.KgProduct.Id);
        product.Deactivate();
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var replay = await fixture.Service.ApplyAsync(request, Actor, Ct);

        Assert.True(replay.Replayed);
        Assert.True(replay.Applied);
        Assert.Equal(first.Orders.Single().OrderId, replay.Orders.Single().OrderId);
        Assert.Equal(1, await fixture.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task SameSourceOrderInDifferentBatch_IsExistingWithoutNewOrderOrAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,");
        var first = await fixture.Service.ApplyAsync(request, Actor, Ct);
        var auditCount = await fixture.Db.OrderAuditLogs.CountAsync();
        var second = await fixture.Service.ApplyAsync(request with { BatchKey = "BATCH-2" }, Actor, Ct);

        Assert.True(second.Applied);
        Assert.Equal("existing", Assert.Single(second.Orders).Disposition);
        Assert.Equal(first.Orders.Single().OrderId, second.Orders.Single().OrderId);
        Assert.Equal(1, await fixture.Db.Orders.CountAsync());
        Assert.Equal(auditCount, await fixture.Db.OrderAuditLogs.CountAsync());
    }

    [Fact]
    public async Task SourceAndBatchWithColons_RemainDistinctTuples()
    {
        await using var fixture = await Fixture.CreateAsync();
        var content = Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,");
        var first = await fixture.Service.ApplyAsync(content with { Source = "A:B", BatchKey = "C" }, Actor, Ct);
        var second = await fixture.Service.ApplyAsync(content with { Source = "A", BatchKey = "B:C" }, Actor, Ct);

        Assert.True(first.Applied);
        Assert.True(second.Applied);
        Assert.False(second.Replayed);
        Assert.NotEqual(first.Orders.Single().OrderId, second.Orders.Single().OrderId);
        Assert.Equal(2, await fixture.Db.Orders.CountAsync());
        Assert.Equal(2, await fixture.OperationCountAsync());
    }

    [Fact]
    public async Task ChangedSourceOrderInAnotherBatch_IsExplicitConflict_NotCorrection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ApplyAsync(Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,"), Actor, Ct);
        var changed = Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,17,kg,") with { BatchKey = "BATCH-2" };

        var preview = await fixture.Service.ValidateAsync(changed, Ct);
        Assert.False(preview.CanApply);
        Assert.NotEmpty(preview.Errors);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.Service.ApplyAsync(changed, Actor, Ct));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(15, (await fixture.Db.OrderLines.SingleAsync()).Quantity);
        Assert.Equal(1, await fixture.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task TwoOrdersSameCustomerAndServiceDate_KeepSecondPendingForReview()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ApplyAsync(Request(
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,",
            "ORDER-2,2026-10-25,C-TEST,order,P-PACK,2,package,"), Actor, Ct);

        Assert.True(result.Applied);
        var orders = await fixture.Db.Orders.OrderBy(x => x.SequenceNumber).ToArrayAsync();
        Assert.Equal(2, orders.Length);
        Assert.Equal(1, orders[0].SequenceNumber);
        Assert.Equal(2, orders[1].SequenceNumber);
        Assert.Equal(OrderStatus.PendingAdminReview, orders[1].Status);
        Assert.Equal(AdminReviewReason.AdditionalOrderSameDay, orders[1].AdminReviewReason);
    }

    [Fact]
    public async Task Correction_ReplacesCompleteLineSet_PreservesDateAndRecordsBeforeAfter()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.ApplyAsync(Request(
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,Original",
            "ORDER-1,2026-10-25,C-TEST,order,P-PACK,2,package,"), Actor, Ct);
        var id = created.Orders.Single().OrderId!.Value;
        var previous = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync();
        var originalStatus = previous.Status;
        var result = await fixture.Service.CorrectAsync(id, new CorrectOrderRequest("CORRECTION-1", previous.Version,
            "El cliente corrigió cantidades", [new(fixture.PackageProduct.Id, 4, "Confirmado")]), Actor, Ct);

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(id, persisted.Id);
        Assert.Equal(new DateOnly(2026, 10, 25), persisted.OrderDate);
        Assert.Equal(originalStatus, persisted.Status);
        Assert.Equal(2, result.Version);
        var line = Assert.Single(persisted.Lines);
        Assert.Equal(fixture.PackageProduct.Id, line.ProductId);
        Assert.Equal(4, line.Quantity);
        Assert.Equal("Confirmado", line.Notes);
        Assert.Equal(1, await fixture.Db.OrderLines.CountAsync());
        Assert.Equal(1, await fixture.Db.Orders.CountAsync());
        var corrections = (await fixture.Db.OrderAuditLogs.Where(x => x.OrderId == id).ToArrayAsync())
            .Where(x => x.MetadataJson?.Contains("El cliente corrigió cantidades", StringComparison.Ordinal) == true ||
                        x.MetadataJson?.Contains("El cliente corrigi", StringComparison.Ordinal) == true).ToArray();
        var audit = Assert.Single(corrections);
        using var json = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("El cliente corrigió cantidades", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("before").GetProperty("lines").GetArrayLength());
        Assert.Single(json.RootElement.GetProperty("after").GetProperty("lines").EnumerateArray());
        Assert.Equal(15, json.RootElement.GetProperty("before").GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("productId").GetGuid() == fixture.KgProduct.Id).GetProperty("quantity").GetDecimal());
        Assert.Equal("admin-test", audit.ActorId);
    }

    [Fact]
    public async Task CorrectionReplay_UsesOriginalReceiptDespiteExpectedVersionBecomingOld()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.CreateOrderAsync();
        var request = new CorrectOrderRequest("CORRECTION-1", 1, "Cantidad confirmada", [new(fixture.KgProduct.Id, 12, null)]);
        var first = await fixture.Service.CorrectAsync(id, request, Actor, Ct);
        var auditCount = await fixture.Db.OrderAuditLogs.CountAsync();
        await using var secondDb = fixture.NewContext();
        var replay = await fixture.NewService(secondDb).CorrectAsync(id, request, Actor, Ct);

        Assert.Equal(first.Version, replay.Version);
        Assert.Equal(first.Lines.Single().Quantity, replay.Lines.Single().Quantity);
        Assert.Equal(2, (await secondDb.Orders.SingleAsync()).Version);
        Assert.Equal(auditCount, await secondDb.OrderAuditLogs.CountAsync());
    }

    [Fact]
    public async Task QuantityCorrection_KeepsExistingProductAndUnitSnapshots()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.CreateOrderAsync();
        var product = await fixture.Db.Products.SingleAsync(x => x.Id == fixture.KgProduct.Id);
        product.Update("Nombre de catálogo posterior", null);
        product.SetUnitCode("package");
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var corrected = await fixture.Service.CorrectAsync(id, new CorrectOrderRequest("CORRECTION-1", 1,
            "Ajustar kilos del pedido original", [new(fixture.KgProduct.Id, 12, null)]), Actor, Ct);

        var line = Assert.Single(corrected.Lines);
        Assert.Equal("Variante Demo A", line.ProductName);
        Assert.Equal("kg", line.UnitCode);
        Assert.Equal(12, line.Quantity);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.OrderLines.SingleAsync();
        Assert.Equal("Variante Demo A", persisted.ProductNameSnapshot);
        Assert.Equal("kg", persisted.UnitCode);
    }

    [Fact]
    public async Task CorrectionStaleVersion_IsRejectedWithoutChangingLinesOrAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.CreateOrderAsync();
        await fixture.Service.CorrectAsync(id, new CorrectOrderRequest("CORRECTION-1", 1, "Primera corrección",
            [new(fixture.KgProduct.Id, 12, null)]), Actor, Ct);
        var auditCount = await fixture.Db.OrderAuditLogs.CountAsync();

        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.Service.CorrectAsync(id,
            new CorrectOrderRequest("CORRECTION-2", 1, "Versión antigua", [new(fixture.KgProduct.Id, 22, null)]), Actor, Ct));

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(12, (await fixture.Db.OrderLines.SingleAsync()).Quantity);
        Assert.Equal(auditCount, await fixture.Db.OrderAuditLogs.CountAsync());
        Assert.Equal(2, (await fixture.Db.Orders.SingleAsync()).Version);
    }

    [Fact]
    public async Task CorrectionKeyWithDifferentPayload_IsConflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.CreateOrderAsync();
        var request = new CorrectOrderRequest("CORRECTION-1", 1, "Primera corrección", [new(fixture.KgProduct.Id, 12, null)]);
        await fixture.Service.CorrectAsync(id, request, Actor, Ct);

        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.Service.CorrectAsync(id,
            request with { Lines = [new(fixture.KgProduct.Id, 99, null)] }, Actor, Ct));

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(12, (await fixture.Db.OrderLines.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task CorrectionCannotSilentlyCancelAnOrderWithEmptyReplacement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.CreateOrderAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CorrectAsync(id,
            new CorrectOrderRequest("CORRECTION-1", 1, "Sin renglones", []), Actor, Ct));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(15, (await fixture.Db.OrderLines.SingleAsync()).Quantity);
        Assert.Equal(1, (await fixture.Db.Orders.SingleAsync()).Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CorrectionRequiresReason(string reason)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.CreateOrderAsync();
        var auditCount = await fixture.Db.OrderAuditLogs.CountAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CorrectAsync(id,
            new CorrectOrderRequest("CORRECTION-1", 1, reason, [new(fixture.KgProduct.Id, 12, null)]), Actor, Ct));
        Assert.Equal(auditCount, await fixture.Db.OrderAuditLogs.CountAsync());
        Assert.Equal(1, (await fixture.Db.Orders.SingleAsync()).Version);
    }

    [Fact]
    public async Task FailureSavingOperationReceipt_RollsBackWholeImport()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyAsync(Request(
            "ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,",
            "ORDER-2,2026-10-25,C-OTHER,order,P-PACK,2,package,"), Actor, Ct));

        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Orders.ToArrayAsync());
        Assert.Empty(await fixture.Db.OrderLines.ToArrayAsync());
        Assert.Empty(await fixture.Db.OrderAuditLogs.ToArrayAsync());
        Assert.Equal(0, await fixture.OperationCountAsync());
        fixture.Failure.Enabled = false;
        Assert.True((await fixture.Service.ApplyAsync(Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,"), Actor, Ct)).Applied);
    }

    [Fact]
    public async Task FailureSavingCorrectionReceipt_RollsBackLinesVersionAndAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.CreateOrderAsync();
        var auditCount = await fixture.Db.OrderAuditLogs.CountAsync();
        fixture.Failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CorrectAsync(id,
            new CorrectOrderRequest("CORRECTION-1", 1, "Cambio que fallará", [new(fixture.PackageProduct.Id, 9, null)]), Actor, Ct));

        fixture.Db.ChangeTracker.Clear();
        var order = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(1, order.Version);
        var line = Assert.Single(order.Lines);
        Assert.Equal(fixture.KgProduct.Id, line.ProductId);
        Assert.Equal(15, line.Quantity);
        Assert.Equal(auditCount, await fixture.Db.OrderAuditLogs.CountAsync());
        Assert.Equal(1, await fixture.OperationCountAsync());
    }

    private static DailyOrderImportRequest Request(params string[] rows) =>
        new(Header + "\n" + string.Join("\n", rows), "test_csv", "BATCH-1", "pedidos-demo.csv");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<PedidosDbContext> options;
        private readonly Clock clock = new();
        private Fixture(SqliteConnection connection, DbContextOptions<PedidosDbContext> options, PedidosDbContext db,
            Customer customer, Product kg, Product package, Machine kgMachine, Machine packageMachine, ReceiptFailure failure)
        {
            this.connection = connection;
            this.options = options;
            Db = db; Customer = customer; KgProduct = kg; PackageProduct = package;
            KgMachine = kgMachine; PackageMachine = packageMachine; Failure = failure;
            Service = NewService(db);
        }
        public PedidosDbContext Db { get; }
        public Customer Customer { get; }
        public Product KgProduct { get; }
        public Product PackageProduct { get; }
        public Machine KgMachine { get; }
        public Machine PackageMachine { get; }
        public ReceiptFailure Failure { get; }
        public AdminDailyOrderService Service { get; }
        public AdminOrderService AdminService => new(new EfOrderRepository(Db), new EfOrderAuditLogRepository(Db),
            new EfCustomerRepository(Db), new EfProductRepository(Db), new EfMachineRepository(Db), new EfSalesChannelRepository(Db), clock);
        public void SetTime(TimeOnly value) => clock.Time = value;
        public PedidosDbContext NewContext() => new(options);
        public AdminDailyOrderService NewService(PedidosDbContext db) => new(new EfOrderRepository(db),
            new EfOrderAuditLogRepository(db), new EfCustomerRepository(db), new EfProductRepository(db),
            new EfMachineRepository(db), new EfSalesChannelRepository(db), clock, new EfOrderOperationStore(db));
        public Task<int> OperationCountAsync() => Db.OrderOperations.CountAsync();
        public async Task<Guid> CreateOrderAsync() => (await Service.ApplyAsync(
            Request("ORDER-1,2026-10-25,C-TEST,order,P-KG,15,kg,"), Actor, Ct)).Orders.Single().OrderId!.Value;
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var failure = new ReceiptFailure();
            var options = new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).AddInterceptors(failure).Options;
            var db = new PedidosDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var now = new Clock().Now;
            var customer = Customer.Create("Cliente Demo", null, null, null, null, null, now);
            customer.SetExternalCode("C-TEST", now);
            customer.SetPilotDelivery("R-DEMO", 1, null, null, null, now);
            var other = Customer.Create("Otro Cliente Demo", null, null, null, null, null, now);
            other.SetExternalCode("C-OTHER", now);
            var kg = Product.Create("Variante Demo A", null); kg.SetUnitCode("kg"); kg.SetExternalCode("P-KG");
            var package = Product.Create("Variante Demo B", null); package.SetUnitCode("package"); package.SetExternalCode("P-PACK");
            var kgMachine = new Machine { Id = Guid.NewGuid(), Number = 1, ExternalCode = "M-1", Name = "Máquina Demo A" };
            var packageMachine = new Machine { Id = Guid.NewGuid(), Number = 2, ExternalCode = "M-2", Name = "Máquina Demo B" };
            db.AddRange(customer, other, kg, package, kgMachine, packageMachine,
                new SalesChannel { Id = Guid.NewGuid(), Name = "Captura administrativa", Type = SalesChannelType.AdminManualCapture, IsInternal = true },
                new CustomerFrequentProduct { CustomerId = customer.Id, ProductId = kg.Id, PreferredMachineId = kgMachine.Id, SortOrder = 1 },
                new CustomerFrequentProduct { CustomerId = customer.Id, ProductId = package.Id, PreferredMachineId = packageMachine.Id, SortOrder = 2 });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(connection, options, db, customer, kg, package, kgMachine, packageMachine, failure);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class ReceiptFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        private bool failAfterWrite;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            failAfterWrite = Enabled && eventData.Context!.ChangeTracker.Entries()
                .Any(x => x.State == EntityState.Added && x.Entity is OrderOperation);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (failAfterWrite)
            {
                failAfterWrite = false;
                throw new InvalidOperationException("Fallo simulado después de escribir el recibo y antes de confirmar la transacción.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Clock : IDateTimeProvider
    {
        public TimeOnly Time { get; set; } = new(9, 0);
        public DateTimeOffset Now => new(2026, 10, 24, Time.Hour, Time.Minute, Time.Second, TimeSpan.FromHours(-6));
        public DateOnly Today => new(2026, 10, 24);
        public TimeOnly LocalTimeOfDay => Time;
    }
}
