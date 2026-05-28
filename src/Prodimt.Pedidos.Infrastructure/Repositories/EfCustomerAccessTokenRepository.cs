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

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
