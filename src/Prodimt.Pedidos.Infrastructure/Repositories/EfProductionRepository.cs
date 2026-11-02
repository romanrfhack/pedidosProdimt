using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfProductionRepository(PedidosDbContext db) : IProductionRepository
{
    public async Task<IReadOnlyList<ProductionBatch>> GetBatchesByDateAsync(DateOnly date, CancellationToken cancellationToken)
        => await db.ProductionBatches.AsNoTracking().Where(x => x.ServiceDate == date).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductionAllocation>> GetAllocationsByDateAsync(DateOnly date, CancellationToken cancellationToken)
        => await db.ProductionAllocations.AsNoTracking().Where(x => db.ProductionBatches
            .Any(batch => batch.Id == x.BatchId && batch.ServiceDate == date)).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductionAllocation>> GetAllocationsForOrderAsync(Guid orderId, CancellationToken cancellationToken)
        => await db.ProductionAllocations.AsNoTracking().Where(x => x.OrderId == orderId).ToArrayAsync(cancellationToken);

    public Task<ProductionBatch?> GetBatchByIdAsync(Guid id, CancellationToken cancellationToken)
        => db.ProductionBatches.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<ProductionAllocation?> GetAllocationByIdAsync(Guid id, CancellationToken cancellationToken)
        => db.ProductionAllocations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> HasActiveProductAllocationsAsync(Guid productId, CancellationToken cancellationToken)
        => db.ProductionAllocations.AnyAsync(x => x.ReversedAt == null && db.ProductionBatches
            .Any(batch => batch.Id == x.BatchId && batch.ProductId == productId), cancellationToken);

    public Task<bool> HasRecordedProductProductionAsync(Guid productId, CancellationToken cancellationToken)
        => db.ProductionBatches.AnyAsync(x => x.ProductId == productId, cancellationToken);

    public async Task AddBatchAsync(ProductionBatch batch, CancellationToken cancellationToken)
        => await db.ProductionBatches.AddAsync(batch, cancellationToken);

    public async Task AddAllocationAsync(ProductionAllocation allocation, CancellationToken cancellationToken)
        => await db.ProductionAllocations.AddAsync(allocation, cancellationToken);
}
