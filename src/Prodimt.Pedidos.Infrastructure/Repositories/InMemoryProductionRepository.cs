using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

/// <summary>The demo supports reads; IOrderOperationStore rejects durable writes without SQL Server.</summary>
public sealed class InMemoryProductionRepository(InMemoryDataStore store) : IProductionRepository
{
    public Task<IReadOnlyList<ProductionBatch>> GetBatchesByDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) return Task.FromResult<IReadOnlyList<ProductionBatch>>(store.ProductionBatches.Where(x => x.ServiceDate == date).ToArray());
    }
    public Task<IReadOnlyList<ProductionAllocation>> GetAllocationsByDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            var ids = store.ProductionBatches.Where(x => x.ServiceDate == date).Select(x => x.Id).ToHashSet();
            return Task.FromResult<IReadOnlyList<ProductionAllocation>>(store.ProductionAllocations.Where(x => ids.Contains(x.BatchId)).ToArray());
        }
    }
    public Task<IReadOnlyList<ProductionAllocation>> GetAllocationsForOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) return Task.FromResult<IReadOnlyList<ProductionAllocation>>(store.ProductionAllocations.Where(x => x.OrderId == orderId).ToArray());
    }
    public Task<ProductionBatch?> GetBatchByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) return Task.FromResult(store.ProductionBatches.SingleOrDefault(x => x.Id == id));
    }
    public Task<ProductionAllocation?> GetAllocationByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) return Task.FromResult(store.ProductionAllocations.SingleOrDefault(x => x.Id == id));
    }
    public Task<bool> HasActiveProductAllocationsAsync(Guid productId, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) return Task.FromResult(store.ProductionAllocations.Any(x => x.ReversedAt is null && store.ProductionBatches.Any(b => b.Id == x.BatchId && b.ProductId == productId)));
    }
    public Task<bool> HasRecordedProductProductionAsync(Guid productId, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) return Task.FromResult(store.ProductionBatches.Any(x => x.ProductId == productId));
    }
    public Task<IReadOnlyList<ProductionLotHandoff>> GetHandoffsByDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            var ids = store.ProductionBatches.Where(batch => batch.ServiceDate == date).Select(batch => batch.Id).ToHashSet();
            return Task.FromResult<IReadOnlyList<ProductionLotHandoff>>(store.ProductionLotHandoffs.Where(x => ids.Contains(x.BatchId)).ToArray());
        }
    }
    public Task AddHandoffAsync(ProductionLotHandoff handoff, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) store.ProductionLotHandoffs.Add(handoff);
        return Task.CompletedTask;
    }
    public Task AddBatchAsync(ProductionBatch batch, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) store.ProductionBatches.Add(batch);
        return Task.CompletedTask;
    }
    public Task AddAllocationAsync(ProductionAllocation allocation, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) store.ProductionAllocations.Add(allocation);
        return Task.CompletedTask;
    }
}
