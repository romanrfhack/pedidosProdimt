using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed class AdminOperationsService(
    IOrderRepository orders,
    ICustomerRepository customers,
    IMachineRepository machines,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<OperationsPlanResponse> GetPlanAsync(DateOnly? date, string? routeCode, CancellationToken cancellationToken)
    {
        var serviceDate = date ?? dateTimeProvider.Today;
        var route = string.IsNullOrWhiteSpace(routeCode) ? null : routeCode.Trim();
        var sourceOrders = (await orders.GetByDateAsync(serviceDate, cancellationToken))
            .Where(x => route is null || string.Equals(x.RouteCode, route, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var customerLookup = (await customers.GetByIdsAsync(sourceOrders.Select(x => x.CustomerId).OfType<Guid>(), cancellationToken))
            .ToDictionary(x => x.Id);
        string Name(Order order) => order.CustomerNameSnapshot ??
            (order.CustomerId is { } id && customerLookup.TryGetValue(id, out var customer) ? customer.Name : "Mostrador");
        var ready = sourceOrders.Where(x => x.Status is OrderStatus.Submitted or OrderStatus.Accepted).ToArray();
        var machineLookup = (await machines.GetByIdsAsync(ready.SelectMany(x => x.Lines).Select(x => x.AssignedMachineId).OfType<Guid>(), cancellationToken))
            .ToDictionary(x => x.Id);
        var warnings = new List<OperationsPlanWarning>();
        var operationalOrders = ready.OrderBy(x => x.RouteCode).ThenBy(x => x.RouteStopOrder ?? int.MaxValue)
            .ThenBy(Name).ThenBy(x => x.SequenceNumber).Select(order =>
        {
            if (order.RouteCode is null)
                warnings.Add(new("ROUTE_UNKNOWN", "El pedido no tiene ruta capturada; revisa su destino.", order.Id, null));
            var lines = order.Lines.OrderBy(x => x.ProductNameSnapshot).Select(line =>
            {
                var machine = line.AssignedMachineId is { } id && machineLookup.TryGetValue(id, out var found) ? found : null;
                if (line.UnitCode is null || !ProductUnitCodes.IsSupported(line.UnitCode))
                    warnings.Add(new("UNIT_UNKNOWN", "La unidad no esta definida: esta linea se excluye del resumen de cantidades.", order.Id, line.Id));
                if (line.AssignedMachineId is null)
                    warnings.Add(new("MACHINE_UNASSIGNED", "La linea esta por asignar a una maquina o centro de preparacion.", order.Id, line.Id));
                else if (machine is null || !machine.IsActive)
                    warnings.Add(new("MACHINE_UNAVAILABLE", "La maquina de la linea ya no esta disponible; revisa la asignacion.", order.Id, line.Id));
                return new OperationsLineResponse(line.Id, line.ProductId,
                    line.ProductNameSnapshot ?? "Producto sin nombre historico", line.UnitCode, line.Quantity,
                    line.AssignedMachineId, machine?.Number, machine?.Name, line.Notes);
            }).ToArray();
            return new OperationsOrderResponse(order.Id, order.CustomerId, Name(order), order.SequenceNumber,
                order.Status, order.RouteCode, order.RouteStopOrder, order.HasStoreKey, order.PaymentTermCode,
                order.InternalDeliveryInstructions, order.RequestedDeliveryTime, order.RequestedDeliveryWindowStart,
                order.RequestedDeliveryWindowEnd, order.DeliveryNotes, order.OrderingProfileVersion, lines, order.Version);
        }).ToArray();

        var demand = operationalOrders.SelectMany(x => x.Lines)
            .Where(x => x.UnitCode is not null && ProductUnitCodes.IsSupported(x.UnitCode))
            .GroupBy(x => new { x.AssignedMachineId, x.ProductId, x.UnitCode, x.ProductName })
            .Select(group =>
            {
                var first = group.First();
                return new OperationsDemandResponse(first.AssignedMachineId, first.AssignedMachineNumber,
                    first.AssignedMachineName, first.ProductId, first.ProductName, first.UnitCode!, group.Sum(x => x.Quantity));
            })
            .OrderBy(x => x.MachineNumber ?? int.MaxValue).ThenBy(x => x.ProductName).ThenBy(x => x.UnitCode).ToArray();
        var pending = sourceOrders.Where(x => x.Status == OrderStatus.PendingAdminReview)
            .OrderBy(x => x.SubmittedAt)
            .Select(x => new OperationsPendingReviewResponse(x.Id, x.CustomerId, Name(x), x.SequenceNumber, x.AdminReviewReason))
            .ToArray();

        return new(serviceDate, route, "Por producir", "Por preparar", operationalOrders, demand, pending, warnings);
    }
}
