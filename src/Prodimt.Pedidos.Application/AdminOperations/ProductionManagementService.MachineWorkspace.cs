using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed partial class ProductionManagementService
{
    public async Task<MachineWorkspaceResponse> GetMachineWorkspaceAsync(Guid machineId, MachineWorkspaceAccess access,
        IMachineWorkspaceRepository workspace, AdminActorContext actor, CancellationToken token)
    {
        await access.RequireAsync(machineId, actor, token);
        var dashboard = await GetDashboardAsync(clock.Today, null, token);
        var machine = dashboard.Machines.Single(x => x.MachineId == machineId);
        var released = dashboard.Allocations.Where(x => x.ReversedAt is null).GroupBy(x => x.OrderLineId)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.ReleasedQuantity));
        var scoped = dashboard.Orders.Select(order => new MachineWorkspaceOrder(order.OrderId,
            order.CustomerId, order.CustomerName, order.SequenceNumber, order.RouteCode, order.RouteStopOrder,
            order.OrderVersion, order.Lines.Where(line => line.AssignedMachineId == machineId && line.UnitCode == ProductUnitCodes.Kilogram
                && line.Quantity > 0).Select(line => new MachineWorkspaceLine(line.OrderLineId, line.ProductId,
                line.ProductName, line.Quantity, line.AllocatedQuantity, line.PendingAllocationQuantity,
                released.GetValueOrDefault(line.OrderLineId))).ToArray())).Where(order => order.Lines.Count > 0).ToArray();
        var batches = dashboard.Batches.Where(batch => batch.MachineId == machineId)
            .Select(batch => new MachineWorkspaceBatch(batch.Id, batch.ProductId, batch.ProductName, batch.Quantity,
                batch.AllocatedQuantity, batch.AvailableQuantity + batch.HandedOffQuantity, batch.HandedOffQuantity,
                batch.CreatedAt)).ToArray();
        var catalog = (await products.GetAllAsync(token)).Where(CanProduce)
            .OrderBy(product => product.Name).Select(product => new MachineWorkspaceProduct(product.Id, product.Name)).ToArray();
        var notices = (await workspace.GetNoticesAsync(machineId, dashboard.Date, token))
            .Select(notice => new MachineWorkspaceNotice(notice.Id, notice.OrderId, notice.OrderLineId,
                notice.CustomerName, notice.ProductName, notice.Quantity, notice.CreatedAt)).ToArray();
        return new(dashboard.Date, new(machineId, machine.MachineNumber, machine.MachineName),
            new(machine.ProducedQuantity, machine.AllocatedQuantity,
                scoped.SelectMany(order => order.Lines).Sum(line => line.PendingQuantity),
                machine.ProducedQuantity - machine.AllocatedQuantity), scoped, batches, catalog, notices);
    }

    public Task<ProductionCommandResponse> CaptureForMachineAsync(Guid machineId, MachineCaptureRequest request,
        MachineWorkspaceAccess access, AdminActorContext actor, CancellationToken token)
    {
        ValidateQuantity(request.Quantity);
        return ExecuteMachineAsync("CAPTURE", machineId, request.OperationId, request, access, actor, async ct =>
        {
            var order = await RequireOrderAsync(request.OrderId, request.ExpectedOrderVersion, ct);
            var line = await RequireLineAsync(order, request.OrderLineId, ct);
            RequireMachineLine(machineId, order, line);
            var batch = await RegisterBatchAsync(new(request.OperationId, order.OrderDate, machineId, line.ProductId,
                request.Quantity), actor, ct);
            return await AllocateBatchAsync(batch, order, line, request.Quantity, actor, ct);
        }, token);
    }

    public Task<ProductionCommandResponse> RegisterMachineLotAsync(Guid machineId, MachineLotRequest request,
        MachineWorkspaceAccess access, AdminActorContext actor, CancellationToken token)
    {
        ValidateQuantity(request.Quantity);
        return ExecuteMachineAsync("LOT", machineId, request.OperationId, request, access, actor, async ct =>
        {
            if (request.Date != clock.Today)
                throw new CustomerOrderConflictException("Cambió la fecha de servicio. Actualiza el panel antes de registrar kilos.");
            var batch = await RegisterBatchAsync(new(request.OperationId, request.Date, machineId, request.ProductId,
                request.Quantity), actor, ct);
            return new(batch.Id, null);
        }, token);
    }

    public Task<ProductionCommandResponse> AllocateMachineLotAsync(Guid machineId, AllocateProductionRequest request,
        MachineWorkspaceAccess access, AdminActorContext actor, CancellationToken token)
    {
        ValidateQuantity(request.Quantity);
        return ExecuteMachineAsync("ALLOCATE", machineId, request.OperationId, request, access, actor, async ct =>
        {
            var batch = await RequireMachineBatchAsync(machineId, request.BatchId, ct);
            var order = await RequireOrderAsync(request.OrderId, request.ExpectedOrderVersion, ct);
            var line = await RequireLineAsync(order, request.OrderLineId, ct);
            RequireMachineLine(machineId, order, line);
            return await AllocateBatchAsync(batch, order, line, request.Quantity, actor, ct);
        }, token);
    }

    public Task<ProductionCommandResponse> HandoffMachineLotAsync(Guid machineId, MachineHandoffRequest request,
        MachineWorkspaceAccess access, AdminActorContext actor, CancellationToken token)
    {
        ValidateQuantity(request.Quantity);
        return ExecuteMachineAsync("HANDOFF", machineId, request.OperationId, request, access, actor, async ct =>
        {
            var batch = await RequireMachineBatchAsync(machineId, request.BatchId, ct);
            var allocations = await production.GetAllocationsByDateAsync(batch.ServiceDate, ct);
            var allocated = allocations.Where(x => x.BatchId == batch.Id && x.ReversedAt is null).Sum(x => x.Quantity);
            var previous = await GetHandedOffAsync(batch, ct);
            if (request.Quantity > batch.Quantity - previous - allocated)
                throw new CustomerOrderConflictException("El lote no tiene suficientes kilos sin cliente en máquina.");
            var movement = new ProductionLotHandoff { Id = Guid.NewGuid(), BatchId = batch.Id,
                Quantity = request.Quantity, CreatedAt = clock.Now, CreatedBy = actor.ActorId };
            await production.AddHandoffAsync(movement, ct);
            await AuditAsync(movement.Id, "ProductionLotHandedOff", actor, "Lote sin cliente entregado a Embarques.",
                new { batchId = batch.Id, batch.ServiceDate, machineId, batch.ProductId, request.OperationId,
                    request.Quantity, previous, handedOffQuantity = previous + request.Quantity }, ct);
            return new(movement.Id, null);
        }, token);
    }

    public Task<ProductionCommandResponse> ReleaseMachineProductionAsync(Guid machineId, MachineReleaseRequest request,
        MachineWorkspaceAccess access, IMachineWorkspaceRepository workspace, AdminActorContext actor, CancellationToken token)
    {
        if (request.OrderLineId.HasValue != request.Quantity.HasValue)
            throw new ArgumentException("Indica partida y kilos para una liberación parcial.");
        if (request.Quantity is { } quantity) ValidateQuantity(quantity);
        return ExecuteMachineAsync("RELEASE", machineId, request.OperationId, request, access, actor, async ct =>
        {
            var order = await RequireOrderAsync(request.OrderId, request.ExpectedOrderVersion, ct);
            if (order.OrderDate != clock.Today) throw new CustomerOrderConflictException("La partida es de otra fecha de servicio.");
            var candidates = order.Lines.Where(line => line.AssignedMachineId == machineId
                && line.RequiresMachine && line.UnitCode == ProductUnitCodes.Kilogram && line.Quantity > 0).ToArray();
            var catalog = (await products.GetByIdsIncludingInactiveAsync(candidates.Select(x => x.ProductId).Distinct(), ct))
                .ToDictionary(product => product.Id);
            var lines = candidates.Where(line => catalog.GetValueOrDefault(line.ProductId) is not { RequiresMachine: false }
                && (request.OrderLineId is null || request.OrderLineId == line.Id)).ToArray();
            if (lines.Length == 0) throw new KeyNotFoundException("No hay partidas de producción para esta máquina y pedido.");
            var byLine = (await production.GetAllocationsForOrderAsync(order.Id, ct))
                .Where(allocation => allocation.ReversedAt is null).ToLookup(allocation => allocation.OrderLineId);
            if (request.OrderLineId is null && lines.Any(line => byLine[line.Id].Sum(a => a.Quantity) < line.Quantity))
                throw new CustomerOrderConflictException("Completa la producción pendiente de este pedido antes de marcarlo listo.");

            var customer = await CustomerNameAsync(order, ct);
            var notices = 0;
            foreach (var line in lines)
            {
                var allocations = byLine[line.Id].OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
                var available = allocations.Sum(x => x.Quantity - x.ReleasedQuantity);
                var toRelease = request.Quantity ?? available;
                if (toRelease == 0) continue;
                if (toRelease > available)
                    throw new CustomerOrderConflictException("La cantidad supera los kilos producidos pendientes de liberar.");
                var left = toRelease;
                var releasedByAllocation = new List<object>();
                foreach (var snapshot in allocations.Where(x => x.Quantity > x.ReleasedQuantity))
                {
                    if (left == 0) break;
                    var allocation = await production.GetAllocationByIdAsync(snapshot.Id, ct)
                        ?? throw new CustomerOrderConflictException("La asignación cambió. Actualiza el panel.");
                    var previous = allocation.ReleasedQuantity;
                    var added = Math.Min(left, allocation.Quantity - previous);
                    allocation.ReleasedQuantity += added;
                    releasedByAllocation.Add(new { allocationId = allocation.Id, allocation.BatchId, previous, allocation.ReleasedQuantity, added });
                    left -= added;
                }

                var notice = new MachineProductionNotice { Id = Guid.NewGuid(), MachineId = machineId,
                    ServiceDate = order.OrderDate, OrderId = order.Id, OrderLineId = line.Id, CustomerName = customer,
                    ProductName = line.ProductNameSnapshot ?? "Producto", Quantity = toRelease, CreatedAt = clock.Now };
                await workspace.AddNoticeAsync(notice, ct);
                await AuditAsync(notice.Id, "MachineProductionReleased", actor, "Producción liberada para Embarques.",
                    new { orderId = order.Id, lineId = line.Id, machineId, request.OperationId, quantity = toRelease,
                        releasedQuantity = allocations.Sum(x => x.ReleasedQuantity) + toRelease, releasedByAllocation }, ct);
                notices++;
            }
            if (notices == 0) throw new CustomerOrderConflictException("La partida ya está liberada.");
            return new(order.Id, order.Version);
        }, token);
    }

    private async Task<ProductionCommandResponse> ExecuteMachineAsync(string action, Guid machineId, Guid operationId,
        object payload, MachineWorkspaceAccess access, AdminActorContext actor,
        Func<CancellationToken, Task<ProductionCommandResponse>> command, CancellationToken token)
    {
        await access.RequireAsync(machineId, actor, token);
        return await ExecuteAsync("WORKSPACE-" + action, operationId, new { machineId, payload }, actor, async ct =>
        {
            await access.RequireAsync(machineId, actor, ct);
            return await command(ct);
        }, token);
    }

    private void RequireMachineLine(Guid machineId, Order order, OrderLine line)
    {
        if (line.AssignedMachineId != machineId) throw new UnauthorizedAccessException();
        if (order.OrderDate != clock.Today) throw new CustomerOrderConflictException("La partida es de otra fecha de servicio.");
    }

    private async Task<decimal> GetHandedOffAsync(ProductionBatch batch, CancellationToken token)
        => (await production.GetHandoffsByDateAsync(batch.ServiceDate, token))
            .Where(x => x.BatchId == batch.Id).Sum(x => x.Quantity);

    private async Task<ProductionBatch> RequireMachineBatchAsync(Guid machineId, Guid batchId, CancellationToken token)
    {
        var batch = await RequireBatchAsync(batchId, token);
        if (batch.MachineId != machineId) throw new UnauthorizedAccessException();
        if (batch.ServiceDate != clock.Today) throw new CustomerOrderConflictException("El lote es de otra fecha de servicio.");
        return batch;
    }
}
