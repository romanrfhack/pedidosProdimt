using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryProductRepository(InMemoryDataStore store) : IProductRepository
{
    public Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.ToHashSet();
        IReadOnlyList<Product> products = store.Products.Where(x => ids.Contains(x.Id) && x.IsActive).ToArray();
        return Task.FromResult(products);
    }
}
