using System.Security.Cryptography;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Collections;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.Delivery;

/// <summary>Delivery quantities are a separate ledger; they never replace the customer's order.</summary>
public sealed class DeliveryManagementService(IDeliveryRepository delivery, IOrderRepository orders,
    ICustomerRepository customers, IAdminUserRepository users, IOrderOperationStore operations,
    IAuditLogRepository audit, IDateTimeProvider clock, CollectionService? collections = null)
{
    public async Task<DeliveryDashboardResponse> DashboardAsync(DateOnly? date, string? routeCode,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        var userId = ActorId(actor);
        var serviceDate = date ?? clock.Today;
        var route = Optional(routeCode);
        var dateTrips = await delivery.GetTripsAsync(serviceDate, token);
        var trips = dateTrips
            .Where(x => (manage || x.DriverUserId == userId) && (route is null || SameRoute(x.RouteCode, route))).ToArray();
        var operationalDate = IsOperationalDate(serviceDate);
        var dayDrivers = manage || operationalDate ? await delivery.GetDayDriversAsync(serviceDate, token) : [];
        var savedRoutes = manage || operationalDate ? await delivery.GetRoutesAsync(token) : [];
        var driverUsers = manage || operationalDate ? await delivery.GetDriversAsync(token) : [];
        var routes = manage ? savedRoutes.Select(x =>
        {
            var dayDriver = FindDayDriver(dayDrivers, x.Code);
            return new DeliveryRouteResponse(x.Code, x.Name, x.DefaultDriverUserId, x.IsActive, x.Version,
                dayDriver?.DriverUserId, dayDriver?.Version ?? 0);
        }).ToArray() : [];
        var drivers = manage ? driverUsers.Select(x => new DeliveryDriverResponse(x.Id, x.DisplayName, x.UserName)).ToArray() : [];
        var readyOrders = (await orders.GetByDateAsync(serviceDate, token))
            .Where(x => Ready(x) && x.CustomerId is not null && (route is null || SameRoute(x.RouteCode, route)))
            .OrderBy(x => x.RouteCode).ThenBy(x => x.RouteStopOrder ?? int.MaxValue).ThenBy(x => x.SequenceNumber).ToArray();
        var tripResponses = new List<DeliveryTripResponse>();
        foreach (var trip in trips) tripResponses.Add(await MapTripAsync(trip, token, readyOrders));
        var assignedOrderIds = tripResponses.SelectMany(x => x.Assignments).Select(x => x.OrderId).ToHashSet();
        var orderResponses = new List<DeliveryOrderResponse>();
        foreach (var order in readyOrders.Where(x => manage || assignedOrderIds.Contains(x.Id)))
        {
            var assignments = await delivery.GetOrderAssignmentsAsync(order.Id, token);
            orderResponses.Add(await MapOrderAsync(order, order.Lines, assignments, token));
        }
        var canPrepare = false;
        if (operationalDate)
        {
            var missing = EligibleFirstTrips(dateTrips, savedRoutes, dayDrivers, driverUsers, manage ? null : userId)
                .Select(x => x.Route.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            canPrepare = missing.Count != 0 && (await RoutesWithPlannedOrdersAsync(readyOrders.Where(x =>
                x.RouteCode is not null && missing.Contains(x.RouteCode.Trim())), token)).Count != 0;
        }
        return new(serviceDate, routes, drivers, tripResponses, orderResponses, canPrepare, operationalDate);
    }

    public async Task<DeliveryTripResponse> TripAsync(Guid id, AdminActorContext actor, bool manage, CancellationToken token)
        => await MapTripAsync(await RequireTripAsync(id, actor, manage, false, token), token);

    public async Task PrepareForOrderAsync(Guid orderId, CancellationToken token)
    {
        var key = $"DELIVERY:AUTO-TRIP:{orderId:N}";
        try
        {
            await operations.ExecuteAsync(key, key, clock.Now, async ct =>
            {
                var order = await orders.GetByIdAsync(orderId, ct);
                if (order is null || !IsOperationalDate(order.OrderDate))
                    throw new NoTripToPrepareException();
                var routeCodes = await RoutesWithPlannedOrdersAsync([order], ct);
                var trip = await PrepareTripsAsync(order.OrderDate, routeCodes, null, null, ct);
                // Do not save a no-op receipt: the route or driver can be fixed later.
                return trip ?? throw new NoTripToPrepareException();
            }, token);
        }
        catch (NoTripToPrepareException)
        {
        }
    }

    public Task<DeliveryCommandResponse> PrepareAsync(PrepareDeliveryRequest request, AdminActorContext actor,
        bool manage, CancellationToken token)
    {
        RequireOperationalDate(request.Date);
        var driverScope = manage ? (Guid?)null : ActorId(actor);
        return ExecuteAsync("prepare", request.OperationId, request, actor, async ct =>
        {
            var dateOrders = await orders.GetByDateAsync(request.Date, ct);
            var routeCodes = await RoutesWithPlannedOrdersAsync(dateOrders, ct);
            return await PrepareTripsAsync(request.Date, routeCodes, driverScope, actor, ct) ?? new(Guid.Empty);
        }, token);
    }

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

    public Task<DeliveryCommandResponse> SaveDayDriverAsync(SaveDeliveryDayDriverRequest request, AdminActorContext actor,
        CancellationToken token)
    {
        RequireOperationalDate(request.Date);
        var code = Required(request.RouteCode, 80, "Selecciona una ruta.").ToUpperInvariant();
        var reason = Required(request.Reason, 500, "Indica el motivo del cambio de repartidor.");
        return ExecuteAsync("day-driver", request.OperationId, request, actor, async ct =>
        {
            var route = await delivery.GetRouteAsync(code, ct) ?? throw new KeyNotFoundException("No se encontró la ruta.");
            if (!route.IsActive) throw new ArgumentException("La ruta está inactiva.");
            var dayDriver = FindDayDriver(await delivery.GetDayDriversAsync(request.Date, ct), code);
            Version(dayDriver?.Version ?? 0, request.ExpectedVersion);
            var trip = (await delivery.GetTripsAsync(request.Date, ct)).SingleOrDefault(x => x.TripNumber == 1 && SameRoute(x.RouteCode, code));
            if (trip?.Version != request.ExpectedTripVersion)
                Conflict("La primera vuelta cambió. Recarga y revisa el repartidor.");

            var effectiveId = request.DriverUserId ?? route.DefaultDriverUserId;
            var effectiveDriver = effectiveId is { } id ? await RequireDriverAsync(id, ct) : null;
            if (trip is not null && trip.DriverUserId != effectiveId)
            {
                if (effectiveDriver is null) throw new ArgumentException("Configura un repartidor habitual o elige un sustituto.");
                var assignments = await delivery.GetAssignmentsAsync(trip.Id, ct);
                if (!CanChangeDriver(trip, assignments))
                    Conflict("La vuelta ya inició su operación. Conserva su repartidor y usa otra vuelta para el relevo.");
                var previousId = trip.DriverUserId;
                var previousName = trip.DriverName;
                trip.DriverUserId = effectiveDriver.Id;
                trip.DriverName = effectiveDriver.DisplayName;
                trip.Version++;
                await EventAsync(trip.Id, null, "TripDriverChanged", null, null, reason,
                    $"Repartidor anterior: {previousName}. Nuevo repartidor: {trip.DriverName}.", actor, ct);
                await AuditAsync(trip.Id.ToString(), "DeliveryTripDriverReassigned", actor,
                    new { previousDriverUserId = previousId, newDriverUserId = trip.DriverUserId, reason }, ct);
            }

            var previousOverride = dayDriver?.DriverUserId;
            if (dayDriver is null)
            {
                dayDriver = new DeliveryRouteDayDriver { Date = request.Date, RouteCode = code, DriverUserId = request.DriverUserId };
                await delivery.AddDayDriverAsync(dayDriver, ct);
            }
            else
            {
                dayDriver.DriverUserId = request.DriverUserId;
                dayDriver.Version++;
            }
            await AuditAsync(code, "DeliveryDayDriverSaved", actor, new
            {
                request.Date, routeCode = code, previousDriverUserId = previousOverride, dayDriver.DriverUserId,
                effectiveDriverUserId = effectiveId, dayDriver.Version, reason
            }, ct);
            return new(trip?.Id ?? Guid.Empty, dayDriver.Version);
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
            var dayDriver = FindDayDriver(await delivery.GetDayDriversAsync(request.Date, ct), code);
            var usual = dayDriver?.DriverUserId ?? route.DefaultDriverUserId;
            var driverId = request.DriverUserId ?? usual ?? throw new ArgumentException("Asigna un repartidor a la vuelta.");
            var driver = await RequireDriverAsync(driverId, ct);
            if (usual is not null && driverId != usual && reason is null)
                throw new ArgumentException("Indica el motivo del repartidor sustituto.");
            if ((await delivery.GetTripsAsync(request.Date, ct)).Any(x => SameRoute(x.RouteCode, code) && x.TripNumber == request.TripNumber))
                Conflict("Esta ruta ya tiene esa vuelta para la fecha. Actualiza el listado.");
            var trip = await AddTripAsync(request.Date, request.TripNumber, route, driver, reason, actor, ct);
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
            if (trip.DepartedAt is not null) Conflict("La vuelta ya salió. Asigna el saldo a otra vuelta.");
            var order = await RequireOrderAsync(request.OrderId, request.ExpectedOrderVersion, ct);
            var line = RequireTripOrderLine(trip, order, request.OrderLineId);
            await EnsureCapacityAsync(order, line.Id, request.Quantity, null, ct, trip.TripNumber == 1 ? trip.Id : null);
            var item = await AddAssignmentAsync(trip, order, line, request.Quantity, actor, ct);
            return new(item.Id, item.Version);
        }, token);
    }

    public async Task<DeliveryCommandResponse> CountPlannedAsync(Guid tripId, CountPlannedDeliveryLineRequest request,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        Quantity(request.Quantity, true);
        var reason = Optional(request.Reason, 500);
        var notes = Optional(request.Notes, 2000);
        if (!manage) await RequireTripAsync(tripId, actor, false, false, token);
        return await ExecuteAsync("planned-count", request.OperationId, new { tripId, request }, actor, async ct =>
        {
            var trip = await RequireTripAsync(tripId, actor, manage, true, ct);
            Version(trip.Version, request.ExpectedTripVersion);
            var tripAssignments = await delivery.GetAssignmentsAsync(trip.Id, ct);
            var events = await delivery.GetEventsAsync(trip.Id, ct);
            if (!CanPlan(trip, tripAssignments, events)) Conflict("La primera vuelta ya no admite conteos de pedidos planeados.");
            var order = await RequireOrderAsync(request.OrderId, request.ExpectedOrderVersion, ct);
            var line = RequireTripOrderLine(trip, order, request.OrderLineId);
            var orderAssignments = await delivery.GetOrderAssignmentsAsync(order.Id, ct);
            if (orderAssignments.Any(x => x.OrderLineId == line.Id))
                Conflict("La partida ya fue asignada. Recarga para consultar el registro o el saldo disponible.");

            Quantity(line.Quantity, false);
            AtMost(request.Quantity, line.Quantity);
            DifferenceReason(request.Quantity, line.Quantity, reason);
            var item = await AddAssignmentAsync(trip, order, line, line.Quantity, actor, ct);
            await RecordCountAsync(item, request.Quantity, line.Quantity, reason, notes, actor, ct);
            return new(item.Id, item.Version);
        }, token);
    }

    public async Task<DeliveryCommandResponse> DepartAsync(Guid tripId, DepartDeliveryTripRequest request,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        if (!manage) await RequireTripAsync(tripId, actor, false, false, token);
        return await ExecuteAsync("depart", request.OperationId, new { tripId, request }, actor, async ct =>
        {
            var trip = await RequireTripAsync(tripId, actor, manage, true, ct);
            Version(trip.Version, request.ExpectedVersion);
            if (trip.DepartedAt is not null) Conflict("La salida de la vuelta ya está registrada.");
            RequireOperationalDate(trip.Date);
            if (trip.Date != DeliveryDatePolicy.LocalDate(clock.Now))
                throw new ArgumentException("Registra la salida en la fecha de entrega de la vuelta.");
            var assignments = await delivery.GetAssignmentsAsync(trip.Id, ct);
            if (assignments.Any(x => x.CommitmentQuantity > x.DeliveredQuantity + x.CustodyQuantity))
                Conflict("Confirma la carga o libera las cantidades reservadas antes de registrar la salida.");
            trip.DepartedAt = clock.Now;
            trip.Version++;
            await EventAsync(trip.Id, null, "TripDeparted", null, null, null, null, actor, ct);
            return new(trip.Id, trip.Version);
        }, token);
    }

    public async Task<DeliveryCommandResponse> RecordAsync(Guid id, string stage, RecordDeliveryStageRequest request,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        Quantity(request.Quantity, true);
        var reason = Optional(request.Reason, 500); var notes = Optional(request.Notes, 2000);
        if (stage is not ("count" or "load" or "deliver" or "return")) throw new ArgumentException("La etapa de reparto no es válida.");
        if (stage != "deliver" && (request.CollectedAmount is not null || request.CollectionVersion is not null))
            throw new ArgumentException("El cobro se registra al entregar el pedido.");
        if (request.CollectedAmount is not null && collections is null)
            throw new NotSupportedException("La cobranza requiere persistencia transaccional.");
        // Authorize before reading a durable receipt too: a former manager must not
        // replay an operation for another driver's trip through the own endpoint.
        if (!manage)
        {
            var scoped = await delivery.GetAssignmentAsync(id, token) ?? throw new KeyNotFoundException("No se encontró la asignación.");
            await RequireTripAsync(scoped.TripId, actor, false, false, token);
        }
        // Preserve the hash of durable receipts written before collection fields existed.
        object payload = request.CollectedAmount is null && request.CollectionVersion is null
            ? new { id, request = (object)new { request.OperationId, request.ExpectedVersion, request.ExpectedOrderVersion,
                request.Quantity, request.Reason, request.Notes, request.RemainderDisposition, request.AcknowledgeOrderChange } }
            : new { id, request = (object)request };
        return await ExecuteAsync(stage, request.OperationId, payload, actor, async ct =>
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
                await RecordCountAsync(item, request.Quantity, previous, reason, notes, actor, ct);
                trip.Version++;
                return new(item.Id, item.Version);
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
            var movement = await EventAsync(item.TripId, item.Id, stage switch { "load" => "Loaded", "deliver" => "Delivered", _ => "Returned" },
                request.Quantity, previous, reason, notes, actor, ct);
            if (stage == "deliver" && collections is not null)
                await collections.RecordDeliveryAsync(item, trip, order, line!, movement.Id, request.Quantity,
                    request.CollectedAmount, request.CollectionVersion, actor, manage, ct);
            if (stage == "load" && previous != request.Quantity)
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
            var movement = await EventAsync(item.TripId, item.Id, request.Stage switch { "count" => "CountAdjusted", "load" => "LoadAdjusted", _ => "DeliveredAdjusted" }, request.Quantity,
                previous, reason, "Corrección autorizada; el movimiento anterior permanece en bitácora.", actor, ct);
            if (request.Stage == "deliver" && collections is not null)
                await collections.RecordDeliveryAdjustmentAsync(item, trip, order,
                    order.Lines.Single(x => x.Id == item.OrderLineId), movement.Id,
                    request.Quantity - previous, reason, actor, ct);
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

    private async Task<DeliveryTripResponse> MapTripAsync(DeliveryTrip trip, CancellationToken token,
        IReadOnlyList<Order>? dateOrders = null)
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
        var planned = new List<DeliveryOrderResponse>();
        if (CanPlan(trip, assignments, events))
        {
            var routeOrders = (dateOrders ?? await orders.GetByDateAsync(trip.Date, token))
                .Where(order => CanPlanOrder(order) && SameRoute(trip.RouteCode, order.RouteCode))
                .OrderBy(order => order.RouteStopOrder ?? int.MaxValue).ThenBy(order => order.SequenceNumber);
            foreach (var order in routeOrders)
            {
                var history = await delivery.GetOrderAssignmentsAsync(order.Id, token);
                var lines = UnassignedPlanLines(order, history);
                if (lines.Length != 0) planned.Add(await MapOrderAsync(order, lines, history, token));
            }
        }
        return new(trip.Id, trip.Date, trip.RouteCode, trip.RouteName, trip.TripNumber, trip.DriverUserId,
            trip.DriverName, trip.Status, trip.Version, trip.CreatedAt, trip.ClosedAt, mapped,
            events.Select(e => new DeliveryEventResponse(e.Id, e.AssignmentId,
                e.Type, e.Quantity, e.PreviousQuantity, e.Reason, e.Notes, e.ActorName, e.OccurredAt)).ToArray(),
            trip.DepartedAt, planned, CanChangeDriver(trip, assignments));
    }

    private async Task<DeliveryOrderResponse> MapOrderAsync(Order order, IEnumerable<OrderLine> lines,
        IReadOnlyList<DeliveryAssignment> assignments, CancellationToken token)
    {
        var customerId = order.CustomerId!.Value;
        var customer = order.CustomerNameSnapshot is null ? await customers.GetByIdAsync(customerId, token) : null;
        var location = await delivery.GetLocationAsync(customerId, token);
        return new(order.Id, customerId, order.CustomerNameSnapshot ?? customer?.Name ?? "Cliente",
            order.SequenceNumber, order.RouteCode, order.RouteStopOrder, order.Version, location?.MapsUrl, location?.Address,
            order.RequestedDeliveryTime, order.RequestedDeliveryWindowStart, order.RequestedDeliveryWindowEnd,
            order.HasStoreKey, order.PaymentTermCode, order.DeliveryNotes, order.InternalDeliveryInstructions,
            location?.Version ?? 0, lines.Select(line =>
            {
                var same = assignments.Where(x => x.OrderLineId == line.Id).ToArray();
                var delivered = same.Sum(x => x.DeliveredQuantity);
                var commitment = same.Sum(x => x.CommitmentQuantity);
                return new DeliveryOrderLineResponse(line.Id, line.ProductId, line.ProductNameSnapshot ?? "Producto",
                    line.UnitCode, line.Quantity, delivered, Math.Max(commitment - delivered, 0), Math.Max(line.Quantity - commitment, 0));
            }).ToArray());
    }

    private async Task<HashSet<string>> RoutesWithPlannedOrdersAsync(IEnumerable<Order> candidates, CancellationToken token)
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in candidates.Where(CanPlanOrder))
        {
            var code = order.RouteCode!.Trim();
            if (routes.Contains(code)) continue;
            var history = await delivery.GetOrderAssignmentsAsync(order.Id, token);
            if (UnassignedPlanLines(order, history).Length != 0) routes.Add(code);
        }
        return routes;
    }

    private async Task<DeliveryCommandResponse?> PrepareTripsAsync(DateOnly date, HashSet<string> routeCodes, Guid? driverScope,
        AdminActorContext? actor, CancellationToken token)
    {
        if (routeCodes.Count == 0) return null;
        var dateTrips = await delivery.GetTripsAsync(date, token);
        var dayDrivers = await delivery.GetDayDriversAsync(date, token);
        var drivers = await delivery.GetDriversAsync(token);
        var routes = (await delivery.GetRoutesAsync(token)).Where(x => routeCodes.Contains(x.Code));
        DeliveryCommandResponse? firstCreated = null;
        foreach (var (route, driver) in EligibleFirstTrips(dateTrips, routes, dayDrivers, drivers, driverScope))
        {
            var trip = await AddTripAsync(date, 1, route, driver, "Primera vuelta preparada para pedidos registrados.", actor, token);
            firstCreated ??= new(trip.Id, trip.Version);
        }
        return firstCreated;
    }

    private static IEnumerable<(DeliveryRoute Route, AdminUser Driver)> EligibleFirstTrips(IReadOnlyList<DeliveryTrip> dateTrips,
        IEnumerable<DeliveryRoute> routes, IReadOnlyList<DeliveryRouteDayDriver> dayDrivers, IReadOnlyList<AdminUser> drivers, Guid? driverScope)
    {
        var existingFirstTrips = dateTrips.Where(x => x.TripNumber == 1).Select(x => x.RouteCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var driversById = drivers.ToDictionary(x => x.Id);
        foreach (var route in routes)
        {
            if (!route.IsActive || existingFirstTrips.Contains(route.Code)) continue;
            var driverId = FindDayDriver(dayDrivers, route.Code)?.DriverUserId ?? route.DefaultDriverUserId;
            if (driverId is { } id && (driverScope is null || driverScope == id) && driversById.TryGetValue(id, out var driver))
                yield return (route, driver);
        }
    }

    private async Task<DeliveryTrip> AddTripAsync(DateOnly date, int number, DeliveryRoute route, AdminUser driver,
        string? reason, AdminActorContext? actor, CancellationToken token)
    {
        var trip = new DeliveryTrip
        {
            Id = Guid.NewGuid(), Date = date, RouteCode = route.Code, RouteName = route.Name, TripNumber = number,
            DriverUserId = driver.Id, DriverName = driver.DisplayName, CreatedAt = clock.Now
        };
        await delivery.AddTripAsync(trip, token);
        await EventAsync(trip.Id, null, "TripCreated", null, null, reason, null, actor, token);
        return trip;
    }

    private static OrderLine RequireTripOrderLine(DeliveryTrip trip, Order order, Guid lineId)
    {
        if (order.CustomerId is null || !SameRoute(trip.RouteCode, order.RouteCode) || order.OrderDate != trip.Date)
            throw new ArgumentException("El pedido debe corresponder a la ruta y fecha de la vuelta.");
        var line = order.Lines.SingleOrDefault(x => x.Id == lineId) ?? throw new KeyNotFoundException("No se encontró la partida.");
        if (line.UnitCode is null || !ProductUnitCodes.IsSupported(line.UnitCode))
            throw new ArgumentException("Confirma la unidad del producto antes de asignarlo al reparto.");
        return line;
    }

    private async Task<DeliveryAssignment> AddAssignmentAsync(DeliveryTrip trip, Order order, OrderLine line,
        decimal quantity, AdminActorContext actor, CancellationToken token)
    {
        var customerId = order.CustomerId!.Value;
        var customer = order.CustomerNameSnapshot is null ? await customers.GetByIdAsync(customerId, token) : null;
        var location = await delivery.GetLocationAsync(customerId, token);
        var item = new DeliveryAssignment
        {
            Id = Guid.NewGuid(), TripId = trip.Id, OrderId = order.Id, OrderLineId = line.Id,
            ProductId = line.ProductId, CustomerId = customerId, CustomerName = order.CustomerNameSnapshot ?? customer?.Name ?? "Cliente",
            SequenceNumber = order.SequenceNumber, RouteStopOrder = order.RouteStopOrder, ProductName = line.ProductNameSnapshot ?? "Producto",
            UnitCode = line.UnitCode!, RequestedQuantity = line.Quantity, AssignedQuantity = quantity,
            AcknowledgedOrderVersion = order.Version, MapsUrl = location?.MapsUrl, Address = location?.Address,
            RequestedDeliveryTime = order.RequestedDeliveryTime, RequestedDeliveryWindowStart = order.RequestedDeliveryWindowStart,
            RequestedDeliveryWindowEnd = order.RequestedDeliveryWindowEnd, HasStoreKey = order.HasStoreKey,
            PaymentTermCode = order.PaymentTermCode, DeliveryNotes = order.DeliveryNotes, InternalDeliveryInstructions = order.InternalDeliveryInstructions
        };
        await delivery.AddAssignmentAsync(item, token);
        trip.Version++;
        await EventAsync(trip.Id, item.Id, "Assigned", quantity, null, null, null, actor, token);
        return item;
    }

    private async Task RecordCountAsync(DeliveryAssignment item, decimal quantity, decimal previous,
        string? reason, string? notes, AdminActorContext actor, CancellationToken token)
    {
        item.CountedQuantity = quantity;
        item.ReleasedQuantity += previous - quantity;
        item.Version++;
        await EventAsync(item.TripId, item.Id, "Counted", quantity, previous, reason, notes, actor, token);
        if (quantity != previous)
            await EventAsync(item.TripId, item.Id, "UnloadedReservationReleased", previous - quantity, null,
                reason, "Saldo no cargado disponible para otra vuelta.", actor, token);
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
    private async Task EnsureCapacityAsync(Order order, Guid lineId, decimal additional, Guid? excludeId, CancellationToken token,
        Guid? firstTripId = null)
    {
        var line = order.Lines.SingleOrDefault(x => x.Id == lineId) ?? throw new KeyNotFoundException("No se encontró la partida.");
        var assignments = (await delivery.GetOrderAssignmentsAsync(order.Id, token))
            .Where(x => x.OrderLineId == lineId && x.Id != excludeId).ToArray();
        if (firstTripId is { } tripId && assignments.Any(x => x.TripId == tripId && x.CountedQuantity is not null && x.ReleasedQuantity > 0))
            Conflict("El saldo de esta partida quedó disponible para otra vuelta.");
        var committed = assignments.Sum(x => x.CommitmentQuantity);
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
    private async Task<DeliveryEvent> EventAsync(Guid tripId, Guid? assignmentId, string type, decimal? quantity, decimal? previous,
        string? reason, string? notes, AdminActorContext? actor, CancellationToken token)
    {
        var movement = new DeliveryEvent { Id = Guid.NewGuid(), TripId = tripId, AssignmentId = assignmentId,
            Type = type, Quantity = quantity, PreviousQuantity = previous, Reason = reason, Notes = notes,
            ActorId = actor?.ActorId, ActorName = actor is null ? "Sistema" : actor.ActorDisplayName, OccurredAt = clock.Now };
        await delivery.AddEventAsync(movement, token);
        await AuditAsync((assignmentId ?? tripId).ToString(), "Delivery" + type, actor,
            new { tripId, assignmentId, quantity, previous }, token);
        return movement;
    }
    private Task AuditAsync(string id, string type, AdminActorContext? actor, object metadata, CancellationToken token)
        => audit.AddAsync(AuditLog.Create("Delivery", id, type, clock.Now, actor is null ? AuditActorType.System : AuditActorType.Admin,
            "Movimiento de reparto registrado.", actor?.ActorId, actor?.ActorDisplayName, JsonSerializer.Serialize(metadata)), token);
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
    private static bool CanPlanOrder(Order order) => Ready(order) && order.CustomerId is not null && !string.IsNullOrWhiteSpace(order.RouteCode);
    private static OrderLine[] UnassignedPlanLines(Order order, IReadOnlyList<DeliveryAssignment> history)
        => order.Lines.Where(line => line.Quantity is > 0 and <= 999999999 && decimal.Round(line.Quantity, 3) == line.Quantity &&
            line.UnitCode is not null && ProductUnitCodes.IsSupported(line.UnitCode) && history.All(x => x.OrderLineId != line.Id)).ToArray();
    private bool CanPlan(DeliveryTrip trip, IReadOnlyList<DeliveryAssignment> assignments, IReadOnlyList<DeliveryEvent>? events = null)
        => trip.TripNumber == 1 && trip.Status == "Open" && trip.DepartedAt is null && IsOperationalDate(trip.Date) &&
            assignments.All(x => x.DeliveredQuantity == 0 && x.ReturnedQuantity == 0 && x.TransferredInQuantity == 0 &&
                x.TransferredOutQuantity == 0 && !x.HasUnresolvedIncident) &&
            (events is null || events.All(x => x.Type != "Delivered"));
    private bool CanChangeDriver(DeliveryTrip trip, IReadOnlyList<DeliveryAssignment> assignments)
        => CanPlan(trip, assignments) && assignments.All(x => x.CountedQuantity is null && x.LoadedQuantity is null);
    private static DeliveryRouteDayDriver? FindDayDriver(IReadOnlyList<DeliveryRouteDayDriver> dayDrivers, string routeCode)
        => dayDrivers.FirstOrDefault(x => SameRoute(x.RouteCode, routeCode));
    private bool IsOperationalDate(DateOnly date)
    {
        var now = clock.Now;
        return date == DeliveryDatePolicy.LocalDate(now) || date == DeliveryDatePolicy.GetDate(now);
    }
    private void RequireOperationalDate(DateOnly date)
    {
        if (!IsOperationalDate(date)) throw new ArgumentException("Selecciona la fecha de hoy o la fecha de entrega vigente.");
    }
    private sealed class NoTripToPrepareException : Exception { }
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
