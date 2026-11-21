using System.Security.Cryptography;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed partial class ProductionManagementService(IProductionRepository production, IOrderRepository orders,
    IProductRepository products, IMachineRepository machines, ICustomerRepository customers,
    IOrderOperationStore operations, IAuditLogRepository audit, IDateTimeProvider clock,
    OrderChangeNoticePublisher? changeNotices = null, IMachineWorkspaceRepository? machineWorkspace = null)
{
    public async Task<ProductionDashboardResponse> GetDashboardAsync(DateOnly? date, string? routeCode,
        CancellationToken cancellationToken)
    {
        var serviceDate = date ?? clock.Today;
        var route = string.IsNullOrWhiteSpace(routeCode) ? null : routeCode.Trim();
        var allOrders = (await orders.GetByDateAsync(serviceDate, cancellationToken))
            .Where(IsReady).ToArray();
        var allProducts = (await products.GetByIdsIncludingInactiveAsync(
            allOrders.SelectMany(x => x.Lines).Select(x => x.ProductId), cancellationToken)).ToDictionary(x => x.Id);
        bool Required(OrderLine line) => line.RequiresMachine &&
            (!allProducts.TryGetValue(line.ProductId, out var product) || product.RequiresMachine);
        var allMachines = await machines.GetAllAsync(cancellationToken);
        var machineLookup = allMachines.ToDictionary(x => x.Id);
        var customerLookup = (await customers.GetByIdsAsync(allOrders.Select(x => x.CustomerId).OfType<Guid>(), cancellationToken))
            .ToDictionary(x => x.Id);
        string Name(Order order) => order.CustomerNameSnapshot ??
            (order.CustomerId is { } id && customerLookup.TryGetValue(id, out var customer) ? customer.Name : "Mostrador");
        var batches = await production.GetBatchesByDateAsync(serviceDate, cancellationToken);
        var allocations = await production.GetAllocationsByDateAsync(serviceDate, cancellationToken);
        var active = allocations.Where(x => x.ReversedAt is null).ToArray();
        var handoffs = (await production.GetHandoffsByDateAsync(serviceDate, cancellationToken))
            .GroupBy(x => x.BatchId).ToDictionary(group => group.Key, group => group.Sum(x => x.Quantity));
        decimal HandedOff(Guid batchId) => handoffs.GetValueOrDefault(batchId);
        decimal Allocated(Guid batchId) => active.Where(x => x.BatchId == batchId).Sum(x => x.Quantity);
        decimal ForLine(Guid lineId) => active.Where(x => x.OrderLineId == lineId).Sum(x => x.Quantity);
        var requiredLines = allOrders.SelectMany(x => x.Lines).Where(Required).ToArray();
        var scopedOrders = allOrders.Where(x => route is null || string.Equals(route, x.RouteCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.RouteCode).ThenBy(x => x.RouteStopOrder ?? int.MaxValue).ThenBy(Name).ThenBy(x => x.SequenceNumber)
            .Select(order => new ProductionOrderResponse(order.Id, order.CustomerId, Name(order), order.SequenceNumber,
                order.RouteCode, order.RouteStopOrder, order.Version, order.InternalDeliveryInstructions,
                order.RequestedDeliveryTime, order.RequestedDeliveryWindowStart, order.RequestedDeliveryWindowEnd,
                order.Lines.Where(Required).Select(line =>
                {
                    var machine = line.AssignedMachineId is { } id && machineLookup.TryGetValue(id, out var found) ? found : null;
                    var allocated = ForLine(line.Id);
                    return new ProductionLineResponse(line.Id, line.ProductId, line.ProductNameSnapshot ?? "Producto",
                        line.UnitCode, line.Quantity, line.AssignedMachineId, machine?.Number, machine?.Name,
                        allocated, Math.Max(line.Quantity - allocated, 0), line.Notes);
                }).ToArray())).Where(x => x.Lines.Count > 0).ToArray();
        var visibleOrderIds = scopedOrders.Select(x => x.OrderId).ToHashSet();
        var summaries = allMachines.Where(x => x.IsActive || requiredLines.Any(l => l.AssignedMachineId == x.Id) || batches.Any(b => b.MachineId == x.Id))
            .OrderBy(x => x.Number).Select(machine =>
            {
                var demand = requiredLines.Where(x => x.AssignedMachineId == machine.Id && x.UnitCode == ProductUnitCodes.Kilogram).ToArray();
                var output = batches.Where(x => x.MachineId == machine.Id).ToArray();
                var pending = demand.GroupBy(x => x.ProductId).Sum(group =>
                    Math.Max(group.Sum(x => x.Quantity) - output.Where(x => x.ProductId == group.Key).Sum(x => x.Quantity), 0));
                var produced = output.Sum(x => x.Quantity);
                var assigned = output.Sum(x => Allocated(x.Id));
                return new ProductionMachineResponse(machine.Id, machine.Number, machine.Name ?? $"Máquina {machine.Number}", demand.Sum(x => x.Quantity),
                    produced, pending, assigned, produced - assigned - output.Sum(x => HandedOff(x.Id)), machine.IsActive);
            }).ToArray();
        var releases = machineWorkspace is null ? [] : (await machineWorkspace.GetDayNoticesAsync(serviceDate, cancellationToken))
            .Where(notice => route is null || visibleOrderIds.Contains(notice.OrderId))
            .Select(notice => new MachineProductionReleaseResponse(notice.Id, notice.MachineId,
                machineLookup.GetValueOrDefault(notice.MachineId)?.Number ?? 0,
                machineLookup.GetValueOrDefault(notice.MachineId)?.Name ?? "Máquina",
                notice.OrderId, notice.OrderLineId, notice.CustomerName, notice.ProductName,
                notice.Quantity, notice.CreatedAt)).ToArray();
        return new(serviceDate, route, "all-routes", summaries,
            batches.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(batch =>
            {
                machineLookup.TryGetValue(batch.MachineId, out var machine);
                var allocated = Allocated(batch.Id);
                return new ProductionBatchResponse(batch.Id, batch.ServiceDate, batch.MachineId, machine?.Number,
                    machine?.Name, batch.ProductId, batch.ProductNameSnapshot, batch.UnitCode, batch.Quantity,
                    allocated, batch.Quantity - allocated - HandedOff(batch.Id), batch.CreatedAt, HandedOff(batch.Id));
            }).ToArray(), scopedOrders,
            allocations.Where(x => route is null || visibleOrderIds.Contains(x.OrderId))
                .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new ProductionAllocationResponse(
                    x.Id, x.BatchId, x.OrderId, x.OrderLineId, x.CustomerNameSnapshot, x.Quantity, x.CreatedAt,
                    x.ReversedAt, x.ReverseReason, x.ReleasedQuantity)).ToArray(), releases);
    }

    public Task<ProductionCommandResponse> RegisterAsync(RegisterProductionRequest request, AdminActorContext actor,
        CancellationToken cancellationToken)
    {
        ValidateQuantity(request.Quantity);
        if (request.Date == default || request.MachineId == Guid.Empty || request.ProductId == Guid.Empty)
            throw new ArgumentException("Indica fecha, máquina y producto.");
        if (request.Notes?.Length > 500) throw new ArgumentException("Las notas admiten hasta 500 caracteres.");
        return ExecuteAsync("BATCH", request.OperationId, request, actor, async token =>
        {
            var batch = await RegisterBatchAsync(request, actor, token);
            return new(batch.Id, null);
        }, cancellationToken);
    }

    private async Task<ProductionBatch> RegisterBatchAsync(RegisterProductionRequest request, AdminActorContext actor,
        CancellationToken token)
    {
        await RequireMachineAsync(request.MachineId, token);
        var product = await products.GetByIdAsync(request.ProductId, token)
            ?? throw new KeyNotFoundException("No se encontró el producto.");
        if (!CanProduce(product))
            throw new ArgumentException("Selecciona un producto activo que requiera máquina y tenga unidad kg.");
        var batch = new ProductionBatch
        {
            Id = Guid.NewGuid(), ServiceDate = request.Date, MachineId = request.MachineId,
            ProductId = product.Id, ProductNameSnapshot = product.Name, Quantity = request.Quantity,
            UnitCode = ProductUnitCodes.Kilogram, CreatedAt = clock.Now, CreatedBy = actor.ActorId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        await production.AddBatchAsync(batch, token);
        await AuditAsync(batch.Id, "ProductionBatchRegistered", actor, "Producción física registrada.",
            new { batch.ServiceDate, batch.MachineId, batch.ProductId, batch.Quantity, batch.UnitCode }, token);
        return batch;
    }

    private static bool CanProduce(Product product) => product.IsActive && product.RequiresMachine
        && product.MergedIntoProductId is null && product.UnitCode == ProductUnitCodes.Kilogram;

    public Task<ProductionCommandResponse> AllocateAsync(AllocateProductionRequest request, AdminActorContext actor,
        CancellationToken cancellationToken)
    {
        ValidateQuantity(request.Quantity);
        return ExecuteAsync("ALLOCATE", request.OperationId, request, actor, async token =>
        {
            var batch = await RequireBatchAsync(request.BatchId, token);
            var order = await RequireOrderAsync(request.OrderId, request.ExpectedOrderVersion, token);
            var line = await RequireLineAsync(order, request.OrderLineId, token);
            return await AllocateBatchAsync(batch, order, line, request.Quantity, actor, token);
        }, cancellationToken);
    }

    private async Task<ProductionBatch> RequireBatchAsync(Guid batchId, CancellationToken token)
        => await production.GetBatchByIdAsync(batchId, token)
            ?? throw new KeyNotFoundException("No se encontró el lote producido.");

    private async Task<ProductionCommandResponse> AllocateBatchAsync(ProductionBatch batch, Order order, OrderLine line,
        decimal quantity, AdminActorContext actor, CancellationToken token)
    {
        if (order.OrderDate != batch.ServiceDate || line.ProductId != batch.ProductId ||
            line.UnitCode != batch.UnitCode || line.AssignedMachineId != batch.MachineId)
            throw new ArgumentException("El lote y el pedido deben compartir fecha, máquina, producto y unidad.");
        var dateAllocations = await production.GetAllocationsByDateAsync(batch.ServiceDate, token);
        var active = dateAllocations.Where(x => x.ReversedAt is null).ToArray();
        var handedOff = await GetHandedOffAsync(batch, token);
        if (quantity > batch.Quantity - handedOff - active.Where(x => x.BatchId == batch.Id).Sum(x => x.Quantity))
            throw new CustomerOrderConflictException("El lote no tiene suficientes kilos disponibles en máquina. Actualiza el panel.");
        if (quantity > line.Quantity - active.Where(x => x.OrderLineId == line.Id).Sum(x => x.Quantity))
            throw new CustomerOrderConflictException("La cantidad supera los kilos pendientes de asignar al pedido.");
        var allocation = new ProductionAllocation
        {
            Id = Guid.NewGuid(), BatchId = batch.Id, OrderId = order.Id, OrderLineId = line.Id,
            Quantity = quantity, CustomerNameSnapshot = await CustomerNameAsync(order, token),
            CreatedAt = clock.Now, CreatedBy = actor.ActorId
        };
        await production.AddAllocationAsync(allocation, token);
        order.Version++;
        await AuditAsync(allocation.Id, "ProductionAllocated", actor, "Kilos producidos asignados al pedido.",
            new { allocation.BatchId, allocation.OrderId, allocation.OrderLineId, allocation.Quantity }, token);
        return new(allocation.Id, order.Version);
    }

    public Task<ProductionCommandResponse> ReverseAsync(Guid allocationId, ReverseProductionAllocationRequest request,
        AdminActorContext actor, CancellationToken cancellationToken)
    {
        ValidateReason(request.Reason);
        return ExecuteAsync("REVERSE", request.OperationId, new { allocationId, request }, actor, async token =>
        {
            var allocation = await production.GetAllocationByIdAsync(allocationId, token)
                ?? throw new KeyNotFoundException("No se encontró la asignación de producción.");
            if (allocation.ReleasedQuantity > 0)
                throw new CustomerOrderConflictException("La asignación ya tiene kilos liberados para Embarques.");
            if (allocation.ReversedAt is not null)
                throw new CustomerOrderConflictException("Esta asignación ya fue revertida. Actualiza el panel.");
            var order = await orders.GetByIdAsync(allocation.OrderId, token)
                ?? throw new KeyNotFoundException("No se encontró el pedido.");
            allocation.ReversedAt = clock.Now;
            allocation.ReversedBy = actor.ActorId;
            allocation.ReverseReason = request.Reason.Trim();
            order.Version++;
            await AuditAsync(allocation.Id, "ProductionAllocationReversed", actor,
                "Asignación revertida explícitamente; los kilos vuelven al lote.",
                new { allocation.OrderId, allocation.OrderLineId, allocation.Quantity, allocation.ReverseReason }, token);
            return new(allocation.Id, order.Version);
        }, cancellationToken);
    }

    public Task<ProductionCommandResponse> AssignMachineAsync(Guid orderId, Guid lineId,
        AssignProductionMachineRequest request, AdminActorContext actor, CancellationToken cancellationToken)
    {
        ValidateReason(request.Reason);
        return ExecuteAsync("MACHINE", request.OperationId, new { orderId, lineId, request }, actor, async token =>
        {
            var order = await RequireOrderAsync(orderId, request.ExpectedOrderVersion, token);
            var line = await RequireLineAsync(order, lineId, token, requireKilograms: false);
            var previousDemand = OrderChangeNoticePublisher.Capture(order);
            if ((await production.GetAllocationsForOrderAsync(orderId, token)).Any(x => x.OrderLineId == lineId && x.ReversedAt is null))
                throw new CustomerOrderConflictException("Revierte explícitamente los kilos asignados antes de cambiar la máquina.");
            if (request.MachineId is { } machineId) await RequireMachineAsync(machineId, token);
            var previousMachineId = line.AssignedMachineId;
            line.AssignedMachineId = request.MachineId;
            order.Version++;
            if (changeNotices is not null)
                await changeNotices.PublishAsync(order, previousDemand, "machine-assignment", request.Reason.Trim(),
                    actor, clock.Now, token);
            await AuditAsync(order.Id, "ProductionMachineAssigned", actor, "Máquina del pedido actualizada desde producción.",
                new { orderId, lineId, previousMachineId, request.MachineId, reason = request.Reason.Trim() }, token);
            return new(line.Id, order.Version);
        }, cancellationToken);
    }

    private async Task<ProductionCommandResponse> ExecuteAsync(string action, Guid operationId, object payload,
        AdminActorContext actor, Func<CancellationToken, Task<ProductionCommandResponse>> command, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Indica una clave de operación válida.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { payload, actor.ActorId })));
        var receipt = await operations.ExecuteAsync($"PRODUCTION:{action}:{operationId:N}", hash, clock.Now, command, cancellationToken);
        return receipt.Result with { Replayed = receipt.Replayed };
    }

    private async Task<Order> RequireOrderAsync(Guid orderId, int expectedVersion, CancellationToken token)
    {
        var order = await orders.GetByIdAsync(orderId, token) ?? throw new KeyNotFoundException("No se encontró el pedido.");
        if (expectedVersion < 1 || order.Version != expectedVersion || !IsReady(order))
            throw new CustomerOrderConflictException("El pedido cambió o ya no está autorizado para producción. Actualiza el panel.");
        return order;
    }

    private async Task<OrderLine> RequireLineAsync(Order order, Guid lineId, CancellationToken token, bool requireKilograms = true)
    {
        var line = order.Lines.SingleOrDefault(x => x.Id == lineId)
            ?? throw new KeyNotFoundException("No se encontró el renglón del pedido.");
        var product = (await products.GetByIdsIncludingInactiveAsync([line.ProductId], token)).SingleOrDefault();
        if (!line.RequiresMachine || product is { RequiresMachine: false })
            throw new ArgumentException("El producto no requiere máquina y no participa en producción.");
        if (requireKilograms && line.UnitCode != ProductUnitCodes.Kilogram)
            throw new ArgumentException("El registro y asignación de producción de este módulo se realizan en kg.");
        return line;
    }

    private async Task RequireMachineAsync(Guid machineId, CancellationToken token)
    {
        var machine = await machines.GetByIdAsync(machineId, token);
        if (machine is null || !machine.IsActive) throw new ArgumentException("Selecciona una máquina activa.");
    }

    private async Task<string> CustomerNameAsync(Order order, CancellationToken token)
    {
        if (order.CustomerNameSnapshot is not null) return order.CustomerNameSnapshot;
        if (order.CustomerId is { } id && await customers.GetByIdAsync(id, token) is { } customer)
            return customer.Name;
        return "Mostrador";
    }

    private Task AuditAsync(Guid id, string eventType, AdminActorContext actor, string summary, object metadata, CancellationToken token)
        => audit.AddAsync(AuditLog.Create("Production", id.ToString(), eventType, clock.Now, AuditActorType.Admin,
            summary, actor.ActorId, actor.ActorDisplayName, JsonSerializer.Serialize(metadata)), token);
    private static bool IsReady(Order order) => order.Status is OrderStatus.Submitted or OrderStatus.Accepted;
    private static void ValidateQuantity(decimal quantity)
    {
        if (quantity <= 0 || quantity > 9999999999999999.99m || decimal.Round(quantity, 2) != quantity)
            throw new ArgumentException("Indica kilos positivos con hasta dos decimales.");
    }
    private static void ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("Indica un motivo de hasta 500 caracteres.");
    }
}
