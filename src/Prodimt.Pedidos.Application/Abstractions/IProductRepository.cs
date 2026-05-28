using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken);

    Task<Product?> GetByIdForUpdateAsync(Guid productId, CancellationToken cancellationToken);

    Task<Product?> GetActiveByExactNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetByIdsIncludingInactiveAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken);

    Task AddAsync(Product product, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
