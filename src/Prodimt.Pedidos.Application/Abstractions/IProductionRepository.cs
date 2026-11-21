using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IProductionRepository
{
    Task<IReadOnlyList<ProductionBatch>> GetBatchesByDateAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionAllocation>> GetAllocationsByDateAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionAllocation>> GetAllocationsForOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<ProductionBatch?> GetBatchByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ProductionAllocation?> GetAllocationByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasActiveProductAllocationsAsync(Guid productId, CancellationToken cancellationToken);
    Task<bool> HasRecordedProductProductionAsync(Guid productId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionLotHandoff>> GetHandoffsByDateAsync(DateOnly date, CancellationToken cancellationToken);
    Task AddHandoffAsync(ProductionLotHandoff handoff, CancellationToken cancellationToken);
    Task AddBatchAsync(ProductionBatch batch, CancellationToken cancellationToken);
    Task AddAllocationAsync(ProductionAllocation allocation, CancellationToken cancellationToken);
}
