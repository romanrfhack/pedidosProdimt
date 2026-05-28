using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryAdminUserRepository(InMemoryDataStore dataStore) : IAdminUserRepository
{
    public Task<AdminUser?> GetByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var normalizedUserName = userName.Trim();

        lock (dataStore.SyncRoot)
        {
            return Task.FromResult(dataStore.AdminUsers.SingleOrDefault(x => x.UserName == normalizedUserName));
        }
    }
}
