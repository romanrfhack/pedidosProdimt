using System.Security.Cryptography;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.Delivery;

/// <summary>Delivery quantities are a separate ledger; they never replace the customer's order.</summary>
public sealed class DeliveryManagementService(IDeliveryRepository delivery, IOrderRepository orders,
    ICustomerRepository customers, IAdminUserRepository users, IOrderOperationStore operations,
    IAuditLogRepository audit, IDateTimeProvider clock)
{
    public async Task<DeliveryDashboardResponse> DashboardAsync(DateOnly? date, string? routeCode,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        var userId = ActorId(actor);
        var serviceDate = date ?? clock.Today;
        var route = Optional(routeCode);
        var trips = (await delivery.GetTripsAsync(serviceDate, token))
            .Where(x => (manage || x.DriverUserId == userId) && (route is null || SameRoute(x.RouteCode, route))).ToArray();
        var tripResponses = new List<DeliveryTripResponse>();
        foreach (var trip in trips) tripResponses.Add(await MapTripAsync(trip, token));
        var routes = manage ? (await delivery.GetRoutesAsync(token)).Select(x => new DeliveryRouteResponse(x.Code, x.Name,
            x.DefaultDriverUserId, x.IsActive, x.Version)).ToArray() : [];
        var drivers = manage ? (await delivery.GetDriversAsync(token)).Select(x => new DeliveryDriverResponse(x.Id, x.DisplayName, x.UserName)).ToArray() : [];
        var assignedOrderIds = tripResponses.SelectMany(x => x.Assignments).Select(x => x.OrderId).ToHashSet();
        var candidates = (await orders.GetByDateAsync(serviceDate, token))
            .Where(x => Ready(x) && x.CustomerId is not null && (manage || assignedOrderIds.Contains(x.Id)) &&
                (route is null || SameRoute(x.RouteCode, route)))
            .OrderBy(x => x.RouteCode).ThenBy(x => x.RouteStopOrder ?? int.MaxValue).ThenBy(x => x.SequenceNumber);
        var orderResponses = new List<DeliveryOrderResponse>();
        foreach (var order in candidates)
        {
            var customer = await customers.GetByIdAsync(order.CustomerId!.Value, token);
            var location = await delivery.GetLocationAsync(order.CustomerId.Value, token);
            var assignments = await delivery.GetOrderAssignmentsAsync(order.Id, token);
            orderResponses.Add(new(order.Id, order.CustomerId.Value, order.CustomerNameSnapshot ?? customer?.Name ?? "Cliente",
                order.SequenceNumber, order.RouteCode, order.RouteStopOrder, order.Version, location?.MapsUrl, location?.Address,
                order.RequestedDeliveryTime, order.RequestedDeliveryWindowStart, order.RequestedDeliveryWindowEnd,
                order.HasStoreKey, order.PaymentTermCode, order.DeliveryNotes, order.InternalDeliveryInstructions,
                location?.Version ?? 0, order.Lines.Select(line =>
                {
                    var same = assignments.Where(x => x.OrderLineId == line.Id).ToArray();
                    var delivered = same.Sum(x => x.DeliveredQuantity);
                    var commitment = same.Sum(x => x.CommitmentQuantity);
                    return new DeliveryOrderLineResponse(line.Id, line.ProductId, line.ProductNameSnapshot ?? "Producto",
                        line.UnitCode, line.Quantity, delivered, Math.Max(commitment - delivered, 0), Math.Max(line.Quantity - commitment, 0));
                }).ToArray()));
        }
        return new(serviceDate, routes, drivers, tripResponses, orderResponses);
    }

    public async Task<DeliveryTripResponse> TripAsync(Guid id, AdminActorContext actor, bool manage, CancellationToken token)
        => await MapTripAsync(await RequireTripAsync(id, actor, manage, false, token), token);

    public Task<DeliveryCommandResponse> SaveRouteAsync(SaveDeliveryRouteRequest request, AdminActorContext actor, CancellationToken token)
    {
        var code = Required(request.Code, 80, "Indica el código de ruta.").ToUpperInvariant();
        var name = Required(request.Name, 160, "Indica el nombre de la ruta.");
        return ExecuteAsync("route", request.OperationId, request, actor, async ct =>
        {
            if (request.DefaultDriverUserId is { } driverId) await RequireDriverAsync(driverId, ct);
            var route = await delivery.GetRouteAsync(code, ct);
            if (route is null)
            {
                if (request.ExpectedVersion is not (null or 0)) Conflict("La ruta ya no coincide con la versión cargada.");
                route = new DeliveryRoute { Code = code, Name = name, DefaultDriverUserId = request.DefaultDriverUserId, IsActive = request.IsActive };
                await delivery.AddRouteAsync(route, ct);
            }
            else
            {
                Version(route.Version, request.ExpectedVersion ?? 0);
                route.Name = name; route.DefaultDriverUserId = request.DefaultDriverUserId;
                route.IsActive = request.IsActive; route.Version++;
            }
            await AuditAsync(code, "DeliveryRouteSaved", actor, new { route.Code, route.Version }, ct);
            return new(Guid.Empty, route.Version);
        }, token);
    }

    public Task<DeliveryCommandResponse> SaveLocationAsync(Guid customerId, SaveDeliveryLocationRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        var url = ValidateMapsUrl(request.MapsUrl);
        var address = Optional(request.Address, 500);
        return ExecuteAsync("location", request.OperationId, new { customerId, request }, actor, async ct =>
        {
            _ = await customers.GetByIdAsync(customerId, ct) ?? throw new KeyNotFoundException("No se encontró el cliente.");
            var item = await delivery.GetLocationAsync(customerId, ct);
            if (item is null)
            {
                Version(0, request.ExpectedVersion);
                item = new CustomerDeliveryLocation { CustomerId = customerId, MapsUrl = url, Address = address };
                await delivery.AddLocationAsync(item, ct);
            }
            else
            {
                Version(item.Version, request.ExpectedVersion); item.MapsUrl = url; item.Address = address; item.Version++;
            }
            // Operational access instructions and URLs are not copied into general audit metadata.
            await AuditAsync(customerId.ToString(), "DeliveryLocationSaved", actor, new { item.Version }, ct);
            return new(customerId, item.Version);
        }, token);
    }

    public Task<DeliveryCommandResponse> CreateTripAsync(CreateDeliveryTripRequest request, AdminActorContext actor, CancellationToken token)
    {
        if (request.Date == default || request.TripNumber is < 1 or > 999) throw new ArgumentException("Indica fecha y número de vuelta entre 1 y 999.");
        var code = Required(request.RouteCode, 80, "Selecciona una ruta.").ToUpperInvariant();
        var reason = Optional(request.Reason, 500);
        return ExecuteAsync("trip", request.OperationId, request, actor, async ct =>
        {
            var route = await delivery.GetRouteAsync(code, ct) ?? throw new KeyNotFoundException("No se encontró la ruta.");
            if (!route.IsActive) throw new ArgumentException("La ruta está inactiva.");
            var driverId = request.DriverUserId ?? route.DefaultDriverUserId ?? throw new ArgumentException("Asigna un repartidor a la vuelta.");
            var driver = await RequireDriverAsync(driverId, ct);
            if (route.DefaultDriverUserId is { } usual && driverId != usual && reason is null)
                throw new ArgumentException("Indica el motivo del repartidor sustituto.");
            if ((await delivery.GetTripsAsync(request.Date, ct)).Any(x => SameRoute(x.RouteCode, code) && x.TripNumber == request.TripNumber))
                Conflict("Esta ruta ya tiene esa vuelta para la fecha. Actualiza el listado.");
            var trip = new DeliveryTrip { Id = Guid.NewGuid(), Date = request.Date, RouteCode = code, RouteName = route.Name,
                TripNumber = request.TripNumber, DriverUserId = driver.Id, DriverName = driver.DisplayName, CreatedAt = clock.Now };
            await delivery.AddTripAsync(trip, ct);
            await EventAsync(trip.Id, null, "TripCreated", null, null, reason, null, actor, ct);
            return new(trip.Id, trip.Version);
        }, token);
    }

    public Task<DeliveryCommandResponse> AssignAsync(Guid tripId, AssignDeliveryLineRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        Quantity(request.Quantity, false);
        return ExecuteAsync("assign", request.OperationId, new { tripId, request }, actor, async ct =>
        {
            var trip = await RequireTripAsync(tripId, actor, true, true, ct);
            Version(trip.Version, request.ExpectedTripVersion);
            var order = await RequireOrderAsync(request.OrderId, request.ExpectedOrderVersion, ct);
            var line = order.Lines.SingleOrDefault(x => x.Id == request.OrderLineId) ?? throw new KeyNotFoundException("No se encontró la partida.");
            if (order.CustomerId is null || !SameRoute(trip.RouteCode, order.RouteCode) || order.OrderDate != trip.Date)
                throw new ArgumentException("El pedido debe corresponder a la ruta y fecha de la vuelta.");
            if (line.UnitCode is null || !ProductUnitCodes.IsSupported(line.UnitCode))
                throw new ArgumentException("Confirma la unidad del producto antes de asignarlo al reparto.");
            await EnsureCapacityAsync(order, line.Id, request.Quantity, null, ct);
            var customer = await customers.GetByIdAsync(order.CustomerId.Value, ct);
            var location = await delivery.GetLocationAsync(order.CustomerId.Value, ct);
            var item = new DeliveryAssignment { Id = Guid.NewGuid(), TripId = trip.Id, OrderId = order.Id, OrderLineId = line.Id,
                ProductId = line.ProductId, CustomerId = order.CustomerId.Value, CustomerName = order.CustomerNameSnapshot ?? customer?.Name ?? "Cliente",
                SequenceNumber = order.SequenceNumber, RouteStopOrder = order.RouteStopOrder, ProductName = line.ProductNameSnapshot ?? "Producto",
                UnitCode = line.UnitCode, RequestedQuantity = line.Quantity, AssignedQuantity = request.Quantity,
                AcknowledgedOrderVersion = order.Version, MapsUrl = location?.MapsUrl, Address = location?.Address,
                RequestedDeliveryTime = order.RequestedDeliveryTime, RequestedDeliveryWindowStart = order.RequestedDeliveryWindowStart,
                RequestedDeliveryWindowEnd = order.RequestedDeliveryWindowEnd, HasStoreKey = order.HasStoreKey,
                PaymentTermCode = order.PaymentTermCode, DeliveryNotes = order.DeliveryNotes, InternalDeliveryInstructions = order.InternalDeliveryInstructions };
            await delivery.AddAssignmentAsync(item, ct); trip.Version++;
            await EventAsync(trip.Id, item.Id, "Assigned", request.Quantity, null, null, null, actor, ct);
            return new(item.Id, item.Version);
        }, token);
    }

    public async Task<DeliveryCommandResponse> RecordAsync(Guid id, string stage, RecordDeliveryStageRequest request,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        Quantity(request.Quantity, true);
        var reason = Optional(request.Reason, 500); var notes = Optional(request.Notes, 2000);
        if (stage is not ("count" or "load" or "deliver" or "return")) throw new ArgumentException("La etapa de reparto no es válida.");
        // Authorize before reading a durable receipt too: a former manager must not
        // replay an operation for another driver's trip through the own endpoint.
        if (!manage)
        {
            var scoped = await delivery.GetAssignmentAsync(id, token) ?? throw new KeyNotFoundException("No se encontró la asignación.");
            await RequireTripAsync(scoped.TripId, actor, false, false, token);
        }
        return await ExecuteAsync(stage, request.OperationId, new { id, request }, actor, async ct =>
        {
            var item = await RequireAssignmentAsync(id, actor, manage, ct);
            Version(item.Version, request.ExpectedVersion);
            if (item.CommitmentQuantity == 0 && item.CustodyQuantity == 0)
                Conflict("Esta reserva ya fue liberada. Crea una nueva asignación para continuar el reparto.");
            var order = await RequireOrderAsync(item.OrderId, request.ExpectedOrderVersion, ct);
            var line = order.Lines.SingleOrDefault(x => x.Id == item.OrderLineId);
            if (line is null || line.ProductId != item.ProductId || line.UnitCode != item.UnitCode)
                Conflict("La identidad de la partida cambió. Solicita revisión al responsable de reparto.");
            if (item.AcknowledgedOrderVersion != order.Version && !request.AcknowledgeOrderChange)
                Conflict("El pedido cambió después de asignarlo. Revisa las cantidades y confirma el aviso antes de continuar.");
            if (item.AcknowledgedOrderVersion != order.Version)
            {
                await EventAsync(item.TripId, item.Id, "OrderChangeAcknowledged", null, null,
                    "Pedido actualizado revisado por el repartidor.", null, actor, ct);
                item.AcknowledgedOrderVersion = order.Version;
            }
            var trip = (await delivery.GetTripAsync(item.TripId, ct))!;
            decimal previous;
            if (stage == "count")
            {
                if (item.CountedQuantity is not null || item.LoadedQuantity is not null || item.TransferredInQuantity > 0)
                    Conflict("El conteo ya está registrado. Una corrección requiere al responsable de reparto.");
                previous = item.CommitmentQuantity;
                AtMost(request.Quantity, previous);
                DifferenceReason(request.Quantity, previous, reason);
                item.CountedQuantity = request.Quantity;
                item.ReleasedQuantity += previous - request.Quantity;
            }
            else if (stage == "load")
            {
                if (item.CountedQuantity is null) throw new ArgumentException("Primero registra el conteo.");
                if (item.LoadedQuantity is not null || item.TransferredInQuantity > 0) Conflict("La carga ya está registrada.");
                previous = item.CountedQuantity.Value;
                AtMost(request.Quantity, previous);
                AtMost(request.Quantity, item.CommitmentQuantity);
                DifferenceReason(request.Quantity, previous, reason);
                item.LoadedQuantity = request.Quantity;
                item.ReleasedQuantity += previous - request.Quantity;
            }
            else if (stage == "deliver")
            {
                if (item.LoadedQuantity is null && item.TransferredInQuantity <= 0) throw new ArgumentException("Primero confirma la carga.");
                previous = item.CustodyQuantity;
                AtMost(request.Quantity, previous);
                DifferenceReason(request.Quantity, previous, reason);
                if (request.Quantity == 0 && reason is null) throw new ArgumentException("Indica por qué no se entregó el producto.");
                if (request.Quantity < previous && request.RemainderDisposition is not ("in-vehicle" or "unresolved"))
                    throw new ArgumentException("Indica si el producto no entregado sigue en la camioneta o requiere aclaración.");
                var all = await delivery.GetOrderAssignmentsAsync(item.OrderId, ct);
                if (all.Where(x => x.OrderLineId == item.OrderLineId).Sum(x => x.DeliveredQuantity) + request.Quantity > line!.Quantity)
                    Conflict("La entrega acumulada superaría la cantidad vigente del pedido.");
                item.DeliveredQuantity += request.Quantity;
                item.HasUnresolvedIncident = request.Quantity < previous && request.RemainderDisposition == "unresolved";
                if (item.CustodyQuantity == 0) item.HasUnresolvedIncident = false;
            }
            else
            {
                if (reason is null) throw new ArgumentException("Indica el motivo de la devolución.");
                if (request.Quantity == 0) throw new ArgumentException("La devolución requiere una cantidad positiva.");
                previous = item.CustodyQuantity; AtMost(request.Quantity, previous);
                item.ReturnedQuantity += request.Quantity;
                item.ReleasedQuantity += request.Quantity;
                if (item.CustodyQuantity == 0) item.HasUnresolvedIncident = false;
            }
            item.Version++; trip.Version++;
            await EventAsync(item.TripId, item.Id, stage switch { "count" => "Counted", "load" => "Loaded", "deliver" => "Delivered", _ => "Returned" },
                request.Quantity, previous, reason, notes, actor, ct);
            if (stage is "count" or "load" && previous != request.Quantity)
                await EventAsync(item.TripId, item.Id, "UnloadedReservationReleased", previous - request.Quantity, null,
                    reason, "Saldo no cargado disponible para otra vuelta.", actor, ct);
            if (stage == "deliver" && request.Quantity < previous)
                await EventAsync(item.TripId, item.Id, request.RemainderDisposition == "in-vehicle" ? "RemainsInVehicle" : "IncidentUnresolved",
                    previous - request.Quantity, null, reason, notes, actor, ct);
            return new(item.Id, item.Version);
        }, token);
    }

    public Task<DeliveryCommandResponse> ReleaseAsync(Guid id, ReleaseDeliveryAssignmentRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        var reason = Required(request.Reason, 500, "Indica el motivo de liberar la reserva.");
        return ExecuteAsync("release", request.OperationId, new { id, request }, actor, async ct =>
        {
            var item = await RequireAssignmentAsync(id, actor, true, ct); Version(item.Version, request.ExpectedVersion);
            var releasable = item.CommitmentQuantity - item.DeliveredQuantity - item.CustodyQuantity;
            if (releasable <= 0) throw new ArgumentException("No hay reserva sin cargar. El producto en camioneta requiere devolución o transferencia.");
            item.ReleasedQuantity += releasable; item.Version++;
            var trip = (await delivery.GetTripAsync(item.TripId, ct))!; trip.Version++;
            await EventAsync(item.TripId, id, "ReservationReleased", releasable, null, reason, null, actor, ct);
            return new(id, item.Version);
        }, token);
    }

    public Task<DeliveryCommandResponse> RefreshInstructionsAsync(Guid id, ReleaseDeliveryAssignmentRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        var reason = Required(request.Reason, 500, "Indica por qué actualizas la ficha de esta vuelta.");
        return ExecuteAsync("instructions", request.OperationId, new { id, request }, actor, async ct =>
        {
            var item = await RequireAssignmentAsync(id, actor, true, ct); Version(item.Version, request.ExpectedVersion);
            var customer = await customers.GetByIdAsync(item.CustomerId, ct)
                ?? throw new KeyNotFoundException("No se encontró el cliente.");
            var location = await delivery.GetLocationAsync(item.CustomerId, ct);
            var order = await orders.GetByIdAsync(item.OrderId, ct)
                ?? throw new KeyNotFoundException("No se encontró el pedido.");
            item.MapsUrl = location?.MapsUrl; item.Address = location?.Address;
            item.HasStoreKey = customer.HasStoreKey; item.PaymentTermCode = customer.PaymentTermCode;
            item.DeliveryNotes = order.DeliveryNotes; item.InternalDeliveryInstructions = customer.InternalDeliveryInstructions;
            // Keep the delivery conditions promised for this order, rather than
            // replacing them with the customer's defaults for another day.
            item.RequestedDeliveryTime = order.RequestedDeliveryTime;
            item.RequestedDeliveryWindowStart = order.RequestedDeliveryWindowStart;
            item.RequestedDeliveryWindowEnd = order.RequestedDeliveryWindowEnd;
            item.Version++; var trip = (await delivery.GetTripAsync(item.TripId, ct))!; trip.Version++;
            await EventAsync(item.TripId, item.Id, "InstructionsUpdated", null, null, reason,
                "Ficha vigente aplicada explícitamente a la vuelta abierta.", actor, ct);
            return new(item.Id, item.Version);
        }, token);
    }

    public Task<DeliveryCommandResponse> TransferAsync(Guid id, TransferDeliveryRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        Quantity(request.Quantity, false); var reason = Required(request.Reason, 500, "Indica el motivo de la transferencia.");
        return ExecuteAsync("transfer", request.OperationId, new { id, request }, actor, async ct =>
        {
            var source = await RequireAssignmentAsync(id, actor, true, ct); Version(source.Version, request.ExpectedVersion);
            if (source.HasUnresolvedIncident) Conflict("Primero aclara el producto no localizado antes de transferirlo.");
            AtMost(request.Quantity, source.CustodyQuantity);
            var targetTrip = await RequireTripAsync(request.TargetTripId, actor, true, true, ct);
            Version(targetTrip.Version, request.ExpectedTargetTripVersion);
            var sourceTrip = (await delivery.GetTripAsync(source.TripId, ct))!;
            if (sourceTrip.Id == targetTrip.Id || sourceTrip.Date != targetTrip.Date)
                throw new ArgumentException("Selecciona otra vuelta abierta de la misma fecha.");
            if (targetTrip.DriverUserId == sourceTrip.DriverUserId) throw new ArgumentException("La transferencia requiere otro repartidor.");
            if (!SameRoute(sourceTrip.RouteCode, targetTrip.RouteCode))
                throw new ArgumentException("La vuelta receptora debe corresponder a la misma ruta.");
            await RequireDriverAsync(targetTrip.DriverUserId, ct);
            var target = CloneForTransfer(source, targetTrip.Id, request.Quantity);
            source.TransferredOutQuantity += request.Quantity; source.ReleasedQuantity += request.Quantity; source.Version++;
            sourceTrip.Version++; targetTrip.Version++;
            await delivery.AddAssignmentAsync(target, ct);
            await EventAsync(source.TripId, source.Id, "TransferredOut", request.Quantity, null, reason,
                $"Vuelta receptora: {targetTrip.Id}.", actor, ct);
            await EventAsync(target.TripId, target.Id, "TransferredIn", request.Quantity, null, reason,
                $"Vuelta de origen: {source.TripId}.", actor, ct);
            return new(target.Id, target.Version);
        }, token);
    }

    public Task<DeliveryCommandResponse> AdjustAsync(Guid id, AdjustDeliveryRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        Quantity(request.Quantity, true); var reason = Required(request.Reason, 500, "Indica el motivo de la corrección.");
        if (request.Stage is not ("count" or "load" or "deliver")) throw new ArgumentException("Selecciona conteo, carga o entrega para la corrección.");
        return ExecuteAsync("adjust", request.OperationId, new { id, request }, actor, async ct =>
        {
            var item = await RequireAssignmentAsync(id, actor, true, ct); Version(item.Version, request.ExpectedVersion);
            if (item.CommitmentQuantity == 0 && item.CustodyQuantity == 0)
                Conflict("Esta reserva ya fue liberada. Crea una nueva asignación en lugar de reactivarla.");
            var order = await orders.GetByIdAsync(item.OrderId, ct) ?? throw new KeyNotFoundException("No se encontró el pedido.");
            if (!Ready(order)) Conflict("El pedido ya no está autorizado para reparto.");
            decimal previous;
            if (request.Stage == "count")
            {
                if (item.CountedQuantity is null || item.LoadedQuantity is not null || item.TransferredInQuantity > 0)
                    throw new ArgumentException("Sólo se corrige el conteo antes de cargar; después corrige la carga.");
                previous = item.CountedQuantity.Value;
                AtMost(request.Quantity, item.AssignedQuantity);
                var delta = request.Quantity - previous;
                if (delta > 0) await EnsureCapacityAsync(order, item.OrderLineId, delta, null, ct);
                item.CountedQuantity = request.Quantity; item.ReleasedQuantity -= delta;
            }
            else if (request.Stage == "load")
            {
                if (item.LoadedQuantity is null || item.TransferredInQuantity > 0) throw new ArgumentException("No hay una carga propia corregible.");
                previous = item.LoadedQuantity.Value;
                AtMost(request.Quantity, item.CountedQuantity ?? 0);
                var minimum = item.DeliveredQuantity + item.ReturnedQuantity + item.TransferredOutQuantity;
                if (request.Quantity < minimum) Conflict("La corrección no puede reducir lo ya entregado, devuelto o transferido.");
                var delta = request.Quantity - previous;
                if (delta > 0) await EnsureCapacityAsync(order, item.OrderLineId, delta, null, ct);
                item.LoadedQuantity = request.Quantity; item.ReleasedQuantity -= delta;
            }
            else
            {
                if (item.LoadedQuantity is null && item.TransferredInQuantity <= 0)
                    throw new ArgumentException("No hay producto cargado para corregir la entrega.");
                previous = item.DeliveredQuantity;
                var available = (item.LoadedQuantity ?? 0) + item.TransferredInQuantity - item.ReturnedQuantity - item.TransferredOutQuantity;
                AtMost(request.Quantity, available);
                var line = order.Lines.SingleOrDefault(x => x.Id == item.OrderLineId)
                    ?? throw new CustomerOrderConflictException("La partida cambió y requiere revisión.");
                var otherDelivered = (await delivery.GetOrderAssignmentsAsync(item.OrderId, ct))
                    .Where(x => x.OrderLineId == item.OrderLineId && x.Id != item.Id).Sum(x => x.DeliveredQuantity);
                if (request.Quantity + otherDelivered > line.Quantity) Conflict("La corrección superaría lo pedido por el cliente.");
                item.DeliveredQuantity = request.Quantity;
                if (request.Quantity < previous && item.CustodyQuantity > 0) item.HasUnresolvedIncident = true;
            }
            if (item.CustodyQuantity == 0) item.HasUnresolvedIncident = false;
            item.Version++; var trip = (await delivery.GetTripAsync(item.TripId, ct))!; trip.Version++;
            await EventAsync(item.TripId, item.Id, request.Stage switch { "count" => "CountAdjusted", "load" => "LoadAdjusted", _ => "DeliveredAdjusted" }, request.Quantity,
                previous, reason, "Corrección autorizada; el movimiento anterior permanece en bitácora.", actor, ct);
            return new(item.Id, item.Version);
        }, token);
    }

    public async Task<DeliveryCommandResponse> CloseAsync(Guid id, CloseDeliveryTripRequest request,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        var notes = Optional(request.Notes, 2000);
        if (!manage) await RequireTripAsync(id, actor, false, false, token);
        return await ExecuteAsync("close", request.OperationId, new { id, request }, actor, async ct =>
        {
            var trip = await RequireTripAsync(id, actor, manage, true, ct); Version(trip.Version, request.ExpectedVersion);
            var assignments = await delivery.GetAssignmentsAsync(id, ct);
            if (assignments.Any(x => x.CustodyQuantity != 0 || x.HasUnresolvedIncident))
                Conflict("Hay producto en camioneta o una incidencia pendiente. Registra entrega, devolución o aclaración antes de cerrar.");
            if (assignments.Any(x => x.CommitmentQuantity > x.DeliveredQuantity))
                Conflict("Hay reservas sin resolver. Confirma el conteo/carga en cero con motivo o pide liberar la reserva.");
            trip.Status = "Closed"; trip.ClosedAt = clock.Now; trip.Version++;
            await EventAsync(id, null, "TripClosed", null, null, null, notes, actor, ct);
            return new(id, trip.Version);
        }, token);
    }

    private async Task<DeliveryTripResponse> MapTripAsync(DeliveryTrip trip, CancellationToken token)
    {
        var assignments = await delivery.GetAssignmentsAsync(trip.Id, token);
        var events = await delivery.GetEventsAsync(trip.Id, token);
        var mapped = new List<DeliveryAssignmentResponse>();
        foreach (var item in assignments.OrderBy(x => x.RouteStopOrder ?? int.MaxValue).ThenBy(x => x.CustomerName).ThenBy(x => x.SequenceNumber))
        {
            var order = await orders.GetByIdAsync(item.OrderId, token);
            var line = order?.Lines.SingleOrDefault(x => x.Id == item.OrderLineId);
            var all = await delivery.GetOrderAssignmentsAsync(item.OrderId, token);
            var fulfilled = all.Where(x => x.OrderLineId == item.OrderLineId).Sum(x => x.DeliveredQuantity);
            var requested = line?.Quantity ?? item.RequestedQuantity;
            var pending = Math.Max(requested - fulfilled, 0);
            var attempted = events.Any(e => e.AssignmentId == item.Id && e.Type == "Delivered");
            var status = item.HasUnresolvedIncident ? "Incident" : pending == 0 ? "Delivered" :
                item.DeliveredQuantity > 0 ? "Partial" : attempted ? "NotDelivered" : item.CustodyQuantity > 0 ? "Loaded" :
                item.LoadedQuantity is not null ? (item.LoadedQuantity == 0 ? "NotDelivered" : "Loaded") :
                item.CountedQuantity is not null ? "Counted" : "PendingCount";
            if (!attempted && item.CommitmentQuantity == 0 && item.DeliveredQuantity == 0 && item.CustodyQuantity == 0) status = "Released";
            mapped.Add(new(item.Id, item.OrderId, item.OrderLineId, item.CustomerId, item.CustomerName, item.SequenceNumber,
                item.RouteStopOrder, item.ProductName, item.UnitCode, requested, item.AssignedQuantity,
                item.ReleasedQuantity, item.CountedQuantity, item.LoadedQuantity, item.DeliveredQuantity, item.ReturnedQuantity,
                item.TransferredOutQuantity, item.TransferredInQuantity, item.CustodyQuantity, pending, status, item.Version,
                order?.Version ?? 0, order is null || !Ready(order) || line is null || line.ProductId != item.ProductId ||
                line.UnitCode != item.UnitCode || item.AcknowledgedOrderVersion != order.Version,
                item.MapsUrl, item.Address, item.RequestedDeliveryTime, item.RequestedDeliveryWindowStart, item.RequestedDeliveryWindowEnd,
                item.HasStoreKey, item.PaymentTermCode, item.DeliveryNotes, item.InternalDeliveryInstructions,
                order is not null && Ready(order) && order.Lines.All(part =>
                    all.Where(a => a.OrderLineId == part.Id).Sum(a => a.DeliveredQuantity) >= part.Quantity)));
        }
        return new(trip.Id, trip.Date, trip.RouteCode, trip.RouteName, trip.TripNumber, trip.DriverUserId,
            trip.DriverName, trip.Status, trip.Version, trip.CreatedAt, trip.ClosedAt, mapped,
            events.Select(e => new DeliveryEventResponse(e.Id, e.AssignmentId,
                e.Type, e.Quantity, e.PreviousQuantity, e.Reason, e.Notes, e.ActorName, e.OccurredAt)).ToArray());
    }

    private async Task<DeliveryTrip> RequireTripAsync(Guid id, AdminActorContext actor, bool manage, bool open, CancellationToken token)
    {
        var trip = await delivery.GetTripAsync(id, token) ?? throw new KeyNotFoundException("No se encontró la vuelta.");
        if (!manage && trip.DriverUserId != ActorId(actor)) throw new UnauthorizedAccessException("La vuelta está asignada a otro repartidor.");
        if (open && trip.Status != "Open") Conflict("La vuelta está cerrada y conserva su historial.");
        return trip;
    }
    private async Task<DeliveryAssignment> RequireAssignmentAsync(Guid id, AdminActorContext actor, bool manage, CancellationToken token)
    {
        var item = await delivery.GetAssignmentAsync(id, token) ?? throw new KeyNotFoundException("No se encontró la asignación.");
        await RequireTripAsync(item.TripId, actor, manage, true, token); return item;
    }
    private async Task<Order> RequireOrderAsync(Guid id, int expectedVersion, CancellationToken token)
    {
        var order = await orders.GetByIdAsync(id, token) ?? throw new KeyNotFoundException("No se encontró el pedido.");
        if (!Ready(order)) Conflict("El pedido no está autorizado para reparto.");
        Version(order.Version, expectedVersion); return order;
    }
    private async Task<AdminUser> RequireDriverAsync(Guid id, CancellationToken token)
    {
        var user = await users.GetByIdAsync(id, token);
        if (user is null || !user.IsActive || user.IsDeleted || !(await delivery.GetDriversAsync(token)).Any(x => x.Id == id))
            throw new ArgumentException("El repartidor debe estar activo y tener permiso para operar su reparto.");
        return user;
    }
    private async Task EnsureCapacityAsync(Order order, Guid lineId, decimal additional, Guid? excludeId, CancellationToken token)
    {
        var line = order.Lines.SingleOrDefault(x => x.Id == lineId) ?? throw new KeyNotFoundException("No se encontró la partida.");
        var assignments = await delivery.GetOrderAssignmentsAsync(order.Id, token);
        var committed = assignments.Where(x => x.OrderLineId == lineId && x.Id != excludeId).Sum(x => x.CommitmentQuantity);
        if (committed + additional > line.Quantity) Conflict("La cantidad supera el saldo sin asignar. Otra vuelta puede tenerlo reservado o cargado.");
    }
    private async Task<DeliveryCommandResponse> ExecuteAsync(string action, Guid operationId, object payload,
        AdminActorContext actor, Func<CancellationToken, Task<DeliveryCommandResponse>> mutation, CancellationToken token)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("La operación requiere una referencia de reintento.");
        var userId = ActorId(actor);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
        var result = await operations.ExecuteAsync($"DELIVERY:{userId:N}:{action}:{operationId:N}", hash, clock.Now, mutation, token);
        return result.Result with { Replayed = result.Replayed };
    }
    private async Task EventAsync(Guid tripId, Guid? assignmentId, string type, decimal? quantity, decimal? previous,
        string? reason, string? notes, AdminActorContext actor, CancellationToken token)
    {
        await delivery.AddEventAsync(new DeliveryEvent { Id = Guid.NewGuid(), TripId = tripId, AssignmentId = assignmentId,
            Type = type, Quantity = quantity, PreviousQuantity = previous, Reason = reason, Notes = notes,
            ActorId = actor.ActorId, ActorName = actor.ActorDisplayName, OccurredAt = clock.Now }, token);
        await AuditAsync((assignmentId ?? tripId).ToString(), "Delivery" + type, actor,
            new { tripId, assignmentId, quantity, previous }, token);
    }
    private Task AuditAsync(string id, string type, AdminActorContext actor, object metadata, CancellationToken token)
        => audit.AddAsync(AuditLog.Create("Delivery", id, type, clock.Now, AuditActorType.Admin,
            "Movimiento de reparto registrado.", actor.ActorId, actor.ActorDisplayName, JsonSerializer.Serialize(metadata)), token);
    private static DeliveryAssignment CloneForTransfer(DeliveryAssignment source, Guid targetTripId, decimal quantity)
        => new() { Id = Guid.NewGuid(), TripId = targetTripId, OrderId = source.OrderId, OrderLineId = source.OrderLineId,
            ProductId = source.ProductId, CustomerId = source.CustomerId, CustomerName = source.CustomerName,
            SequenceNumber = source.SequenceNumber, RouteStopOrder = source.RouteStopOrder, ProductName = source.ProductName,
            UnitCode = source.UnitCode, RequestedQuantity = source.RequestedQuantity, AssignedQuantity = quantity,
            TransferredInQuantity = quantity, AcknowledgedOrderVersion = source.AcknowledgedOrderVersion,
            MapsUrl = source.MapsUrl, Address = source.Address, RequestedDeliveryTime = source.RequestedDeliveryTime,
            RequestedDeliveryWindowStart = source.RequestedDeliveryWindowStart, RequestedDeliveryWindowEnd = source.RequestedDeliveryWindowEnd,
            HasStoreKey = source.HasStoreKey, PaymentTermCode = source.PaymentTermCode, DeliveryNotes = source.DeliveryNotes,
            InternalDeliveryInstructions = source.InternalDeliveryInstructions };
    private static bool Ready(Order order) => order.Status is OrderStatus.Submitted or OrderStatus.Accepted;
    private static bool SameRoute(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static Guid ActorId(AdminActorContext actor) => Guid.TryParse(actor.ActorId, out var id) && id != Guid.Empty ? id : throw new UnauthorizedAccessException("Se requiere una sesión interna válida.");
    private static string Required(string? value, int max, string error) => Optional(value, max) ?? throw new ArgumentException(error);
    private static string? Optional(string? value, int max = 2000)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean?.Length > max) throw new ArgumentException($"El texto admite hasta {max} caracteres.");
        return clean;
    }
    private static void Version(int actual, int expected) { if (actual != expected) Conflict("Los datos cambiaron. Recarga y revisa antes de confirmar."); }
    private static void Quantity(decimal value, bool allowZero)
    {
        if (value < 0 || (!allowZero && value == 0) || value > 999999999 || decimal.Round(value, 3) != value)
            throw new ArgumentException("Captura una cantidad válida con hasta tres decimales.");
    }
    private static void AtMost(decimal value, decimal available) { if (value > available) Conflict("La cantidad supera lo disponible en esta etapa. Solicita una corrección antes de continuar."); }
    private static void DifferenceReason(decimal actual, decimal expected, string? reason)
    { if (actual != expected && reason is null) throw new ArgumentException("Indica el motivo de la diferencia de cantidades."); }
    private static void Conflict(string message) => throw new CustomerOrderConflictException(message);
    private static string? ValidateMapsUrl(string? value)
    {
        var text = Optional(value, 2048); if (text is null) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Usa un enlace HTTPS válido de Google Maps.");
        var host = uri.IdnHost.ToLowerInvariant();
        var valid = host is "maps.app.goo.gl" or "maps.google.com" or "maps.google.com.mx" ||
            (host is "google.com" or "www.google.com" or "google.com.mx" or "www.google.com.mx" && (uri.AbsolutePath.Equals("/maps", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.StartsWith("/maps/", StringComparison.OrdinalIgnoreCase))) ||
            (host == "goo.gl" && uri.AbsolutePath.StartsWith("/maps/", StringComparison.OrdinalIgnoreCase));
        if (!valid) throw new ArgumentException("El enlace debe pertenecer a Google Maps.");
        return text;
    }
}
