using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CustomerOrderPersistenceTests
{
    [Fact]
    public async Task GetTodayAsync_IncludesCurrentOrderWhenCustomerAlreadyResponded()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var today = await fixture.CustomerOrders.GetTodayAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);

        Assert.NotNull(today.CurrentOrder);
        Assert.Equal(response.OrderId, today.CurrentOrder.OrderId);
        Assert.Equal(OrderStatus.Submitted, today.CurrentOrder.Status);
        Assert.Equal(1, today.CurrentOrder.SequenceNumber);
        Assert.False(today.CurrentOrder.RequiresAdminReview);
    }

    [Fact]
    public async Task SubmitAsync_PersistsNormalOrder()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.Submitted, response.Status);
        Assert.False(response.IsLate);
        Assert.False(response.RequiresAdminReview);
        Assert.Null(response.AdminReviewReason);
        Assert.Single(persisted.Lines);
        Assert.Equal(20, persisted.Lines.Single().Quantity);

        var auditLog = await fixture.DbContext.OrderAuditLogs.SingleAsync(x => x.OrderId == response.OrderId);
        Assert.Equal(OrderAuditEventType.OrderSubmitted, auditLog.EventType);
        Assert.Equal(AuditActorType.Customer, auditLog.ActorType);
        Assert.Equal(OrderStatus.Submitted, auditLog.OrderStatus);
    }

    [Fact]
    public async Task SubmitAsync_PersistsLateOrderAsPendingReviewAndAudit()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 10, 1, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders.SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.PendingAdminReview, persisted.Status);
        Assert.True(persisted.IsLate);
        Assert.True(persisted.RequiresAdminReview);
        Assert.Equal(AdminReviewReason.LateSubmission, persisted.AdminReviewReason);

        var auditEventTypes = await fixture.DbContext.OrderAuditLogs
            .Where(x => x.OrderId == response.OrderId)
            .Select(x => x.EventType)
            .ToArrayAsync();

        Assert.Contains(OrderAuditEventType.OrderSubmitted, auditEventTypes);
        Assert.Contains(OrderAuditEventType.OrderMarkedLate, auditEventTypes);
        Assert.Contains(OrderAuditEventType.OrderRequiresAdminReview, auditEventTypes);
    }

    [Fact]
    public async Task MarkNoOrderAsync_PersistsNoOrderResponseAndAudit()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.NoOrder, persisted.Status);
        Assert.Equal(DevelopmentSeedIds.GranTakitoCustomerId, persisted.CustomerId);
        Assert.Empty(persisted.Lines);
        Assert.False(persisted.RequiresAdminReview);

        var auditLog = await fixture.DbContext.OrderAuditLogs.SingleAsync(x => x.OrderId == response.OrderId);
        Assert.Equal(OrderAuditEventType.NoOrderMarked, auditLog.EventType);
        Assert.Equal(AuditActorType.Customer, auditLog.ActorType);
        Assert.Equal(OrderStatus.NoOrder, auditLog.OrderStatus);
    }

    [Fact]
    public async Task SubmitAsync_SecondOrderSameDayRequiresAdminReviewAndAudit()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        fixture.DateTimeProvider.Now = new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero);

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductTenAndHalfId, 10),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders.SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.PendingAdminReview, persisted.Status);
        Assert.False(persisted.IsLate);
        Assert.True(persisted.RequiresAdminReview);
        Assert.Equal(AdminReviewReason.AdditionalOrderSameDay, persisted.AdminReviewReason);
        Assert.Equal(2, persisted.SequenceNumber);

        var auditLogs = await fixture.DbContext.OrderAuditLogs
            .Where(x => x.OrderId == response.OrderId)
            .ToArrayAsync();

        Assert.Contains(auditLogs, x => x.EventType == OrderAuditEventType.OrderSubmitted);
        Assert.Contains(auditLogs, x => x.EventType == OrderAuditEventType.AdditionalOrderDetected);
        Assert.Contains(auditLogs, x =>
            x.EventType == OrderAuditEventType.OrderRequiresAdminReview &&
            x.AdminReviewReason == AdminReviewReason.AdditionalOrderSameDay);
    }

    [Fact]
    public async Task SubmitAsync_RejectsOrderWithoutPositiveQuantities()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 0),
            CancellationToken.None));

        Assert.Contains("Captura al menos una cantidad", exception.Message);
        Assert.False(await fixture.DbContext.Orders.AnyAsync());
    }

    [Fact]
    public async Task SubmitAsync_RejectsNegativeQuantities()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, -1),
            CancellationToken.None));

        Assert.Contains("cantidades no pueden ser negativas", exception.Message);
        Assert.False(await fixture.DbContext.Orders.AnyAsync());
    }

    [Fact]
    public async Task SubmitAsync_IgnoresZeroQuantityLines()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new SubmitCustomerOrderRequest(
            [
                new SubmitCustomerOrderLineRequest(DevelopmentSeedIds.ProductNineAndHalfId, 0, Notes: null),
                new SubmitCustomerOrderLineRequest(DevelopmentSeedIds.ProductTenAndHalfId, 10, Notes: null)
            ]),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == response.OrderId);

        var line = Assert.Single(persisted.Lines);
        Assert.Equal(DevelopmentSeedIds.ProductTenAndHalfId, line.ProductId);
        Assert.Equal(10, line.Quantity);
    }

    [Fact]
    public async Task MarkNoOrderAsync_ReturnsExistingNoOrderWithoutDuplicate()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var first = await fixture.CustomerOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);
        var second = await fixture.CustomerOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);

        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(1, await fixture.DbContext.Orders.CountAsync(x => x.CustomerId == DevelopmentSeedIds.GranTakitoCustomerId));
        Assert.Equal(1, await fixture.DbContext.OrderAuditLogs.CountAsync(x => x.OrderId == first.OrderId));
    }

    [Fact]
    public async Task AdminSummary_IncludesCustomerNameAndAdminDecision()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 10, 1, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var summaries = await fixture.AdminOrders.GetPendingReviewAsync(CancellationToken.None);
        var summary = Assert.Single(summaries);

        Assert.Equal(response.OrderId, summary.OrderId);
        Assert.Equal("Gran Takito", summary.CustomerName);
        Assert.Equal(AdminDecision.Pending, summary.AdminDecision);
    }

    [Fact]
    public async Task AdminDetail_ReturnsLinesWithInternalMachine()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var detail = await fixture.AdminOrders.GetDetailAsync(response.OrderId, CancellationToken.None);
        var line = Assert.Single(detail.Lines);

        Assert.Equal(response.OrderId, detail.OrderId);
        Assert.Equal("Gran Takito", detail.CustomerName);
        Assert.Equal(DevelopmentSeedIds.ProductNineAndHalfId, line.ProductId);
        Assert.Equal("#9 1/2", line.ProductName);
        Assert.Equal(20, line.Quantity);
        Assert.Equal(DevelopmentSeedIds.MachineOneId, line.AssignedMachineId);
        Assert.Equal("Maquina 1", line.AssignedMachineName);
        Assert.Equal(1, line.AssignedMachineNumber);
    }

    [Fact]
    public async Task PendingCustomers_ExcludesCustomersWithOrderOrNoOrder()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero));

        await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);
        await fixture.AdminOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.DemoCustomer2Id,
            new AdminMarkNoOrderRequest("Confirmado por telefono."),
            new AdminActorContext("admin-1", "Admin Test"),
            CancellationToken.None);

        var pendingCustomers = await fixture.AdminOrders.GetPendingCustomersAsync(
            new DateOnly(2026, 5, 27),
            CancellationToken.None);

        Assert.DoesNotContain(pendingCustomers, x => x.CustomerId == DevelopmentSeedIds.GranTakitoCustomerId);
        Assert.DoesNotContain(pendingCustomers, x => x.CustomerId == DevelopmentSeedIds.DemoCustomer2Id);

        var pending = Assert.Single(pendingCustomers);
        Assert.Equal(DevelopmentSeedIds.DemoCustomer3Id, pending.CustomerId);
        Assert.Equal(1, pending.FrequentProductsCount);
    }

    [Fact]
    public async Task AdminSubmitCustomerOrder_PersistsAdminCaptureAndAudit()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero));

        var response = await fixture.AdminOrders.SubmitCustomerOrderAsync(
            DevelopmentSeedIds.DemoCustomer2Id,
            new AdminSubmitCustomerOrderRequest(
            [
                new AdminSubmitCustomerOrderLineRequest(DevelopmentSeedIds.ProductElevenId, 8, "Por llamada")
            ],
            RequestedDeliveryTime: new TimeOnly(12, 0),
            RequestedDeliveryWindowStart: null,
            RequestedDeliveryWindowEnd: null,
            DeliveryNotes: "Entregar despues de las 12",
            InternalNotes: "Capturado por telefono."),
            new AdminActorContext("admin-1", "Admin Test"),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(OrderStatus.Submitted, persisted.Status);
        Assert.Equal(DevelopmentSeedIds.AdminManualChannelId, persisted.SalesChannelId);
        Assert.Equal(new TimeOnly(12, 0), persisted.RequestedDeliveryTime);
        Assert.Equal("Entregar despues de las 12", persisted.DeliveryNotes);
        Assert.Equal("Capturado por telefono.", persisted.InternalNotes);
        Assert.Equal(DevelopmentSeedIds.MachineTwoId, persisted.Lines.Single().AssignedMachineId);

        var auditLog = await fixture.DbContext.OrderAuditLogs
            .SingleAsync(x => x.OrderId == response.OrderId && x.EventType == OrderAuditEventType.AdminManualOrderCaptured);

        Assert.Equal(AuditActorType.Admin, auditLog.ActorType);
        Assert.Equal("admin-1", auditLog.ActorId);
        Assert.Equal("Admin Test", auditLog.ActorDisplayName);
    }

    [Fact]
    public async Task AdminMarkNoOrder_IsIdempotentAndAudits()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 30, 0, TimeSpan.Zero));

        var first = await fixture.AdminOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.DemoCustomer2Id,
            new AdminMarkNoOrderRequest("No pedira hoy."),
            new AdminActorContext("admin-1", "Admin Test"),
            CancellationToken.None);
        var second = await fixture.AdminOrders.MarkNoOrderAsync(
            DevelopmentSeedIds.DemoCustomer2Id,
            new AdminMarkNoOrderRequest("Reintento."),
            new AdminActorContext("admin-1", "Admin Test"),
            CancellationToken.None);

        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(1, await fixture.DbContext.Orders.CountAsync(x => x.CustomerId == DevelopmentSeedIds.DemoCustomer2Id));

        var auditLog = await fixture.DbContext.OrderAuditLogs
            .SingleAsync(x => x.OrderId == first.OrderId && x.EventType == OrderAuditEventType.AdminNoOrderMarked);

        Assert.Equal(AuditActorType.Admin, auditLog.ActorType);
        Assert.Equal(OrderStatus.NoOrder, auditLog.OrderStatus);
    }

    [Theory]
    [InlineData(AdminDecision.Accepted, OrderStatus.Accepted)]
    [InlineData(AdminDecision.Rejected, OrderStatus.Rejected)]
    [InlineData(AdminDecision.AcceptedWithChanges, OrderStatus.Accepted)]
    public async Task ReviewAsync_PersistsAdminDecision(
        AdminDecision decision,
        OrderStatus expectedStatus)
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 10, 1, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var reviewed = await fixture.AdminOrders.ReviewAsync(
            response.OrderId,
            new ReviewOrderRequest(decision, InternalNotes: "Decision revisada por admin."),
            CancellationToken.None);

        var persisted = await fixture.DbContext.Orders.SingleAsync(x => x.Id == response.OrderId);

        Assert.Equal(decision, reviewed.AdminDecision);
        Assert.Equal(decision, persisted.AdminDecision);
        Assert.Equal(expectedStatus, persisted.Status);
        Assert.False(persisted.RequiresAdminReview);
        Assert.Equal("Decision revisada por admin.", persisted.InternalNotes);

        var auditLog = await fixture.DbContext.OrderAuditLogs
            .SingleAsync(x => x.OrderId == response.OrderId && x.EventType == OrderAuditEventType.AdminDecisionRecorded);

        Assert.Equal(AuditActorType.Admin, auditLog.ActorType);
        Assert.Equal(decision, auditLog.AdminDecision);
        Assert.Equal(expectedStatus, auditLog.OrderStatus);
    }

    [Fact]
    public async Task ReviewAsync_AcceptedWithChanges_ModifiesDeliveryLineAndAuditsChanges()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 10, 1, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);
        var lineId = await fixture.DbContext.OrderLines
            .Where(x => x.OrderId == response.OrderId)
            .Select(x => x.Id)
            .SingleAsync();

        var reviewed = await fixture.AdminOrders.ReviewAsync(
            response.OrderId,
            new ReviewOrderRequest(
                AdminDecision.AcceptedWithChanges,
                InternalNotes: "Se ajusta entrega y cantidad.",
                RequestedDeliveryTime: new TimeOnly(13, 0),
                DeliveryNotes: "Entregar despues de las 13.",
                LineAdjustments:
                [
                    new ReviewOrderLineAdjustmentRequest(lineId, 25, "Cantidad ajustada.")
                ]),
            CancellationToken.None,
            new AdminActorContext("admin-1", "Admin Test"));

        var persisted = await fixture.DbContext.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == response.OrderId);
        var line = persisted.Lines.Single();

        Assert.Equal(OrderStatus.Accepted, persisted.Status);
        Assert.Equal(AdminDecision.AcceptedWithChanges, reviewed.AdminDecision);
        Assert.Equal(new TimeOnly(13, 0), persisted.RequestedDeliveryTime);
        Assert.Equal("Entregar despues de las 13.", persisted.DeliveryNotes);
        Assert.Equal(25, line.Quantity);
        Assert.Equal("Cantidad ajustada.", line.Notes);

        var auditLogs = await fixture.DbContext.OrderAuditLogs
            .Where(x => x.OrderId == response.OrderId)
            .ToArrayAsync();

        Assert.Contains(auditLogs, x =>
            x.EventType == OrderAuditEventType.AdminDecisionRecorded &&
            x.AdminDecision == AdminDecision.AcceptedWithChanges &&
            x.MetadataJson != null &&
            x.MetadataJson.Contains("requestedDeliveryTime", StringComparison.Ordinal));
        Assert.Contains(auditLogs, x =>
            x.EventType == OrderAuditEventType.AdminOrderChanged &&
            x.MetadataJson != null &&
            x.MetadataJson.Contains("quantity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAuditAsync_ReturnsEventsOrderedByOccurredAt()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero));

        var response = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20),
            CancellationToken.None);

        var order = await fixture.DbContext.Orders.SingleAsync(x => x.Id == response.OrderId);

        fixture.DbContext.OrderAuditLogs.Add(OrderAuditLog.Create(
            order,
            OrderAuditEventType.OrderRequiresAdminReview,
            response.SubmittedAt.AddMinutes(10),
            AuditActorType.System,
            "Evento posterior de prueba."));
        fixture.DbContext.OrderAuditLogs.Add(OrderAuditLog.Create(
            order,
            OrderAuditEventType.OrderMarkedLate,
            response.SubmittedAt.AddMinutes(-10),
            AuditActorType.System,
            "Evento anterior de prueba."));
        await fixture.DbContext.SaveChangesAsync();

        var auditLogs = await fixture.AdminOrders.GetAuditAsync(response.OrderId, CancellationToken.None);
        var occurredAtValues = auditLogs.Select(x => x.OccurredAt).ToArray();

        Assert.Equal(occurredAtValues.OrderBy(x => x).ToArray(), occurredAtValues);
    }

    private static SubmitCustomerOrderRequest CreateSubmitRequest(Guid productId, decimal quantity)
    {
        return new SubmitCustomerOrderRequest(
        [
            new SubmitCustomerOrderLineRequest(productId, quantity, Notes: null)
        ]);
    }


    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("unknown-unit")]
    public async Task SubmitAsync_RejectsUnusableProductBeforeSaving(string mode)
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        var product = await fixture.DbContext.Products.SingleAsync(x => x.Id == DevelopmentSeedIds.ProductNineAndHalfId);
        var productId = product.Id;
        if (mode == "missing") productId = Guid.NewGuid();
        if (mode == "inactive") product.IsActive = false;
        if (mode == "unknown-unit") product.UnitCode = null;
        await fixture.DbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId, CreateSubmitRequest(productId, 2), CancellationToken.None));
        Assert.Empty(await fixture.DbContext.Orders.ToArrayAsync());
        Assert.Empty(await fixture.DbContext.OrderAuditLogs.ToArrayAsync());
    }

    [Fact]
    public async Task SubmitAsync_SnapshotsUnitProductDeliveryAndPrefersProductMachine()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        var customer = await fixture.DbContext.Customers.SingleAsync(x => x.Id == DevelopmentSeedIds.GranTakitoCustomerId);
        customer.RouteCode = "PILOT-DEMO"; customer.RouteStopOrder = 2; customer.HasStoreKey = true;
        customer.PaymentTermCode = "cash_on_delivery"; customer.InternalDeliveryInstructions = "Mesa de demostracion";
        customer.OrderingProfileVersion = 3;
        var machine = new Machine { Id = Guid.NewGuid(), Number = 99, Name = "Centro demo", IsActive = true };
        fixture.DbContext.Machines.Add(machine);
        var frequent = await fixture.DbContext.CustomerFrequentProducts.SingleAsync(x => x.CustomerId == customer.Id && x.ProductId == DevelopmentSeedIds.ProductNineAndHalfId);
        frequent.PreferredMachineId = machine.Id;
        var product = await fixture.DbContext.Products.SingleAsync(x => x.Id == frequent.ProductId);
        var originalName = product.Name;
        product.UnitCode = "dozen";
        await fixture.DbContext.SaveChangesAsync();
        var response = await fixture.CustomerOrders.SubmitAsync(customer.Id, CreateSubmitRequest(product.Id, 2), CancellationToken.None);
        product.Name = "Nombre posterior"; product.UnitCode = "kg";
        customer.Name = "Nombre cliente posterior"; customer.RouteCode = "OTRA"; customer.InternalDeliveryInstructions = "Otra instruccion";
        await fixture.DbContext.SaveChangesAsync();
        var detail = await fixture.AdminOrders.GetDetailAsync(response.OrderId, CancellationToken.None);
        Assert.Equal(originalName, detail.Lines.Single().ProductName);
        Assert.Equal("dozen", detail.Lines.Single().UnitCode);
        Assert.Equal(machine.Id, detail.Lines.Single().AssignedMachineId);
        var plan = await fixture.Operations.GetPlanAsync(new DateOnly(2026, 10, 24), "PILOT-DEMO", CancellationToken.None);
        var order = Assert.Single(plan.Orders);
        Assert.True(order.HasStoreKey); Assert.Equal("Mesa de demostracion", order.InternalDeliveryInstructions);
        Assert.Equal(3, order.OrderingProfileVersion); Assert.Equal("cash_on_delivery", order.PaymentTermCode);
        Assert.Equal("dozen", Assert.Single(plan.MachineDemand).UnitCode);
        Assert.DoesNotContain("posterior", order.CustomerName);
    }

    [Fact]
    public async Task SubmitAsync_RejectsInactivePreferredMachine()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        var frequent = await fixture.DbContext.CustomerFrequentProducts.FirstAsync(x => x.CustomerId == DevelopmentSeedIds.GranTakitoCustomerId);
        var machine = await fixture.DbContext.Machines.FirstAsync();
        machine.IsActive = false; frequent.PreferredMachineId = machine.Id;
        await fixture.DbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId, CreateSubmitRequest(frequent.ProductId, 2), CancellationToken.None));
        Assert.Empty(await fixture.DbContext.Orders.ToArrayAsync());
    }

    [Fact]
    public async Task OperationsPlan_ExcludesPendingRejectedNoOrderAndKeepsUnitsSeparate()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        var first = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20), CancellationToken.None);
        var pending = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 50), CancellationToken.None);
        var product = await fixture.DbContext.Products.SingleAsync(x => x.Id == DevelopmentSeedIds.ProductNineAndHalfId);
        product.UnitCode = "package"; await fixture.DbContext.SaveChangesAsync();
        var accepted = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(product.Id, 3), CancellationToken.None);
        await fixture.AdminOrders.ReviewAsync(accepted.OrderId, new ReviewOrderRequest(AdminDecision.Accepted, null), CancellationToken.None);
        var rejected = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(product.Id, 100), CancellationToken.None);
        await fixture.AdminOrders.ReviewAsync(rejected.OrderId, new ReviewOrderRequest(AdminDecision.Rejected, null), CancellationToken.None);
        await fixture.CustomerOrders.MarkNoOrderAsync(DevelopmentSeedIds.DemoCustomer2Id, CancellationToken.None);
        var plan = await fixture.Operations.GetPlanAsync(null, null, CancellationToken.None);
        Assert.Equal("Por producir", plan.DemandLabel); Assert.Equal("Por preparar", plan.PreparationLabel);
        Assert.Equal(2, plan.Orders.Count); Assert.Equal(pending.OrderId, Assert.Single(plan.PendingReview).OrderId);
        Assert.Equal(20, plan.MachineDemand.Single(x => x.UnitCode == "kg").Quantity);
        Assert.Equal(3, plan.MachineDemand.Single(x => x.UnitCode == "package").Quantity);
        Assert.DoesNotContain(plan.Orders, x => x.OrderId == rejected.OrderId);
    }

    [Fact]
    public async Task OperationsPlan_ExcludesUnknownHistoricalUnitFromTotals()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        var response = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20), CancellationToken.None);
        var line = await fixture.DbContext.OrderLines.SingleAsync(); line.UnitCode = null;
        await fixture.DbContext.SaveChangesAsync();
        var plan = await fixture.Operations.GetPlanAsync(null, null, CancellationToken.None);
        Assert.Single(plan.Orders); Assert.Empty(plan.MachineDemand);
        Assert.Contains(plan.Warnings, x => x.Code == "UNIT_UNKNOWN" && x.OrderId == response.OrderId);
    }

    [Fact]
    public async Task ReviewAsync_RejectsStaleOrAlreadyReviewedCommand()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 11, 0, 0, TimeSpan.Zero));
        var response = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20), CancellationToken.None);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.AdminOrders.ReviewAsync(response.OrderId,
            new ReviewOrderRequest(AdminDecision.Accepted, null, ExpectedVersion: 9), CancellationToken.None));
        await fixture.AdminOrders.ReviewAsync(response.OrderId, new ReviewOrderRequest(AdminDecision.Accepted, null, ExpectedVersion: 1), CancellationToken.None);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.AdminOrders.ReviewAsync(response.OrderId,
            new ReviewOrderRequest(AdminDecision.Rejected, null, ExpectedVersion: 1), CancellationToken.None));
        Assert.Equal(2, (await fixture.AdminOrders.GetDetailAsync(response.OrderId, CancellationToken.None)).Version);
    }

    [Fact]
    public async Task ReviewAsync_InvalidAdjustmentDoesNotPartiallyMutateDelivery()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 11, 0, 0, TimeSpan.Zero));
        var response = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20), CancellationToken.None);
        var before = await fixture.AdminOrders.GetDetailAsync(response.OrderId, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.AdminOrders.ReviewAsync(response.OrderId,
            new ReviewOrderRequest(AdminDecision.AcceptedWithChanges, null, DeliveryNotes: "No debe guardarse",
                LineAdjustments: [new(Guid.NewGuid(), 5, null)]), CancellationToken.None));
        var after = await fixture.AdminOrders.GetDetailAsync(response.OrderId, CancellationToken.None);
        Assert.Equal(before.DeliveryNotes, after.DeliveryNotes); Assert.Equal(before.Status, after.Status);
    }

    [Fact]
    public async Task MarkNoOrderAsync_RejectsWhenAnEarlierOrderRemainsActive()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20), CancellationToken.None);
        var additional = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 5), CancellationToken.None);
        await fixture.AdminOrders.ReviewAsync(additional.OrderId, new ReviewOrderRequest(AdminDecision.Rejected, null), CancellationToken.None);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.CustomerOrders.MarkNoOrderAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None));
    }


    [Fact]
    public async Task ReviewAsync_ConcurrentDatabaseChangeReturnsConflict()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 11, 0, 0, TimeSpan.Zero));
        var response = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20), CancellationToken.None);
        await using var concurrentContext = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>()
            .UseSqlite(fixture.DbContext.Database.GetDbConnection()).Options);
        var staleOrder = await concurrentContext.Orders.SingleAsync(x => x.Id == response.OrderId);
        await fixture.AdminOrders.ReviewAsync(response.OrderId, new ReviewOrderRequest(AdminDecision.Accepted, null), CancellationToken.None);
        staleOrder.ApplyAdminDecision(AdminDecision.Rejected);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => new EfOrderRepository(concurrentContext).SaveChangesAsync(CancellationToken.None));
        Assert.Equal(OrderStatus.Accepted, (await fixture.AdminOrders.GetDetailAsync(response.OrderId, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Orders_DatabaseRejectsDuplicateCustomerDateSequence()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(DevelopmentSeedIds.ProductNineAndHalfId, 20), CancellationToken.None);
        var existing = await fixture.DbContext.Orders.SingleAsync();
        var duplicate = Order.CreateNoOrder(existing.CustomerId!.Value, existing.SalesChannelId, existing.OrderDate,
            existing.SubmittedAt, existing.SequenceNumber);
        fixture.DbContext.Orders.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.DbContext.SaveChangesAsync());
    }


    [Fact]
    public async Task OperationsPlan_KeepsDifferentProductNameSnapshotsSeparate()
    {
        await using var fixture = await SqliteFixture.CreateAsync(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
        var product = await fixture.DbContext.Products.SingleAsync(x => x.Id == DevelopmentSeedIds.ProductNineAndHalfId);
        var originalName = product.Name;
        await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(product.Id, 20), CancellationToken.None);
        product.Name = "Variante renombrada"; await fixture.DbContext.SaveChangesAsync();
        var additional = await fixture.CustomerOrders.SubmitAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            CreateSubmitRequest(product.Id, 5), CancellationToken.None);
        await fixture.AdminOrders.ReviewAsync(additional.OrderId, new ReviewOrderRequest(AdminDecision.Accepted, null), CancellationToken.None);
        var plan = await fixture.Operations.GetPlanAsync(null, null, CancellationToken.None);
        Assert.Equal(2, plan.MachineDemand.Count);
        Assert.Equal(20, plan.MachineDemand.Single(x => x.ProductName == originalName).Quantity);
        Assert.Equal(5, plan.MachineDemand.Single(x => x.ProductName == "Variante renombrada").Quantity);
    }

    private sealed class MutableDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SqliteFixture(SqliteConnection connection, PedidosDbContext dbContext, MutableDateTimeProvider dateTimeProvider)
        {
            _connection = connection;
            DbContext = dbContext;
            DateTimeProvider = dateTimeProvider;
            var customerRepository = new EfCustomerRepository(dbContext);
            var orderRepository = new EfOrderRepository(dbContext);
            var auditLogRepository = new EfOrderAuditLogRepository(dbContext);

            CustomerOrders = new CustomerOrderService(
                customerRepository,
                new EfProductRepository(dbContext),
                new EfSalesChannelRepository(dbContext),
                orderRepository,
                auditLogRepository,
                dateTimeProvider,
                new EfMachineRepository(dbContext));
            AdminOrders = new AdminOrderService(
                orderRepository,
                auditLogRepository,
                customerRepository,
                new EfProductRepository(dbContext),
                new EfMachineRepository(dbContext),
                new EfSalesChannelRepository(dbContext),
                dateTimeProvider);
        }

        public PedidosDbContext DbContext { get; }

        public MutableDateTimeProvider DateTimeProvider { get; }

        public CustomerOrderService CustomerOrders { get; }

        public AdminOrderService AdminOrders { get; }

        public AdminOperationsService Operations => new(new EfOrderRepository(DbContext), new EfCustomerRepository(DbContext), new EfMachineRepository(DbContext), DateTimeProvider);

        public static async Task<SqliteFixture> CreateAsync(DateTimeOffset now)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<PedidosDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new PedidosDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            await PedidosDevelopmentSeeder.SeedAsync(dbContext);

            return new SqliteFixture(connection, dbContext, new MutableDateTimeProvider(now));
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
