using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryAdminUserRepository(InMemoryDataStore dataStore) : IAdminUserRepository
{
    public Task<IReadOnlyList<AdminUser>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (dataStore.SyncRoot)
        {
            IReadOnlyList<AdminUser> users = dataStore.AdminUsers
                .OrderBy(x => x.UserName)
                .ToArray();

            return Task.FromResult(users);
        }
    }

    public Task<AdminUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        lock (dataStore.SyncRoot)
        {
            return Task.FromResult(dataStore.AdminUsers.SingleOrDefault(x => x.Id == userId));
        }
    }

    public Task<AdminUser?> GetByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var normalizedUserName = userName.Trim();

        lock (dataStore.SyncRoot)
        {
            return Task.FromResult(dataStore.AdminUsers.SingleOrDefault(x => x.UserName == normalizedUserName));
        }
    }

    public Task AddAsync(AdminUser adminUser, CancellationToken cancellationToken)
    {
        lock (dataStore.SyncRoot)
        {
            dataStore.AdminUsers.Add(adminUser);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
