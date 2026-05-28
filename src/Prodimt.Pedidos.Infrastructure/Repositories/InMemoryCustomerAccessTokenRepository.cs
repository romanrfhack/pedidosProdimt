using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryCustomerAccessTokenRepository(InMemoryDataStore dataStore) : ICustomerAccessTokenRepository
{
    public Task<CustomerAccessToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        lock (dataStore.SyncRoot)
        {
            return Task.FromResult(dataStore.CustomerAccessTokens.SingleOrDefault(x => x.TokenHash == tokenHash));
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
