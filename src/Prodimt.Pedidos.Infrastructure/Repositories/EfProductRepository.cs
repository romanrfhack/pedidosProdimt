using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfProductRepository(PedidosDbContext dbContext) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.ToArray();

        return await dbContext.Products
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id) && x.IsActive)
            .ToArrayAsync(cancellationToken);
    }
}
