using System.Text.Json;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemorySecurityStore : ISecurityStore
{
    private readonly InMemoryDataStore dataStore;
    private SecurityData state;
    public InMemorySecurityStore(InMemoryDataStore dataStore)
    {
        this.dataStore = dataStore;
        state = new SecurityData
        {
            Revision = SecurityDefaults.InitialRevision,
            Roles = [new SecurityRole { Id = SecurityDefaults.AdministratorRoleId, Name = "Administrador", Description = "Administración completa y recuperación", IsActive = true, IsSystem = true }],
            Permissions = SecurityDefaults.Permissions.ToList(),
            UserRoles = dataStore.AdminUsers.Select(x => new SecurityUserRole { UserId = x.Id, RoleId = SecurityDefaults.AdministratorRoleId }).ToList(),
            RolePermissions = SecurityDefaults.Permissions.Select(x => new SecurityRolePermission { RoleId = SecurityDefaults.AdministratorRoleId, PermissionId = x.Id }).ToList()
        };
    }
    public Task<SecurityData> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (dataStore.SyncRoot)
        {
            state.Users = dataStore.AdminUsers;
            return Task.FromResult(Clone(state));
        }
    }
    public async Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
        => SecurityService.Resolve(await ReadAsync(cancellationToken), userId).Select(x => x.Key).ToArray();
    public Task<T> ExecuteAsync<T>(Guid expectedRevision, Func<SecurityData, T> mutation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (dataStore.SyncRoot)
        {
            if (expectedRevision != state.Revision) throw new CatalogConflictException("La seguridad cambió. Actualiza la vista antes de reintentar.");
            state.Users = dataStore.AdminUsers;
            var next = Clone(state);
            next.Revision = Guid.NewGuid();
            next.Audits = [];
            var result = mutation(next);
            dataStore.AdminUsers.Clear();
            dataStore.AdminUsers.AddRange(next.Users);
            dataStore.AuditLogs.AddRange(next.Audits);
            next.Audits = [];
            state = next;
            return Task.FromResult(result);
        }
    }
    private static SecurityData Clone(SecurityData value) => JsonSerializer.Deserialize<SecurityData>(JsonSerializer.Serialize(value))!;
}
