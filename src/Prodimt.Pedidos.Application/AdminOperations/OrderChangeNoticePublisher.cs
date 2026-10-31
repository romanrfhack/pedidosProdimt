using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed class OrderChangeNoticePublisher(IOrderChangeNoticeRepository notices, IMachineRepository machines,
    ICustomerRepository customers)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public sealed record DemandLine(Guid ProductId, string ProductName, string? UnitCode, decimal Quantity,
        Guid? MachineId, string? Notes);

    public static IReadOnlyList<DemandLine> Capture(Order order) => order.Status is OrderStatus.Submitted or OrderStatus.Accepted
        ? order.Lines.Select(line => new DemandLine(line.ProductId, line.ProductNameSnapshot ?? "Producto historico",
            line.UnitCode, line.Quantity, line.AssignedMachineId, line.Notes)).ToArray()
        : [];

    // Stages notices only. The caller must save them in the same transaction as the order change.
    public async Task PublishAsync(Order order, IReadOnlyList<DemandLine> before, string kind, string reason,
        AdminActorContext? actor, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var after = Capture(order);
        var keys = before.Concat(after).Select(line => (line.MachineId, line.ProductId, line.UnitCode)).Distinct().ToArray();
        var changes = keys.Select(key =>
        {
            var oldLine = before.SingleOrDefault(line => (line.MachineId, line.ProductId, line.UnitCode) == key);
            var newLine = after.SingleOrDefault(line => (line.MachineId, line.ProductId, line.UnitCode) == key);
            return (key.MachineId, Line: new OrderChangeLineResponse(key.ProductId,
                newLine?.ProductName ?? oldLine!.ProductName, key.UnitCode, oldLine?.Quantity ?? 0,
                newLine?.Quantity ?? 0, (newLine?.Quantity ?? 0) - (oldLine?.Quantity ?? 0), oldLine?.Notes, newLine?.Notes));
        }).Where(x => x.Line.DeltaQuantity != 0 || x.Line.BeforeNotes != x.Line.AfterNotes).ToArray();
        if (changes.Length == 0) return;

        var machineLookup = (await machines.GetByIdsAsync(changes.Select(x => x.MachineId).OfType<Guid>(), cancellationToken))
            .ToDictionary(x => x.Id);
        var customerName = order.CustomerNameSnapshot;
        if (customerName is null && order.CustomerId is { } customerId)
            customerName = (await customers.GetByIdAsync(customerId, cancellationToken))?.Name;
        foreach (var group in changes.GroupBy(x => x.MachineId))
        {
            var machine = group.Key is { } machineId && machineLookup.TryGetValue(machineId, out var found) ? found : null;
            await notices.AddAsync(new OrderChangeNotice
            {
                OrderId = order.Id, OrderVersion = order.Version, ServiceDate = order.OrderDate,
                CustomerId = order.CustomerId, CustomerName = customerName ?? "Mostrador", RouteCode = order.RouteCode,
                MachineId = group.Key, MachineNumber = machine?.Number, MachineName = machine?.Name,
                Kind = kind, Reason = reason, ActorId = actor?.ActorId, ActorDisplayName = actor?.ActorDisplayName,
                OccurredAt = occurredAt, LinesJson = JsonSerializer.Serialize(group.Select(x => x.Line)
                    .OrderBy(x => x.ProductName).ThenBy(x => x.ProductId).ToArray(), JsonOptions)
            }, cancellationToken);
        }
    }
}
