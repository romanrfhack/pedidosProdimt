using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfProductRepository(PedidosDbContext dbContext) : IProductRepository
{
    public Task<T> ExecuteCatalogUpdateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => CatalogTransaction.ExecuteAsync(dbContext, action, cancellationToken);

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Products
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
    }

    public Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken)
    {
        return dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
    }

    public Task<Product?> GetByIdForUpdateAsync(Guid productId, CancellationToken cancellationToken)
    {
        return dbContext.Products
            .SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
    }

    public Task<Product?> GetActiveByExactNameAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();

        return dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IsActive && x.Name == normalizedName, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.ToArray();

        return await dbContext.Products
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id) && x.IsActive)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetByIdsIncludingInactiveAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToArray();

        return await dbContext.Products
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        await dbContext.Products.AddAsync(product, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (CatalogPersistenceConflicts.IsConcurrentChange(ex))
        {
            throw new CatalogConflictException("Un perfil relacionado cambio al guardar el producto. Recarga el catalogo antes de reintentar.");
        }
    }
}
