using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfCustomerAccessTokenRepository(PedidosDbContext dbContext) : ICustomerAccessTokenRepository
{
    public Task<T> ExecuteMutationAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => CatalogTransaction.ExecuteAsync(dbContext, action, cancellationToken);

    public Task<CustomerAccessToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        return dbContext.CustomerAccessTokens
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
    }

    public Task<CustomerAccessToken?> GetByIdAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        return dbContext.CustomerAccessTokens
            .SingleOrDefaultAsync(x => x.Id == tokenId, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerAccessToken>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CustomerAccessTokens
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task AddAsync(CustomerAccessToken accessToken, CancellationToken cancellationToken)
    {
        await dbContext.CustomerAccessTokens.AddAsync(accessToken, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            throw new CatalogConflictException("El acceso cambió durante la operación. Actualiza la lista antes de reintentar.");
        }
    }
}
