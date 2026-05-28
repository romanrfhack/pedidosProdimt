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

    public Task<CustomerAccessToken?> GetByIdAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        lock (dataStore.SyncRoot)
        {
            return Task.FromResult(dataStore.CustomerAccessTokens.SingleOrDefault(x => x.Id == tokenId));
        }
    }

    public Task<IReadOnlyList<CustomerAccessToken>> GetByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        lock (dataStore.SyncRoot)
        {
            IReadOnlyList<CustomerAccessToken> tokens = dataStore.CustomerAccessTokens
                .Where(x => x.CustomerId == customerId)
                .OrderByDescending(x => x.CreatedAt)
                .ToArray();

            return Task.FromResult(tokens);
        }
    }

    public Task AddAsync(CustomerAccessToken accessToken, CancellationToken cancellationToken)
    {
        lock (dataStore.SyncRoot)
        {
            dataStore.CustomerAccessTokens.Add(accessToken);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
