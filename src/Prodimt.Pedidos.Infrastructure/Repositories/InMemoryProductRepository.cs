using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryProductRepository(InMemoryDataStore store) : IProductRepository
{
    public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Product> products = store.Products
            .OrderBy(x => x.Name)
            .ToArray();
        return Task.FromResult(products);
    }

    public Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = store.Products.SingleOrDefault(x => x.Id == productId);
        return Task.FromResult(product);
    }

    public Task<Product?> GetByIdForUpdateAsync(Guid productId, CancellationToken cancellationToken)
    {
        return GetByIdAsync(productId, cancellationToken);
    }

    public Task<Product?> GetActiveByExactNameAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        var product = store.Products.SingleOrDefault(x => x.IsActive && x.Name == normalizedName);
        return Task.FromResult(product);
    }

    public Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.ToHashSet();
        IReadOnlyList<Product> products = store.Products.Where(x => ids.Contains(x.Id) && x.IsActive).ToArray();
        return Task.FromResult(products);
    }

    public Task<IReadOnlyList<Product>> GetByIdsIncludingInactiveAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var ids = productIds.ToHashSet();
        IReadOnlyList<Product> products = store.Products.Where(x => ids.Contains(x.Id)).ToArray();
        return Task.FromResult(products);
    }

    public Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            store.Products.Add(product);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
