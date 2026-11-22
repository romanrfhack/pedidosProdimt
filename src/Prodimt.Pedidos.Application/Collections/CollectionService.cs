using System.Security.Cryptography;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.Collections;

public sealed class CollectionService(ICollectionRepository repository, IDeliveryRepository delivery,
    IOrderRepository orders, ICustomerRepository customers, IOrderOperationStore operations,
    IAuditLogRepository audit, IDateTimeProvider clock, ISecurityStore security)
{
    private const decimal MaximumAmount = 9999999999999999.99m;

    public async Task<CollectionPreviewResponse> PreviewAsync(Guid assignmentId, decimal quantity,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        ValidateQuantity(quantity, true);
        var (assignment, trip) = await RequireAssignmentAsync(assignmentId, actor, manage, token);
        if (quantity > assignment.CustodyQuantity)
            throw new ArgumentException("La cantidad supera el producto disponible en la camioneta.");
        return await BuildPreviewAsync(assignment, trip, quantity,
            await ReadCustomerAsync(assignment.CustomerId, token), token);
    }

    // The delivery operation owns the serializable transaction and durable retry.
    // Physical delivery confirms this customer's complete order for the service day.
    public async Task RecordDeliveryAsync(DeliveryAssignment assignment, DeliveryTrip trip, Order order,
        OrderLine line, Guid eventId, decimal delta, decimal? amount, string? collectionVersion,
        AdminActorContext actor, bool manage, CancellationToken token)
    {
        RequireIdentity(assignment, trip, order, line);
        await RequireTripAsync(trip.Id, actor, manage, token);
        ValidateQuantity(delta, true);
        if (amount is { } collected) ValidateAmount(collected, true);
        if (assignment.PaymentTermCode != "cash_on_delivery")
        {
            if (amount.HasValue)
                throw new ArgumentException("El cobro del repartidor solo está habilitado para pago contra entrega.");
            return;
        }
        var state = await ReadCustomerAsync(assignment.CustomerId, token);
        if (amount.HasValue)
        {
            if (!(await security.GetPermissionsAsync(ActorId(actor), token)).Contains("operations.collections.work"))
                throw new UnauthorizedAccessException("La cuenta no tiene permiso para registrar cobros.");
            var preview = await BuildPreviewAsync(assignment, trip, delta, state, token);
            if (preview.Version != collectionVersion)
                throw new CustomerOrderConflictException("La cuenta cambió. Revisa el importe de cobro antes de confirmar.");
        }
        if (delta > 0 || amount.HasValue)
            await ReconcileDayAsync(assignment, trip, eventId, actor, state, token);
        if (amount > 0)
            await AddPaymentAsync(assignment, trip, amount.Value, actor, state, token);
        if (amount == 0)
            await AuditAsync(assignment.Id, "CollectionNotPaid", actor, "Entrega registrada sin pago recibido.",
                new { assignment.CustomerId, assignment.TripId, eventId }, token);
    }

    // Financial debt follows the order. Correcting physical delivery never changes its charge.
    public Task RecordDeliveryAdjustmentAsync(DeliveryAssignment assignment, DeliveryTrip trip, Order order,
        OrderLine line, Guid eventId, decimal signedDelta, string reason, AdminActorContext actor, CancellationToken token)
    {
        RequireIdentity(assignment, trip, order, line);
        return Task.CompletedTask;
    }

    // Order capture/correction calls this inside its own transaction. A day starts
    // financial tracking only after an explicit collection/delivery operation.
    public async Task ReconcileOrderAsync(Order order, AdminActorContext? actor, CancellationToken token)
    {
        if (order.CustomerId is not { } customerId) return;
        var state = await ReadCustomerAsync(customerId, token);
        var context = state.Charges.FirstOrDefault(x => x.ServiceDate == order.OrderDate);
        if (context is null) return;
        await ReconcileOrderCoreAsync(order, context, null, actor, state, token);
    }

    private async Task ReconcileDayAsync(DeliveryAssignment assignment, DeliveryTrip trip, Guid eventId,
        AdminActorContext actor, CustomerState state, CancellationToken token)
    {
        var context = Context(assignment, trip);
        foreach (var order in (await orders.GetByDateAsync(trip.Date, token)).Where(x => x.CustomerId == assignment.CustomerId))
            await ReconcileOrderCoreAsync(order, context, eventId, actor, state, token);
    }

    private async Task ReconcileOrderCoreAsync(Order order, CollectionCharge context, Guid? eventId,
        AdminActorContext? actor, CustomerState state, CancellationToken token)
    {
        foreach (var charge in OrderChanges(order, context, eventId, actor, state))
        {
            if (state.Charges.Any(x => x.SourceKey == charge.SourceKey))
                throw new CustomerOrderConflictException("La revisión financiera del pedido cambió. Recarga la cuenta.");
            await repository.AddChargeAsync(charge, token);
            state.Charges.Add(charge);
            await AuditAsync(charge.Id, charge.IsCredit ? "CollectionOrderCredit" : "CollectionOrderCharge",
                actor ?? new(null, "Cliente"), "Movimiento financiero asociado al pedido completo del día.",
                new { charge.OrderId, charge.OrderLineId, charge.CustomerId, charge.RequestedQuantity,
                    charge.Quantity, charge.IsCredit, charge.UnitPrice, charge.Amount }, token);
        }
    }

    private IEnumerable<CollectionCharge> OrderChanges(Order order, CollectionCharge context, Guid? eventId,
        AdminActorContext? actor, CustomerState state)
    {
        var ready = order.Status is OrderStatus.Submitted or OrderStatus.Accepted;
        var previous = state.Charges.Where(x => x.OrderId == order.Id).ToArray();
        if (previous.Length == 0 && order.PaymentTermCode != "cash_on_delivery") yield break;
        foreach (var lineId in order.Lines.Select(x => x.Id).Union(previous.Select(x => x.OrderLineId)))
        {
            var line = order.Lines.SingleOrDefault(x => x.Id == lineId);
            var history = previous.Where(x => x.OrderLineId == lineId).ToArray();
            var original = history.FirstOrDefault();
            var currentQuantity = history.Sum(x => x.IsCredit ? -x.Quantity : x.Quantity);
            var target = ready ? line?.Quantity ?? 0 : 0;
            var delta = target - currentQuantity;
            if (delta == 0) continue;
            var price = history.Select(x => state.Price(x)).FirstOrDefault(x => x.HasValue)
                ?? (line?.CurrencyCodeSnapshot == "MXN" ? line.UnitPriceSnapshot : null);
            if (price is not null) ValidatePrice(price.Value);
            var accumulated = state.Total(history);
            var amount = price is not null && accumulated is not null
                ? ExtendedAmount(target, price.Value) - accumulated.Value : (decimal?)null;
            yield return new()
            {
                Id = Guid.NewGuid(), SourceKey = $"ORDERLINE:{lineId:N}:V{order.Version}",
                DeliveryEventId = eventId, AssignmentId = context.AssignmentId, TripId = context.TripId,
                OrderId = order.Id, OrderLineId = lineId, CustomerId = context.CustomerId,
                CustomerName = order.CustomerNameSnapshot ?? context.CustomerName,
                ProductId = line?.ProductId ?? original!.ProductId,
                ProductName = line?.ProductNameSnapshot ?? original?.ProductName ?? "Producto",
                UnitCode = line?.UnitCode ?? original?.UnitCode ?? "",
                RequestedQuantity = target, Quantity = Math.Abs(delta), IsCredit = delta < 0,
                UnitPrice = price, Amount = amount is null ? null : Math.Abs(amount.Value),
                ServiceDate = order.OrderDate, PaymentTermCode = order.PaymentTermCode,
                RouteCode = order.RouteCode ?? context.RouteCode, TripNumber = context.TripNumber,
                DriverUserId = context.DriverUserId, DriverName = context.DriverName,
                Sequence = history.Select(x => x.Sequence).DefaultIfEmpty(0).Max() + 1,
                CreatedAt = clock.Now, ActorId = actor?.ActorId, ActorName = actor?.ActorDisplayName
            };
        }
    }

    private static CollectionCharge Context(DeliveryAssignment assignment, DeliveryTrip trip) => new()
    {
        CustomerId = assignment.CustomerId, CustomerName = assignment.CustomerName, AssignmentId = assignment.Id,
        TripId = trip.Id, DriverUserId = trip.DriverUserId, DriverName = trip.DriverName,
        RouteCode = trip.RouteCode, TripNumber = trip.TripNumber, ServiceDate = trip.Date
    };

    public async Task ReconcilePendingPricesAsync(Order order, OrderLine line, AdminActorContext actor, CancellationToken token)
    {
        if (order.CustomerId is not { } customerId || line.UnitPriceSnapshot is not { } price ||
            line.CurrencyCodeSnapshot != "MXN") return;
        ValidatePrice(price);
        var state = await ReadCustomerAsync(customerId, token);
        var relevant = state.Charges.Where(x => x.OrderLineId == line.Id).OrderBy(x => x.Sequence).ToArray();
        decimal quantity = 0;
        decimal amount = 0;
        foreach (var charge in relevant)
        {
            quantity += charge.IsCredit ? -charge.Quantity : charge.Quantity;
            var recorded = state.Value(charge);
            if (recorded is null)
            {
                var difference = ExtendedAmount(quantity, price) - amount;
                if (charge.IsCredit != (difference < 0) && difference != 0)
                    throw new CustomerOrderConflictException("La valoración del precio requiere revisar los movimientos de la partida.");
                var valuation = new CollectionChargeValuation
                {
                    Id = Guid.NewGuid(), ChargeId = charge.Id, UnitPrice = price,
                    Amount = Math.Abs(difference), CreatedAt = clock.Now, ActorId = actor.ActorId, ActorName = actor.ActorDisplayName
                };
                await repository.AddValuationAsync(valuation, token);
                state.Valuations.Add(valuation);
                recorded = valuation.Amount;
            }
            amount += charge.IsCredit ? -recorded.Value : recorded.Value;
        }
        if (relevant.Length > 0)
            await AuditAsync(line.Id, "CollectionPendingPriceResolved", actor, "Valoración de entregas que tenían precio pendiente.",
                new { orderId = order.Id, lineId = line.Id, price }, token);
    }

    public async Task<CollectionTripResponse> TripAsync(Guid tripId, AdminActorContext actor,
        bool manage, CancellationToken token)
    {
        var trip = await RequireTripAsync(tripId, actor, manage, token);
        var assignments = await delivery.GetAssignmentsAsync(tripId, token);
        var rows = new List<CollectionTripCustomerResponse>();
        foreach (var group in assignments.GroupBy(x => x.CustomerId))
        {
            var state = await ReadCustomerAsync(group.Key, token);
            var throughDate = state.Through(trip.Date);
            var total = throughDate.Total(throughDate.Charges.Where(x => x.ServiceDate == trip.Date));
            var accountTotal = throughDate.Total(throughDate.Charges);
            var paid = state.Payments.Where(x => x.TripId == tripId).Sum(x => x.Amount);
            rows.Add(new(group.Key, group.First().CustomerName, total, paid,
                accountTotal is null ? null : Math.Max(accountTotal.Value - throughDate.Payments.Sum(x => x.Amount), 0), accountTotal is null));
        }
        var payments = await repository.GetTripPaymentsAsync(tripId, token);
        var accounted = (await repository.GetTripReceiptLinesAsync(tripId, token)).Sum(x => x.Amount);
        return new(tripId, rows, rows.Any(x => x.PricePending) ? null : rows.Sum(x => x.ChargeAmount ?? 0),
            payments.Sum(x => x.Amount), payments.Sum(x => x.Amount) - accounted, rows.Any(x => x.PricePending));
    }

    public async Task<CollectionCustomerAccountResponse> CustomerAccountAsync(Guid customerId, CancellationToken token)
    {
        var customer = await customers.GetByIdAsync(customerId, token) ?? throw new KeyNotFoundException("No se encontró el cliente.");
        var state = await ReadCustomerAsync(customerId, token);
        var total = state.Total(state.Charges);
        var paid = state.Payments.Sum(x => x.Amount);
        return new(customerId, customer.Name, state.Charges.OrderBy(x => x.ServiceDate).ThenBy(x => x.OrderLineId).ThenBy(x => x.Sequence)
            .Select(x => new CollectionAccountEntry(x.Id, x.ServiceDate, x.OrderId, x.OrderLineId, x.ProductName,
                x.UnitCode, x.RequestedQuantity, x.Quantity, x.IsCredit, state.Price(x), state.Value(x), x.RouteCode, x.TripNumber, x.DriverName, true)).ToArray(),
            state.Payments.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(Payment).ToArray(),
            total, paid, total is null ? null : Math.Max(total.Value - paid, 0),
            total is null ? null : Math.Max(paid - total.Value, 0), total is null);
    }

    public async Task<CollectionCashDashboardResponse> CashierDashboardAsync(DateOnly? date, string? routeCode, AdminActorContext actor, CancellationToken token)
    {
        var serviceDate = date ?? clock.Today;
        var route = string.IsNullOrWhiteSpace(routeCode) ? null : routeCode.Trim();
        var result = new List<CollectionCashTripResponse>();
        foreach (var trip in (await delivery.GetTripsAsync(serviceDate, token))
            .Where(x => route is null || string.Equals(x.RouteCode, route, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.RouteCode).ThenBy(x => x.TripNumber))
        {
            var row = await CashTripAsync(trip, token);
            if (trip.DepartedAt is not null || row.ExpectedAmount > 0 || row.Receipts.Count > 0)
                result.Add(row);
        }
        var customerRows = new List<CollectionCashCustomerResponse>();
        foreach (var customerId in await repository.GetCustomerIdsAsync(serviceDate, route, token))
        {
            var state = (await ReadCustomerAsync(customerId, token)).Through(serviceDate);
            var total = state.Total(state.Charges);
            var paid = state.Payments.Sum(x => x.Amount);
            var name = state.Charges.FirstOrDefault()?.CustomerName ?? state.Payments.FirstOrDefault()?.CustomerName ?? "Cliente";
            customerRows.Add(new(customerId, name, state.Total(state.Charges.Where(x => x.ServiceDate == serviceDate)),
                state.Payments.Where(x => x.ServiceDate == serviceDate).Sum(x => x.Amount),
                total is null ? null : Math.Max(total.Value - paid, 0), total is null ? null : Math.Max(paid - total.Value, 0),
                total is null));
        }
        var localStart = serviceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, DeliveryDatePolicy.BusinessTimeZone));
        var until = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), DeliveryDatePolicy.BusinessTimeZone));
        var receivedToday = await repository.GetReceivedBetweenAsync(from, until, token);
        var myReceived = receivedToday.Where(x => x.ReceiverUserId == ActorId(actor)).Sum(x => x.ReceivedAmount);
        return new(serviceDate, route, result, result.Sum(x => x.ExpectedAmount), result.Sum(x => x.ReceivedAmount),
            result.Sum(x => x.PendingAmount), result.Sum(x => x.Difference), myReceived,
            customerRows.OrderBy(x => x.CustomerName).ToArray());
    }

    public async Task<CollectionCommandResponse> ReceiveCashAsync(ReceiveCollectionCashRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        var receiverId = ActorId(actor);
        await RequireReceiveAsync(receiverId, token);
        ValidateAmount(request.ReceivedAmount, true);
        if (request.DeclaredAmount is { } declared) ValidateAmount(declared, false);
        var reason = OptionalReason(request.Reason);
        return await ExecuteAsync("RECEIVE", request.OperationId, request, actor, async ct =>
        {
            await RequireReceiveAsync(receiverId, ct);
            var trip = await delivery.GetTripAsync(request.TripId, ct) ?? throw new KeyNotFoundException("No se encontró la vuelta.");
            var current = await CashTripAsync(trip, ct);
            if (current.Version != request.ExpectedVersion)
                throw new CustomerOrderConflictException("El dinero pendiente cambió. Revisa el conteo de caja antes de confirmar.");
            var expected = request.DeclaredAmount ?? current.PendingAmount;
            if (expected <= 0 || expected > current.PendingAmount)
                throw new ArgumentException("El importe declarado debe estar dentro del dinero pendiente de entregar.");
            if (expected != request.ReceivedAmount && reason is null)
                throw new ArgumentException("Indica el motivo de la diferencia del conteo.");
            var receipt = new CollectionCashReceipt
            {
                Id = Guid.NewGuid(), TripId = trip.Id, ServiceDate = trip.Date, RouteCode = trip.RouteCode,
                TripNumber = trip.TripNumber, DriverUserId = trip.DriverUserId, DriverName = trip.DriverName,
                ExpectedAmount = expected, ReceivedAmount = request.ReceivedAmount, Reason = reason,
                ReceiverUserId = receiverId, ReceiverName = actor.ActorDisplayName ?? "Mostrador", CreatedAt = clock.Now
            };
            var payments = await repository.GetTripPaymentsAsync(trip.Id, ct);
            var applied = (await repository.GetTripReceiptLinesAsync(trip.Id, ct))
                .GroupBy(x => x.PaymentId).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
            if (payments.Any(x => x.DriverUserId != trip.DriverUserId))
                throw new CustomerOrderConflictException("La vuelta contiene cobros de otro chofer. Requiere conciliación administrativa.");
            await repository.AddReceiptAsync(receipt, ct);
            var remaining = expected;
            foreach (var payment in payments.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
            {
                var take = Math.Min(payment.Amount - applied.GetValueOrDefault(payment.Id), remaining);
                if (take <= 0) continue;
                await repository.AddReceiptLineAsync(new() { Id = Guid.NewGuid(), ReceiptId = receipt.Id,
                    PaymentId = payment.Id, Amount = take }, ct);
                remaining -= take;
            }
            if (remaining != 0) throw new CustomerOrderConflictException("No se pudo conciliar el dinero pendiente.");
            await AuditAsync(receipt.Id, "CollectionCashReceived", actor, "Dinero del repartidor recibido en Mostrador.",
                new { receipt.TripId, receipt.DriverUserId, receipt.ExpectedAmount, receipt.ReceivedAmount,
                    Difference = receipt.ExpectedAmount - receipt.ReceivedAmount, receipt.Reason }, ct);
            return new(receipt.Id);
        }, token);
    }

    private async Task<CollectionCashTripResponse> CashTripAsync(DeliveryTrip trip, CancellationToken token)
    {
        var payments = await repository.GetTripPaymentsAsync(trip.Id, token);
        var receipts = await repository.GetTripReceiptsAsync(trip.Id, token);
        var lines = await repository.GetTripReceiptLinesAsync(trip.Id, token);
        var paid = payments.Sum(x => x.Amount);
        var accounted = lines.Sum(x => x.Amount);
        return new(trip.Id, trip.Date, trip.RouteCode, trip.RouteName, trip.TripNumber, trip.DriverUserId,
            trip.DriverName, trip.DepartedAt, trip.Status, paid, accounted, receipts.Sum(x => x.ReceivedAmount),
            paid - accounted, receipts.Sum(x => x.ExpectedAmount - x.ReceivedAmount),
            Hash(new { trip.Id, trip.DriverUserId, Payments = payments.Select(x => x.Id).Order().ToArray(),
                Receipts = receipts.Select(x => x.Id).Order().ToArray() }),
            receipts.OrderByDescending(x => x.CreatedAt).Select(x => new CollectionReceiptResponse(x.Id, x.CreatedAt,
                x.ReceiverName, x.ExpectedAmount, x.ReceivedAmount, x.ExpectedAmount - x.ReceivedAmount, x.Reason)).ToArray());
    }

    private async Task<CollectionPreviewResponse> BuildPreviewAsync(DeliveryAssignment assignment, DeliveryTrip trip,
        decimal quantity, CustomerState state, CancellationToken token)
    {
        var dayOrders = (await orders.GetByDateAsync(trip.Date, token)).Where(x => x.CustomerId == assignment.CustomerId)
            .OrderBy(x => x.Id).ToArray();
        var eligible = state.Through(trip.Date);
        var projection = new CustomerState(eligible.Charges.ToList(), eligible.Valuations, eligible.Payments);
        foreach (var order in dayOrders)
            projection.Charges.AddRange(OrderChanges(order, Context(assignment, trip), null, null, projection));
        var ticket = projection.Charges.Where(x => x.ServiceDate == trip.Date).GroupBy(x => x.OrderLineId)
            .Select(group => new CollectionTicketLine(group.First().OrderId, group.Key, group.First().ProductName,
                group.First().UnitCode, group.Sum(x => x.IsCredit ? -x.Quantity : x.Quantity),
                group.Select(x => projection.Price(x)).FirstOrDefault(x => x.HasValue), projection.Total(group)))
            .Where(x => x.Quantity > 0).ToArray();
        var total = projection.Total(projection.Charges);
        var paid = eligible.Payments.Sum(x => x.Amount);
        var remaining = projection.DayBalances();
        var balance = total is null ? null : (decimal?)Math.Max(total.Value - paid, 0);
        var pending = total is null;
        return new(assignment.PaymentTermCode, assignment.CustomerId, assignment.CustomerName, ticket,
            ticket.Any(x => x.Amount is null) ? null : ticket.Sum(x => x.Amount ?? 0),
            pending ? null : remaining.Where(x => x.Key < trip.Date).Sum(x => x.Value),
            state.Payments.Where(x => x.TripId == trip.Id).Sum(x => x.Amount),
            pending || assignment.PaymentTermCode != "cash_on_delivery" ? null : balance,
            balance, Hash(new { assignment.Id, trip.DriverUserId, quantity,
                Orders = dayOrders.Select(x => new { x.Id, x.Version, x.Status,
                    Lines = x.Lines.OrderBy(y => y.Id).Select(y => new { y.Id, y.Quantity, y.UnitPriceSnapshot, y.CurrencyCodeSnapshot }).ToArray() }).ToArray(),
                Charges = state.Charges.Select(x => x.Id).Order().ToArray(),
                Valuations = state.Valuations.Select(x => x.Id).Order().ToArray(),
                Payments = state.Payments.Select(x => x.Id).Order().ToArray() }),
            "MXN", pending, balance, total is null ? null : Math.Max(paid - total.Value, 0));
    }

    private async Task AddPaymentAsync(DeliveryAssignment assignment, DeliveryTrip trip, decimal amount,
        AdminActorContext actor, CustomerState state, CancellationToken token)
    {
        var eligible = state.Through(trip.Date);
        var allocationPending = eligible.Total(eligible.Charges) is null;
        var balances = allocationPending ? new Dictionary<DateOnly, decimal>() : eligible.DayBalances();
        var remaining = amount;
        var applications = new List<CollectionDayApplication>();
        foreach (var date in balances.Keys.OrderBy(x => x == trip.Date ? 0 : 1).ThenBy(x => x))
        {
            var applied = Math.Min(balances[date], remaining);
            if (applied <= 0) continue;
            applications.Add(new(date, applied));
            remaining -= applied;
        }
        var payment = new CollectionPayment
        {
            Id = Guid.NewGuid(), CustomerId = assignment.CustomerId, CustomerName = assignment.CustomerName,
            TripId = trip.Id, AssignmentId = assignment.Id, DriverUserId = trip.DriverUserId, DriverName = trip.DriverName,
            ServiceDate = trip.Date, RouteCode = trip.RouteCode, TripNumber = trip.TripNumber,
            Amount = amount, AllocationsJson = JsonSerializer.Serialize(applications), CreditAmount = allocationPending ? null : remaining,
            CreatedAt = clock.Now, ActorId = actor.ActorId, ActorName = actor.ActorDisplayName
        };
        await repository.AddPaymentAsync(payment, token);
        await AuditAsync(payment.Id, "CollectionPaymentRegistered", actor, "Pago recibido del cliente por el repartidor.",
            new { payment.CustomerId, payment.TripId, payment.DriverUserId, payment.Amount }, token);
    }

    private async Task<CustomerState> ReadCustomerAsync(Guid customerId, CancellationToken token) => new(
        (await repository.GetCustomerChargesAsync(customerId, token)).ToList(),
        (await repository.GetCustomerValuationsAsync(customerId, token)).ToList(),
        await repository.GetCustomerPaymentsAsync(customerId, token));

    private async Task<(DeliveryAssignment, DeliveryTrip)> RequireAssignmentAsync(Guid id, AdminActorContext actor,
        bool manage, CancellationToken token)
    {
        var assignment = await delivery.GetAssignmentAsync(id, token) ?? throw new KeyNotFoundException("No se encontró la partida de reparto.");
        return (assignment, await RequireTripAsync(assignment.TripId, actor, manage, token));
    }

    private async Task<DeliveryTrip> RequireTripAsync(Guid id, AdminActorContext actor, bool manage, CancellationToken token)
    {
        var trip = await delivery.GetTripAsync(id, token) ?? throw new KeyNotFoundException("No se encontró la vuelta.");
        if (!manage && trip.DriverUserId != ActorId(actor)) throw new UnauthorizedAccessException("Esta vuelta está asignada a otro repartidor.");
        return trip;
    }

    private static void RequireIdentity(DeliveryAssignment assignment, DeliveryTrip trip, Order order, OrderLine line)
    {
        if (assignment.TripId != trip.Id || assignment.OrderId != order.Id || assignment.OrderLineId != line.Id ||
            assignment.CustomerId != order.CustomerId || assignment.ProductId != line.ProductId ||
            assignment.UnitCode != line.UnitCode)
            throw new CustomerOrderConflictException("La identidad de la entrega cambió. Revisa la partida.");
    }

    private async Task<CollectionCommandResponse> ExecuteAsync(string action, Guid operationId, object payload,
        AdminActorContext actor, Func<CancellationToken, Task<CollectionCommandResponse>> command, CancellationToken token)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Indica una referencia de operación válida.");
        var receipt = await operations.ExecuteAsync($"COLLECTION:{action}:{operationId:N}",
            Hash(new { payload, actor.ActorId }), clock.Now, command, token);
        return receipt.Result with { Replayed = receipt.Replayed };
    }

    private Task AuditAsync(Guid id, string kind, AdminActorContext actor, string summary, object metadata, CancellationToken token) =>
        audit.AddAsync(AuditLog.Create("Collection", id.ToString(), kind, clock.Now, AuditActorType.Admin,
            summary, actor.ActorId, actor.ActorDisplayName, JsonSerializer.Serialize(metadata)), token);

    private static CollectionPaymentResponse Payment(CollectionPayment item) =>
        new(item.Id, item.CustomerId, item.CustomerName, item.Amount, item.CreatedAt, item.DriverName,
            item.ServiceDate, item.TripId, item.RouteCode, item.TripNumber, item.ActorId, item.ActorName);
    private static Guid ActorId(AdminActorContext actor) => Guid.TryParse(actor.ActorId, out var id)
        ? id : throw new UnauthorizedAccessException("Se necesita una cuenta identificada.");
    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static decimal ExtendedAmount(decimal quantity, decimal price)
    {
        try
        {
            var amount = Money(quantity * price);
            ValidateAmount(amount, true);
            return amount;
        }
        catch (OverflowException) { throw new ArgumentException("El importe de la entrega excede el máximo permitido."); }
    }
    private static void ValidatePrice(decimal price)
    {
        if (price < 0 || price > 99999999999999.9999m || decimal.Round(price, 4) != price)
            throw new ArgumentException("El precio debe ser válido y tener hasta cuatro decimales.");
    }
    private async Task RequireReceiveAsync(Guid userId, CancellationToken token)
    {
        if (!(await security.GetPermissionsAsync(userId, token)).Contains("operations.collections.receive"))
            throw new UnauthorizedAccessException("La cuenta no tiene permiso para recibir dinero de reparto.");
    }
    private static void ValidateAmount(decimal amount, bool allowZero)
    {
        if (amount < 0 || !allowZero && amount == 0 || amount > MaximumAmount || Money(amount) != amount)
            throw new ArgumentException("Indica un importe válido con hasta dos decimales.");
    }
    private static void ValidateQuantity(decimal quantity, bool allowZero)
    {
        if (quantity < 0 || !allowZero && quantity == 0 || quantity > 999999999999999.999m || decimal.Round(quantity, 3) != quantity)
            throw new ArgumentException("Indica una cantidad válida con hasta tres decimales.");
    }
    private static string? OptionalReason(string? reason)
    {
        if (reason?.Trim().Length > 500) throw new ArgumentException("El motivo admite hasta 500 caracteres.");
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
    private static string Hash(object payload) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));

    private sealed record CollectionDayApplication(DateOnly Date, decimal Amount);

    private sealed record CustomerState(List<CollectionCharge> Charges, List<CollectionChargeValuation> Valuations,
        IReadOnlyList<CollectionPayment> Payments)
    {
        public decimal? Value(CollectionCharge charge) => charge.Amount ?? Valuations.SingleOrDefault(x => x.ChargeId == charge.Id)?.Amount;
        public decimal? Price(CollectionCharge charge) => charge.UnitPrice ?? Valuations.SingleOrDefault(x => x.ChargeId == charge.Id)?.UnitPrice;
        public CustomerState Through(DateOnly date) =>
            new(Charges.Where(x => x.ServiceDate <= date).ToList(), Valuations,
                Payments.Where(x => x.ServiceDate <= date).ToArray());

        public Dictionary<DateOnly, decimal> DayBalances()
        {
            var balances = Charges.GroupBy(x => x.ServiceDate).ToDictionary(x => x.Key, x => Math.Max(Total(x) ?? 0, 0));
            foreach (var payment in Payments.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
            {
                var remaining = payment.Amount;
                foreach (var date in balances.Keys.OrderBy(x => x == payment.ServiceDate ? 0 : 1).ThenBy(x => x))
                {
                    var take = Math.Min(balances[date], remaining);
                    balances[date] -= take;
                    remaining -= take;
                }
            }
            return balances;
        }

        public decimal? Total(IEnumerable<CollectionCharge> charges)
        {
            decimal total = 0;
            foreach (var group in charges.GroupBy(x => x.OrderLineId))
            {
                if (group.Any(x => Value(x) is null))
                {
                    // A fully cancelled unpriced line carries no remaining debt.
                    if (group.Sum(x => x.IsCredit ? -x.Quantity : x.Quantity) == 0) continue;
                    return null;
                }
                total += group.Sum(x => x.IsCredit ? -Value(x)!.Value : Value(x)!.Value);
            }
            return total;
        }
    }
}
