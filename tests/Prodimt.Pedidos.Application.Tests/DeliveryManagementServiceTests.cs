using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Delivery;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class DeliveryManagementServiceTests
{
    private static readonly CancellationToken Ct = default;

    [Fact]
    public async Task ReadyOrderPreparesOneFirstTripAndExposesUnreservedStopsOnlyToItsDriver()
    {
        await using var f = await Fixture.CreateAsync(createTrip: false);
        var route = await f.Db.DeliveryRoutes.SingleAsync(Ct);
        route.IsActive = false;
        await f.Db.SaveChangesAsync(Ct);
        await f.Service.PrepareForOrderAsync(f.Order.Id, Ct);
        Assert.Empty(await f.Db.DeliveryTrips.ToArrayAsync(Ct));

        route = await f.Db.DeliveryRoutes.SingleAsync(Ct);
        route.IsActive = true;
        await f.Db.SaveChangesAsync(Ct);
        var before = await f.Service.DashboardAsync(f.Date, null, f.DriverActor, false, Ct);
        Assert.True(before.CanPrepare);
        Assert.Empty(before.Trips);
        Assert.Empty(await f.Db.DeliveryTrips.ToArrayAsync(Ct));

        await f.Service.PrepareForOrderAsync(f.Order.Id, Ct);
        await using var retryDb = f.NewContext();
        await f.NewService(retryDb).PrepareForOrderAsync(f.Order.Id, Ct);

        var prepared = await f.Service.DashboardAsync(f.Date, null, f.DriverActor, false, Ct);
        Assert.False(prepared.CanPrepare);
        var trip = Assert.Single(prepared.Trips);
        Assert.Equal(1, trip.TripNumber);
        Assert.Equal(f.Driver.Id, trip.DriverUserId);
        Assert.Equal(f.Order.Id, Assert.Single(trip.PlannedOrders!).OrderId);
        Assert.Equal(2, Assert.Single(trip.PlannedOrders!).Lines.Count);
        Assert.Empty(trip.Assignments);
        Assert.Empty(await f.Db.DeliveryAssignments.ToArrayAsync(Ct));
        Assert.Empty((await f.Service.DashboardAsync(f.Date, null, f.OtherActor, false, Ct)).Trips);
    }

    [Fact]
    public async Task PlannedCountKeepsTwentyRequestedAndLeavesEightForAnExplicitSecondTrip()
    {
        await using var f = await Fixture.CreateAsync(kgQuantity: 20);
        var trip = await f.TripAsync();
        var count = new CountPlannedDeliveryLineRequest(Guid.NewGuid(), f.Order.Id, f.KgLine.Id,
            f.Order.Version, trip.Version, 12, "Producción parcial");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            f.Service.CountPlannedAsync(f.TripId, count, f.OtherActor, false, Ct));
        var counted = await f.Service.CountPlannedAsync(f.TripId, count, f.DriverActor, false, Ct);
        var replay = await f.Service.CountPlannedAsync(f.TripId, count, f.DriverActor, false, Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(counted.Id, replay.Id);
        await f.StageAsync(counted.Id, "load", 12);

        await f.Service.PrepareForOrderAsync(f.Order.Id, Ct);
        trip = await f.TripAsync();
        Assert.DoesNotContain(Assert.Single(trip.PlannedOrders!).Lines, x => x.OrderLineId == f.KgLine.Id);
        Assert.Equal(8, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
        await f.Service.DepartAsync(f.TripId, new(Guid.NewGuid(), trip.Version), f.DriverActor, false, Ct);
        trip = await f.TripAsync();
        Assert.NotNull(trip.DepartedAt);
        Assert.Empty(trip.PlannedOrders ?? []);
        Assert.Equal(20, Assert.Single(trip.Assignments).RequestedQuantity);
        Assert.Equal(12, Assert.Single(trip.Assignments).CustodyQuantity);

        var second = await f.CreateTripAsync(2, f.OtherDriver.Id);
        await f.AssignAsync(second, f.KgLine.Id, 8);
        Assert.Equal(0, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
        Assert.Equal(12, (await f.AssignmentAsync(counted.Id)).CustodyQuantity);
    }

    [Fact]
    public async Task DayDriverChangesFirstTripOwnershipAndKeepsHabitualDriverForOtherDates()
    {
        await using var f = await Fixture.CreateAsync();
        var trip = await f.TripAsync();
        var request = new SaveDeliveryDayDriverRequest(Guid.NewGuid(), f.Date, "R-ONE", f.OtherDriver.Id,
            0, trip.Version, "Sustitución del día");
        await f.Service.SaveDayDriverAsync(request, f.ManagerActor, Ct);
        Assert.Empty((await f.Service.DashboardAsync(f.Date, null, f.DriverActor, false, Ct)).Trips);
        var reassigned = Assert.Single((await f.Service.DashboardAsync(f.Date, null, f.OtherActor, false, Ct)).Trips);
        Assert.Equal(f.TripId, reassigned.Id);
        Assert.Equal(f.Order.Id, Assert.Single(reassigned.PlannedOrders!).OrderId);
        var today = Assert.Single((await f.Service.DashboardAsync(f.Date, null, f.ManagerActor, true, Ct)).Routes);
        var tomorrow = Assert.Single((await f.Service.DashboardAsync(f.Date.AddDays(1), null, f.ManagerActor, true, Ct)).Routes);
        Assert.Equal(f.Driver.Id, today.DefaultDriverUserId);
        Assert.Equal(f.OtherDriver.Id, today.DayDriverUserId);
        Assert.Null(tomorrow.DayDriverUserId);

        await f.Service.DepartAsync(reassigned.Id, new(Guid.NewGuid(), reassigned.Version), f.OtherActor, false, Ct);
        var departed = await f.TripAsync();
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.SaveDayDriverAsync(
            request with { OperationId = Guid.NewGuid(), DriverUserId = null, ExpectedVersion = today.DayDriverVersion,
                ExpectedTripVersion = departed.Version }, f.ManagerActor, Ct));
        Assert.Equal(f.OtherDriver.Id, (await f.TripAsync()).DriverUserId);
    }

    [Fact]
    public async Task RouteAndCustomerEditorsShareAssignmentAndRejectStaleEditsWithoutChangingOrders()
    {
        await using var f = await Fixture.CreateAsync();
        var db = f.Db;
        db.DeliveryRoutes.AddRange(new DeliveryRoute { Code = "R-TWO", Name = "Segunda ruta" },
            new DeliveryRoute { Code = "R-INACTIVE", Name = "Ruta inactiva", IsActive = false });
        await db.SaveChangesAsync(Ct);
        var repository = new EfCustomerRepository(db);
        var routeRepository = new EfDeliveryRepository(db);
        var routes = new CustomerRouteAssignmentService(repository, routeRepository, new EfAuditLogRepository(db), f.Clock);
        await using var sheetDb = f.NewContext();
        var sheet = new AdminCustomerCatalogService(new EfCustomerRepository(sheetDb), new EfProductRepository(sheetDb),
            new EfMachineRepository(sheetDb), new EfAuditLogRepository(sheetDb), f.Clock, new EfDeliveryRepository(sheetDb));
        var row = Assert.Single(await routes.GetAssignmentsAsync(Ct));
        var original = await sheet.GetByIdAsync(row.CustomerId, Ct);
        Assert.Equal(original.UpdatedAt, row.UpdatedAt);
        Assert.Equal(new[] { "R-ONE", "R-TWO" }, (await routes.GetActiveRoutesAsync(Ct)).Select(x => x.Code));
        await Assert.ThrowsAsync<ArgumentException>(() => routes.UpdateAsync(row.CustomerId,
            new("R-INACTIVE", null, row.UpdatedAt), f.ManagerActor, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => routes.UpdateAsync(row.CustomerId,
            new("R-TWO", 0, row.UpdatedAt), f.ManagerActor, Ct));

        var moved = await routes.UpdateAsync(row.CustomerId, new("r-two", 3, row.UpdatedAt), f.ManagerActor, Ct);
        Assert.Equal("R-TWO", moved.RouteCode);
        Assert.Equal(3, moved.RouteStopOrder);
        Assert.NotEqual(row.UpdatedAt, moved.UpdatedAt);
        await Assert.ThrowsAsync<CatalogConflictException>(() => routes.UpdateAsync(row.CustomerId,
            new(null, null, row.UpdatedAt), f.ManagerActor, Ct));
        var stale = new UpsertAdminCustomerRequest(original.Name, "5551234567", original.PreferredDeliveryTime, null, null,
            original.DeliveryNotes, original.RouteCode, original.RouteStopOrder, original.HasStoreKey, original.PaymentTermCode,
            original.InternalDeliveryInstructions, ExpectedUpdatedAt: original.UpdatedAt);
        await Assert.ThrowsAsync<CatalogConflictException>(() => sheet.UpdateAsync(row.CustomerId, stale, f.ManagerActor, Ct));

        var refreshed = await sheet.GetByIdAsync(row.CustomerId, Ct);
        Assert.Equal(moved.RouteCode, refreshed.RouteCode);
        Assert.Equal(original.InternalDeliveryInstructions, refreshed.InternalDeliveryInstructions);
        Assert.Equal(original.PaymentTermCode, refreshed.PaymentTermCode);
        Assert.Equal(original.PhoneNumber, refreshed.PhoneNumber);
        var saved = await sheet.UpdateAsync(row.CustomerId, stale with { RouteStopOrder = 4, ExpectedUpdatedAt = refreshed.UpdatedAt },
            f.ManagerActor, Ct);
        var fromSheet = Assert.Single(await routes.GetAssignmentsAsync(Ct));
        Assert.Equal("R-ONE", fromSheet.RouteCode);
        Assert.Equal(4, fromSheet.RouteStopOrder);
        Assert.Equal(saved.UpdatedAt, fromSheet.UpdatedAt);

        db.ChangeTracker.Clear();
        var removed = await routes.UpdateAsync(row.CustomerId, new(null, 4, fromSheet.UpdatedAt), f.ManagerActor, Ct);
        Assert.Null(removed.RouteCode);
        Assert.Null(removed.RouteStopOrder);
        var recordedOrder = await db.Orders.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("R-ONE", recordedOrder.RouteCode);
        Assert.Equal(1, recordedOrder.RouteStopOrder);
        var changes = await db.AuditLogs.Where(x => x.EventType == "CustomerRouteAssignmentChanged").ToArrayAsync(Ct);
        Assert.Equal(2, changes.Length);
        Assert.All(changes, x => Assert.Equal(f.ManagerActor.ActorId, x.ActorId));
        Assert.Contains(changes, x => x.MetadataJson!.Contains("\"previousRouteStopOrder\":4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RouteAssignmentAndAuditRollBackTogetherOnSaveFailure()
    {
        await using var f = await Fixture.CreateAsync();
        var routes = new CustomerRouteAssignmentService(new EfCustomerRepository(f.Db), new EfDeliveryRepository(f.Db),
            new EfAuditLogRepository(f.Db), f.Clock);
        var original = Assert.Single(await routes.GetAssignmentsAsync(Ct));
        f.Failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => routes.UpdateAsync(original.CustomerId,
            new(null, null, original.UpdatedAt), f.ManagerActor, Ct));

        await using var readback = f.NewContext();
        Assert.Equal(original, Assert.Single(await new EfCustomerRepository(readback).GetRouteAssignmentsAsync(Ct)));
        Assert.False(await readback.AuditLogs.AnyAsync(x => x.EventType == "CustomerRouteAssignmentChanged", Ct));
    }

    [Fact]
    public async Task OrderingSchedulesIncludeNewDeliveryRoutesAndKeepLegacyCodes()
    {
        await using var f = await Fixture.CreateAsync();
        var db = f.Db;
        db.DeliveryRoutes.Add(new DeliveryRoute { Code = "R-NEW", Name = "Ruta sin clientes" });
        var legacy = Customer.Create("Cliente en ruta legada", null, null, null, null, null, f.Clock.Now);
        legacy.SetRouteAssignment("ruta legada", null, f.Clock.Now);
        db.Customers.Add(legacy);
        await db.SaveChangesAsync(Ct);
        var schedules = new OrderingScheduleService(new EfOrderingScheduleRepository(db), new EfCustomerRepository(db),
            new EfAuditLogRepository(db), f.Clock, new EfDeliveryRepository(db));
        await schedules.SetRouteAsync("R-ONLY-SCHEDULE", new(new TimeOnly(11, 0), 0), f.ManagerActor, Ct);

        var listed = (await schedules.GetAsync(Ct)).Routes;
        Assert.Contains(listed, x => x.RouteCode == "R-NEW" && x.Cutoff is null && x.Version == 0);
        Assert.Contains(listed, x => x.RouteCode == "RUTA LEGADA");
        Assert.Contains(listed, x => x.RouteCode == "R-ONLY-SCHEDULE" && x.Cutoff == new TimeOnly(11, 0));
    }

    [Fact]
    public async Task PartialDeliverySeparatesCustomerPendingFromUnassignedAndCustodyQuantities()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 50);
        await f.StageAsync(id, "count", 25, "Producción parcial");
        await f.StageAsync(id, "load", 25);
        await f.StageAsync(id, "deliver", 20, "Cliente recibió menos", "No pagó; dejé una caja.", "in-vehicle");

        var assignment = await f.AssignmentAsync(id);
        Assert.Equal(50, assignment.RequestedQuantity);
        Assert.Equal(50, assignment.AssignedQuantity);
        Assert.Equal(25, assignment.ReleasedQuantity);
        Assert.Equal(25, assignment.CountedQuantity);
        Assert.Equal(25, assignment.LoadedQuantity);
        Assert.Equal(20, assignment.DeliveredQuantity);
        Assert.Equal(5, assignment.CustodyQuantity);
        Assert.Equal(30, assignment.PendingQuantity);
        var line = await f.OrderLineAsync(f.KgLine.Id);
        Assert.Equal(50, line.RequestedQuantity);
        Assert.Equal(20, line.DeliveredQuantity);
        Assert.Equal(25, line.AvailableQuantity);
        Assert.Equal(50, (await f.Db.Orders.Include(x => x.Lines).SingleAsync()).Lines.Single(x => x.Id == f.KgLine.Id).Quantity);
        var events = (await f.TripAsync()).Events;
        Assert.Contains(events, e => e.Notes == "No pagó; dejé una caja.");
        Assert.All(events, e => Assert.Equal(f.Clock.Now, e.OccurredAt));
    }

    [Fact]
    public async Task SecondDriverCannotReserveQuantitiesStillUnderFirstDriversCustody()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 50);
        await f.StageAsync(id, "count", 25, "Falta producción");
        await f.StageAsync(id, "load", 25);
        await f.StageAsync(id, "deliver", 20, "Diferencia al entregar", null, "in-vehicle");
        var secondTrip = await f.CreateTripAsync(2, f.OtherDriver.Id);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.AssignAsync(secondTrip, f.KgLine.Id, 30));
        await f.AssignAsync(secondTrip, f.KgLine.Id, 25);
        Assert.Equal(0, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
        Assert.Equal(5, (await f.AssignmentAsync(id)).CustodyQuantity);
    }

    [Fact]
    public async Task LaterVisitDeliversTheRemainingCustodyWithoutCreatingAnotherLoad()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        await f.StageAsync(id, "deliver", 20, "Entrega parcial", null, "in-vehicle");
        await f.StageAsync(id, "deliver", 5, notes: "Segunda visita al mismo cliente.");
        var assignment = await f.AssignmentAsync(id);
        Assert.Equal(25, assignment.LoadedQuantity);
        Assert.Equal(25, assignment.DeliveredQuantity);
        Assert.Equal(0, assignment.CustodyQuantity);
        Assert.Equal(25, assignment.PendingQuantity);
        Assert.Equal(25, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
    }

    [Fact]
    public async Task ClosingTripRequiresEveryLoadedQuantityToHaveADisposition()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        await f.StageAsync(id, "deliver", 20, "Entrega parcial", null, "in-vehicle");
        var trip = await f.TripAsync();
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.CloseAsync(f.TripId,
            new(Guid.NewGuid(), trip.Version), f.DriverActor, false, Ct));
        await f.StageAsync(id, "return", 5, "Regresó a planta");
        trip = await f.TripAsync();
        await f.Service.CloseAsync(f.TripId, new(Guid.NewGuid(), trip.Version, "Saldo pendiente para otra vuelta."),
            f.DriverActor, false, Ct);
        Assert.NotNull((await f.TripAsync()).ClosedAt);
        Assert.Equal(30, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
        Assert.Equal(20, (await f.OrderLineAsync(f.KgLine.Id)).DeliveredQuantity);
    }

    [Fact]
    public async Task TransferPreservesDemandReservationAndChangesCustodyExplicitly()
    {
        await using var f = await Fixture.CreateAsync();
        var sourceId = await f.LoadAsync(25);
        await f.StageAsync(sourceId, "deliver", 20, "Quedan cinco por entregar", null, "in-vehicle");
        var targetId = await f.CreateTripAsync(2, f.OtherDriver.Id);
        var source = await f.AssignmentAsync(sourceId);
        var target = await f.Service.TripAsync(targetId, f.ManagerActor, true, Ct);
        await f.Service.TransferAsync(sourceId, new(Guid.NewGuid(), source.Version, targetId, target.Version, 5,
            "Entrega el compañero en la segunda vuelta."), f.ManagerActor, Ct);
        Assert.Equal(0, (await f.AssignmentAsync(sourceId)).CustodyQuantity);
        target = await f.Service.TripAsync(targetId, f.OtherActor, false, Ct);
        var received = Assert.Single(target.Assignments);
        Assert.Equal(5, received.CustodyQuantity);
        Assert.Equal(5, received.TransferredInQuantity);
        Assert.Equal(25, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
        await f.StageAsync(received.Id, "deliver", 5, actor: f.OtherActor);
        Assert.Equal(25, (await f.OrderLineAsync(f.KgLine.Id)).DeliveredQuantity);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("load")]
    [InlineData("deliver")]
    public async Task DifferencesRequireAReasonAtEveryStage(string stage)
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 25);
        if (stage != "count") await f.StageAsync(id, "count", 25);
        if (stage == "deliver") await f.StageAsync(id, "load", 25);
        await Assert.ThrowsAsync<ArgumentException>(() => f.StageAsync(id, stage, 20));
        var a = await f.AssignmentAsync(id);
        Assert.Equal(stage == "count" ? null : 25m, a.CountedQuantity);
        Assert.Equal(stage == "deliver" ? 25m : null, a.LoadedQuantity);
        Assert.Equal(0, a.DeliveredQuantity);
    }

    [Fact]
    public async Task PartialDeliveryRequiresAnExplicitRemainderDisposition()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        await Assert.ThrowsAsync<ArgumentException>(() => f.StageAsync(id, "deliver", 20, "Cliente ausente"));
        Assert.Equal(25, (await f.AssignmentAsync(id)).CustodyQuantity);
        Assert.Equal(0, (await f.AssignmentAsync(id)).DeliveredQuantity);
    }

    [Fact]
    public async Task ZeroDeliveryRecordsAnAttemptWithAllProductStillUnderCustody()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        await f.StageAsync(id, "deliver", 0, "Cliente no estaba", "No se dejó producto.", "in-vehicle");
        var a = await f.AssignmentAsync(id);
        Assert.Equal(0, a.DeliveredQuantity);
        Assert.Equal(25, a.CustodyQuantity);
        Assert.Contains((await f.TripAsync()).Events, e => e.Quantity == 0 && e.Notes == "No se dejó producto.");
    }

    [Fact]
    public async Task LoadedAndDeliveredQuantitiesCannotExceedAvailableAmounts()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 25);
        await f.StageAsync(id, "count", 20, "Falta producción");
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.StageAsync(id, "load", 21, "Conteo distinto"));
        await f.StageAsync(id, "load", 20);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.StageAsync(id, "deliver", 21, "Conteo distinto"));
        Assert.Equal(20, (await f.AssignmentAsync(id)).CustodyQuantity);
    }

    [Fact]
    public async Task DriverCannotReadOrMutateAnotherDriversTrip()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        var a = await f.AssignmentAsync(id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.TripAsync(f.TripId, f.OtherActor, false, Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RecordAsync(id, "deliver",
            new(Guid.NewGuid(), a.Version, a.CurrentOrderVersion, 25), f.OtherActor, false, Ct));
        Assert.Equal(0, (await f.AssignmentAsync(id)).DeliveredQuantity);
        var ownDashboard = await f.Service.DashboardAsync(f.Date, null, f.OtherActor, false, Ct);
        Assert.Empty(ownDashboard.Trips);
        Assert.Empty(ownDashboard.Orders);
        Assert.Empty(ownDashboard.Drivers);
    }

    [Fact]
    public async Task OwnDashboardShowsAllLinesOfAssignedOrdersWithoutExposingOtherClients()
    {
        await using var f = await Fixture.CreateAsync();
        await f.AssignAsync(f.TripId, f.KgLine.Id, 25);
        var otherCustomer = Customer.Create("Otro cliente sin asignar", null, null, null, null, null, f.Clock.Now);
        otherCustomer.SetPilotDelivery("R-ONE", 2, null, null, null, f.Clock.Now);
        var otherOrder = Order.CreateSubmitted(otherCustomer.Id, f.Order.SalesChannelId, f.Date, f.Clock.Now, 2,
            new(false, false, null, OrderStatus.Submitted),
            [new OrderLine { Id = Guid.NewGuid(), ProductId = f.KgProduct.Id, UnitCode = "kg", Quantity = 15 }], otherCustomer);
        f.Db.AddRange(otherCustomer, otherOrder);
        await f.Db.SaveChangesAsync();
        var own = await f.Service.DashboardAsync(f.Date, null, f.DriverActor, false, Ct);
        var order = Assert.Single(own.Orders);
        Assert.Equal(f.Order.Id, order.OrderId);
        Assert.Equal(2, order.Lines.Count);
        Assert.Contains(order.Lines, x => x.OrderLineId == f.PackageLine.Id && x.RequestedQuantity == 2);
        Assert.DoesNotContain(own.Orders, x => x.OrderId == otherOrder.Id);
        Assert.Empty(own.Routes);
        Assert.Empty(own.Drivers);
    }

    [Fact]
    public async Task DurableReplaysDoNotDuplicateDeliveriesOrEvents()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        var a = await f.AssignmentAsync(id);
        var request = new RecordDeliveryStageRequest(Guid.NewGuid(), a.Version, a.CurrentOrderVersion, 25,
            Notes: "Entregado completo de esta carga.");
        var first = await f.Service.RecordAsync(id, "deliver", request, f.DriverActor, false, Ct);
        var eventCount = (await f.TripAsync()).Events.Count;
        var replay = await f.Service.RecordAsync(id, "deliver", request, f.DriverActor, false, Ct);
        Assert.Equal(first.Id, replay.Id);
        Assert.True(replay.Replayed);
        Assert.Equal(25, (await f.AssignmentAsync(id)).DeliveredQuantity);
        Assert.Equal(eventCount, (await f.TripAsync()).Events.Count);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.RecordAsync(id, "deliver",
            request with { Quantity = 24 }, f.DriverActor, false, Ct));
    }

    [Fact]
    public async Task DeliveryWithoutCollectionRetainsTheHashOfPreCollectionReceipts()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        var a = await f.AssignmentAsync(id);
        var request = new RecordDeliveryStageRequest(Guid.NewGuid(), a.Version, a.CurrentOrderVersion, 25);
        await f.Service.RecordAsync(id, "deliver", request, f.DriverActor, false, Ct);
        var legacyPayload = new { id, request = new { request.OperationId, request.ExpectedVersion,
            request.ExpectedOrderVersion, request.Quantity, request.Reason, request.Notes,
            request.RemainderDisposition, request.AcknowledgeOrderChange } };
        var legacyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(legacyPayload)));
        var operationKey = $"DELIVERY:{f.Driver.Id:N}:deliver:{request.OperationId:N}";
        var receipt = await f.Db.OrderOperations.AsNoTracking().SingleAsync(x => x.OperationKey == operationKey, Ct);
        Assert.Equal(legacyHash, receipt.PayloadHash);
    }

    [Fact]
    public async Task DurableDeliveryReceiptSurvivesANewDbContextAndServiceInstance()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        var a = await f.AssignmentAsync(id);
        var request = new RecordDeliveryStageRequest(Guid.NewGuid(), a.Version, a.CurrentOrderVersion, 25);
        var saved = await f.Service.RecordAsync(id, "deliver", request, f.DriverActor, false, Ct);
        await using var independent = f.NewContext();
        var replay = await f.NewService(independent).RecordAsync(id, "deliver", request, f.DriverActor, false, Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(saved.Id, replay.Id);
        Assert.Equal(25, (await f.AssignmentAsync(id)).DeliveredQuantity);
    }

    [Fact]
    public async Task ReplayingAManagersCommandThroughOwnEndpointDoesNotBypassOwnership()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 25);
        var a = await f.AssignmentAsync(id);
        var request = new RecordDeliveryStageRequest(Guid.NewGuid(), a.Version, a.CurrentOrderVersion, 25);
        await f.Service.RecordAsync(id, "count", request, f.ManagerActor, true, Ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RecordAsync(id, "count", request,
            f.ManagerActor, false, Ct));
        Assert.Equal(25, (await f.AssignmentAsync(id)).CountedQuantity);
    }

    [Fact]
    public async Task ReplayingAManagersCloseStillRequiresCurrentOwnership()
    {
        await using var f = await Fixture.CreateAsync();
        var request = new CloseDeliveryTripRequest(Guid.NewGuid(), (await f.TripAsync()).Version);
        await f.Service.CloseAsync(f.TripId, request, f.ManagerActor, true, Ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CloseAsync(f.TripId, request,
            f.ManagerActor, false, Ct));
    }

    [Fact]
    public async Task SaveFailureRollsBackStageReceiptAndAuditTogether()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 25);
        var eventsBefore = (await f.TripAsync()).Events.Count;
        var auditBefore = await f.Db.AuditLogs.CountAsync();
        var operationsBefore = await f.Db.OrderOperations.CountAsync();
        f.Failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.StageAsync(id, "count", 25));
        f.Failure.Enabled = false;
        f.Db.ChangeTracker.Clear();
        Assert.Null((await f.AssignmentAsync(id)).CountedQuantity);
        Assert.Equal(eventsBefore, (await f.TripAsync()).Events.Count);
        Assert.Equal(auditBefore, await f.Db.AuditLogs.CountAsync());
        Assert.Equal(operationsBefore, await f.Db.OrderOperations.CountAsync());
    }

    [Fact]
    public async Task StaleAssignmentVersionsCannotOverwriteARecordedStage()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 25);
        var a = await f.AssignmentAsync(id);
        await f.StageAsync(id, "count", 25);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.RecordAsync(id, "load",
            new(Guid.NewGuid(), a.Version, a.CurrentOrderVersion, 25), f.DriverActor, false, Ct));
        Assert.Null((await f.AssignmentAsync(id)).LoadedQuantity);
    }

    [Fact]
    public async Task QuantitiesOfDifferentProductsAndUnitsRemainIndependent()
    {
        await using var f = await Fixture.CreateAsync();
        var kg = await f.AssignAsync(f.TripId, f.KgLine.Id, 10);
        var packages = await f.AssignAsync(f.TripId, f.PackageLine.Id, 2);
        await f.StageAsync(kg, "count", 10);
        await f.StageAsync(packages, "count", 2);
        await f.StageAsync(kg, "load", 10);
        await f.StageAsync(packages, "load", 2);
        await f.StageAsync(kg, "deliver", 10);
        await f.StageAsync(packages, "deliver", 2);
        var assignments = (await f.TripAsync()).Assignments;
        Assert.Contains(assignments, x => x.UnitCode == "kg" && x.DeliveredQuantity == 10);
        Assert.Contains(assignments, x => x.UnitCode == "package" && x.DeliveredQuantity == 2);
        Assert.Equal(40, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
        Assert.Equal(0, (await f.OrderLineAsync(f.PackageLine.Id)).AvailableQuantity);
    }

    [Fact]
    public async Task CompletingOneLineDoesNotMarkTheWholeOrderComplete()
    {
        await using var f = await Fixture.CreateAsync();
        var kg = await f.LoadAsync(50);
        await f.StageAsync(kg, "deliver", 50);
        var onlyKg = await f.AssignmentAsync(kg);
        Assert.Equal(0, onlyKg.PendingQuantity);
        Assert.False(onlyKg.OrderIsComplete);
        var packages = await f.AssignAsync(f.TripId, f.PackageLine.Id, 2);
        await f.StageAsync(packages, "count", 2);
        await f.StageAsync(packages, "load", 2);
        await f.StageAsync(packages, "deliver", 2);
        Assert.True((await f.AssignmentAsync(kg)).OrderIsComplete);
        Assert.True((await f.AssignmentAsync(packages)).OrderIsComplete);
    }

    [Fact]
    public async Task ReleasedReservationsCannotBeLoadedOrResurrectedByAZeroDeltaAdjustment()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 50);
        await f.StageAsync(id, "count", 25, "Falta producción");
        var counted = await f.AssignmentAsync(id);
        await f.Service.ReleaseAsync(id, new(Guid.NewGuid(), counted.Version, "Lo entregará otra vuelta"), f.ManagerActor, Ct);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.StageAsync(id, "load", 25));
        var released = await f.AssignmentAsync(id);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.AdjustAsync(id,
            new(Guid.NewGuid(), released.Version, "count", 25, "Intento de reutilizar la reserva liberada"), f.ManagerActor, Ct));
        released = await f.AssignmentAsync(id);
        Assert.Null(released.LoadedQuantity);
        Assert.Equal(50, released.ReleasedQuantity);
        Assert.Equal(0, released.CustodyQuantity);
        Assert.Equal(50, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
    }

    [Fact]
    public async Task PendingRejectedAndNoOrderRecordsNeverBecomeDeliveryCandidates()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var state in new[] { OrderStatus.PendingAdminReview, OrderStatus.Rejected, OrderStatus.NoOrder, OrderStatus.Cancelled, OrderStatus.Superseded })
        {
            var customer = await f.Db.Customers.SingleAsync();
            var order = Order.CreateSubmitted(customer.Id, f.Order.SalesChannelId, f.Date, f.Clock.Now, 10 + (int)state,
                new(false, state == OrderStatus.PendingAdminReview, null, state),
                [new OrderLine { Id = Guid.NewGuid(), ProductId = f.KgProduct.Id, UnitCode = "kg", Quantity = 3 }], customer);
            f.Db.Orders.Add(order);
        }
        await f.Db.SaveChangesAsync();
        var dashboard = await f.Service.DashboardAsync(f.Date, null, f.ManagerActor, true, Ct);
        Assert.Single(dashboard.Orders);
        Assert.Equal(f.Order.Id, dashboard.Orders.Single().OrderId);
    }

    [Theory]
    [InlineData("https://evil.example/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://maps.google.com.evil.example/maps")]
    [InlineData("http://maps.google.com/maps")]
    public async Task DeliveryLocationsRejectUntrustedNavigationTargets(string url)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SaveLocationAsync(f.Order.CustomerId!.Value,
            new(Guid.NewGuid(), url, null), f.ManagerActor, Ct));
        Assert.Null((await f.Service.DashboardAsync(f.Date, null, f.ManagerActor, true, Ct)).Orders.Single().MapsUrl);
    }

    [Fact]
    public async Task TripSnapshotsDoNotChangeWhenClientInstructionsOrLocationChange()
    {
        await using var f = await Fixture.CreateAsync();
        var firstUrl = "https://maps.app.goo.gl/ExampleFirst";
        await f.Service.SaveLocationAsync(f.Order.CustomerId!.Value, new(Guid.NewGuid(), firstUrl, "Acceso principal"), f.ManagerActor, Ct);
        var id = await f.AssignAsync(f.TripId, f.KgLine.Id, 25);
        await f.Service.SaveLocationAsync(f.Order.CustomerId!.Value,
            new(Guid.NewGuid(), "https://maps.app.goo.gl/ExampleSecond", "Otro acceso", 1), f.ManagerActor, Ct);
        var a = await f.AssignmentAsync(id);
        Assert.Equal(firstUrl, a.MapsUrl);
        Assert.Equal("Acceso principal", a.Address);
        await f.Service.RefreshInstructionsAsync(id,
            new(Guid.NewGuid(), a.Version, "Se confirmó otro acceso con el cliente."), f.ManagerActor, Ct);
        a = await f.AssignmentAsync(id);
        Assert.Equal("https://maps.app.goo.gl/ExampleSecond", a.MapsUrl);
        Assert.Equal("Otro acceso", a.Address);
    }

    [Fact]
    public async Task DestructiveOrderCorrectionCannotOrphanLoadedDeliveryHistory()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        var currentOrder = await f.Db.Orders.Include(x => x.Lines).SingleAsync();
        var service = new AdminDailyOrderService(new EfOrderRepository(f.Db), new EfOrderAuditLogRepository(f.Db),
            new EfCustomerRepository(f.Db), new EfProductRepository(f.Db), new EfMachineRepository(f.Db),
            new EfSalesChannelRepository(f.Db), f.Clock, new EfOrderOperationStore(f.Db),
            new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(f.Db), new EfMachineRepository(f.Db), new EfCustomerRepository(f.Db)));
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => service.CorrectAsync(f.Order.Id,
            new("delivery-remove-loaded-line", currentOrder.Version, "Se intenta quitar producto cargado",
                [new(f.PackageProduct.Id, 2, null)]), f.ManagerActor with { IsSystemAdministrator = true }, Ct));
        f.Db.ChangeTracker.Clear();
        Assert.Contains((await f.Db.Orders.Include(x => x.Lines).SingleAsync()).Lines, x => x.Id == f.KgLine.Id);
        Assert.Equal(25, (await f.AssignmentAsync(id)).CustodyQuantity);
    }

    [Fact]
    public async Task CompatibleQuantityCorrectionKeepsIdentityButRequiresReviewBeforeMoreDelivery()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        var currentOrder = await f.Db.Orders.Include(x => x.Lines).SingleAsync();
        var service = new AdminDailyOrderService(new EfOrderRepository(f.Db), new EfOrderAuditLogRepository(f.Db),
            new EfCustomerRepository(f.Db), new EfProductRepository(f.Db), new EfMachineRepository(f.Db),
            new EfSalesChannelRepository(f.Db), f.Clock, new EfOrderOperationStore(f.Db),
            new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(f.Db), new EfMachineRepository(f.Db), new EfCustomerRepository(f.Db)));
        await service.CorrectAsync(f.Order.Id, new("delivery-increase", currentOrder.Version, "Cliente pide diez kilos más",
            [new(f.KgProduct.Id, 60, null), new(f.PackageProduct.Id, 2, null)]), f.ManagerActor with { IsSystemAdministrator = true }, Ct);
        var a = await f.AssignmentAsync(id);
        Assert.True(a.HasOrderChanges);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.StageAsync(id, "deliver", 25));
        await f.StageAsync(id, "deliver", 25, acknowledge: true);
        Assert.Equal(25, (await f.AssignmentAsync(id)).DeliveredQuantity);
        Assert.Equal(60, (await f.OrderLineAsync(f.KgLine.Id)).RequestedQuantity);
    }

    [Fact]
    public async Task ManagerAdjustmentsAppendHistoryAndNeverEraseOriginalLoadingEvidence()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        await f.StageAsync(id, "deliver", 20, "Diferencia de conteo", null, "unresolved");
        var a = await f.AssignmentAsync(id);
        var historyCount = (await f.TripAsync()).Events.Count;
        await f.Service.AdjustAsync(id, new(Guid.NewGuid(), a.Version, "load", 20,
            "Se verificó que la carga real era veinte."), f.ManagerActor, Ct);
        a = await f.AssignmentAsync(id);
        Assert.Equal(20, a.LoadedQuantity);
        Assert.Equal(20, a.DeliveredQuantity);
        Assert.Equal(0, a.CustodyQuantity);
        var events = (await f.TripAsync()).Events;
        Assert.True(events.Count > historyCount);
        Assert.Contains(events, e => e.Quantity == 25);
        Assert.Contains(events, e => e.Quantity == 20 && e.PreviousQuantity == 25);
    }

    [Fact]
    public async Task CorrectingAnOverstatedDeliveryPreservesOriginalEvidenceAndRestoresCustody()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        await f.StageAsync(id, "deliver", 25);
        var before = await f.AssignmentAsync(id);
        await f.Service.AdjustAsync(id, new(Guid.NewGuid(), before.Version, "deliver", 20,
            "Se verificó que el cliente recibió veinte y quedan cinco por aclarar."), f.ManagerActor, Ct);
        var after = await f.AssignmentAsync(id);
        Assert.Equal(20, after.DeliveredQuantity);
        Assert.Equal(5, after.CustodyQuantity);
        Assert.Equal(30, after.PendingQuantity);
        Assert.Equal(25, (await f.OrderLineAsync(f.KgLine.Id)).AvailableQuantity);
        var events = (await f.TripAsync()).Events;
        Assert.Contains(events, x => x.Type == "Delivered" && x.Quantity == 25);
        Assert.Contains(events, x => x.Type == "DeliveredAdjusted" && x.Quantity == 20 && x.PreviousQuantity == 25);
        var tripVersion = (await f.TripAsync()).Version;
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.CloseAsync(f.TripId,
            new(Guid.NewGuid(), tripVersion), f.DriverActor, false, Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedDeliveryEventsCannotBeUpdatedOrDeleted(bool delete)
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.LoadAsync(25);
        await f.StageAsync(id, "deliver", 25);
        var recorded = await f.Db.DeliveryEvents.FirstAsync(x => x.AssignmentId == id && x.Type == "Delivered");
        if (delete) f.Db.DeliveryEvents.Remove(recorded);
        else recorded.Quantity = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        Assert.Equal(25, (await f.Db.DeliveryEvents.SingleAsync(x => x.Id == recorded.Id)).Quantity);
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 11, 2, 14, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 11, 2);
        public TimeOnly LocalTimeOfDay => new(8, 0);
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
            => Enabled ? throw new InvalidOperationException("Synthetic transaction failure after database write.") : ValueTask.FromResult(result);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public PedidosDbContext Db { get; }
        public Clock Clock { get; } = new();
        public DateOnly Date => Clock.Today;
        public Product KgProduct { get; }
        public Product PackageProduct { get; }
        public Order Order { get; }
        public OrderLine KgLine => Order.Lines.Single(x => x.ProductId == KgProduct.Id);
        public OrderLine PackageLine => Order.Lines.Single(x => x.ProductId == PackageProduct.Id);
        public AdminUser Driver { get; }
        public AdminUser OtherDriver { get; }
        public AdminUser Manager { get; }
        public AdminActorContext DriverActor => new(Driver.Id.ToString(), Driver.DisplayName);
        public AdminActorContext OtherActor => new(OtherDriver.Id.ToString(), OtherDriver.DisplayName);
        public AdminActorContext ManagerActor => new(Manager.Id.ToString(), Manager.DisplayName);
        public DeliveryManagementService Service { get; }
        public FailAfterSave Failure { get; }
        public Guid TripId { get; private set; }
        private Fixture(SqliteConnection connection, PedidosDbContext db, Product kgProduct, Product packageProduct,
            Order order, AdminUser driver, AdminUser otherDriver, AdminUser manager, FailAfterSave failure)
        {
            this.connection = connection; Db = db; KgProduct = kgProduct; PackageProduct = packageProduct;
            Order = order; Driver = driver; OtherDriver = otherDriver; Manager = manager; Failure = failure;
            Service = NewService(db);
        }
        public PedidosDbContext NewContext() => new(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
        public DeliveryManagementService NewService(PedidosDbContext db) => new(new EfDeliveryRepository(db),
            new EfOrderRepository(db), new EfCustomerRepository(db), new EfAdminUserRepository(db),
            new EfOrderOperationStore(db), new EfAuditLogRepository(db), Clock);
        public async Task<DeliveryTripResponse> TripAsync()
            => await Service.TripAsync(TripId, ManagerActor, true, Ct);
        public async Task<DeliveryAssignmentResponse> AssignmentAsync(Guid id)
            => (await Service.DashboardAsync(Date, null, ManagerActor, true, Ct)).Trips.SelectMany(x => x.Assignments).Single(x => x.Id == id);
        public async Task<DeliveryOrderLineResponse> OrderLineAsync(Guid lineId)
            => (await Service.DashboardAsync(Date, null, ManagerActor, true, Ct)).Orders.Single(x => x.OrderId == Order.Id)
                .Lines.Single(x => x.OrderLineId == lineId);
        public async Task<Guid> CreateTripAsync(int number, Guid? driver = null)
            => (await Service.CreateTripAsync(new(Guid.NewGuid(), Date, "R-ONE", number, driver,
                driver is not null ? "Sustitución de prueba" : null), ManagerActor, Ct)).Id;
        public async Task<Guid> AssignAsync(Guid tripId, Guid lineId, decimal quantity)
        {
            var trip = await Service.TripAsync(tripId, ManagerActor, true, Ct);
            var orderVersion = (await Db.Orders.AsNoTracking().SingleAsync(x => x.Id == Order.Id)).Version;
            return (await Service.AssignAsync(tripId, new(Guid.NewGuid(), Order.Id, lineId, quantity, trip.Version, orderVersion), ManagerActor, Ct)).Id;
        }
        public async Task StageAsync(Guid assignmentId, string stage, decimal quantity, string? reason = null,
            string? notes = null, string? remainder = null, AdminActorContext? actor = null, bool acknowledge = false)
        {
            var a = await AssignmentAsync(assignmentId);
            await Service.RecordAsync(assignmentId, stage, new(Guid.NewGuid(), a.Version, a.CurrentOrderVersion,
                quantity, reason, notes, remainder, acknowledge), actor ?? DriverActor, false, Ct);
        }
        public async Task<Guid> LoadAsync(decimal quantity)
        {
            var id = await AssignAsync(TripId, KgLine.Id, quantity);
            await StageAsync(id, "count", quantity);
            await StageAsync(id, "load", quantity);
            return id;
        }
        public static async Task<Fixture> CreateAsync(bool createTrip = true, decimal kgQuantity = 50)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var failure = new FailAfterSave();
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection)
                .AddInterceptors(failure).Options);
            await db.Database.EnsureCreatedAsync();
            var clock = new Clock();
            var kg = Product.Create("Producto por kilo", null); kg.SetUnitCode("kg");
            var packages = Product.Create("Producto por paquete", null); packages.SetUnitCode("package");
            var customer = Customer.Create("Cliente ficticio", null, new(9, 0), null, null, "Recibe mostrador", clock.Now);
            customer.SetPilotDelivery("R-ONE", 1, true, "credit", "Usar puerta de servicio", clock.Now);
            var channel = new SalesChannel { Id = Guid.NewGuid(), Name = "Prueba", Type = SalesChannelType.AdminManualCapture };
            var order = Order.CreateSubmitted(customer.Id, channel.Id, clock.Today, clock.Now, 1,
                new(false, false, null, OrderStatus.Submitted),
                [new OrderLine { Id = Guid.NewGuid(), ProductId = kg.Id, ProductNameSnapshot = kg.Name, UnitCode = "kg", Quantity = kgQuantity },
                 new OrderLine { Id = Guid.NewGuid(), ProductId = packages.Id, ProductNameSnapshot = packages.Name, UnitCode = "package", Quantity = 2 }], customer);
            var driver = AdminUser.Create("driver-one", "Repartidor uno", "fictitious-hash", clock.Now);
            var other = AdminUser.Create("driver-two", "Repartidor dos", "fictitious-hash", clock.Now);
            var manager = AdminUser.Create("delivery-manager", "Responsable de reparto", "fictitious-hash", clock.Now);
            var permission = SecurityDefaults.Permissions.Single(x => x.Key == "delivery.operate.own");
            db.AddRange(kg, packages, customer, channel, order, driver, other, manager, permission);
            db.SecurityUserPermissions.AddRange(new SecurityUserPermission { UserId = driver.Id, PermissionId = permission.Id },
                new SecurityUserPermission { UserId = other.Id, PermissionId = permission.Id });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var fixture = new Fixture(connection, db, kg, packages, order, driver, other, manager, failure);
            await fixture.Service.SaveRouteAsync(new(Guid.NewGuid(), "R-ONE", "Ruta de prueba", driver.Id), fixture.ManagerActor, Ct);
            if (createTrip) fixture.TripId = await fixture.CreateTripAsync(1);
            return fixture;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
