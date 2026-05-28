using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfCustomerAccessTokenRepository(PedidosDbContext dbContext) : ICustomerAccessTokenRepository
{
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

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
