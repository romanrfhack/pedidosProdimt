using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Security;

/// <summary>One revision and one transaction protect grants, recovery access and audit together.</summary>
public sealed class EfSecurityStore(PedidosDbContext db) : ISecurityStore
{
    public async Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockAsync("Shared", cancellationToken);
        var activeUsers = db.AdminUsers.Where(x => x.Id == userId && x.IsActive && !x.IsDeleted).Select(x => x.Id);
        var inherited = from userRole in db.SecurityUserRoles
                        join role in db.SecurityRoles on userRole.RoleId equals role.Id
                        join grant in db.SecurityRolePermissions on role.Id equals grant.RoleId
                        join permission in db.SecurityPermissions on grant.PermissionId equals permission.Id
                        where activeUsers.Contains(userRole.UserId) && role.IsActive && permission.IsActive
                        select permission.Key;
        var direct = from grant in db.SecurityUserPermissions
                     join permission in db.SecurityPermissions on grant.PermissionId equals permission.Id
                     where activeUsers.Contains(grant.UserId) && permission.IsActive
                     select permission.Key;
        var result = await inherited.Union(direct).OrderBy(x => x).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<SecurityData> ReadAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockAsync("Shared", cancellationToken);
        var data = await LoadAsync(false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return data;
    }

    public async Task<T> ExecuteAsync<T>(Guid expectedRevision, Func<SecurityData, T> mutation,
        CancellationToken cancellationToken)
    {
        try
        {
            // Authentication may have tracked a user before waiting for the security lock.
            // Reload the graph inside the transaction; never mutate that earlier snapshot.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await LockAsync("Exclusive", cancellationToken);
            var data = await LoadAsync(true, cancellationToken);
            if (data.Revision != expectedRevision)
                throw new CatalogConflictException("La configuración de seguridad cambió. Recarga antes de guardar.");

            var users = data.Users.ToArray();
            var roles = data.Roles.ToArray();
            var permissions = data.Permissions.ToArray();
            var userRoles = data.UserRoles.ToArray();
            var rolePermissions = data.RolePermissions.ToArray();
            var userPermissions = data.UserPermissions.ToArray();
            data.Revision = Guid.NewGuid();
            var result = mutation(data);

            Synchronize(users, data.Users, x => x.Id);
            Synchronize(roles, data.Roles, x => x.Id);
            Synchronize(permissions, data.Permissions, x => x.Id);
            Synchronize(userRoles, data.UserRoles, x => (x.UserId, x.RoleId));
            Synchronize(rolePermissions, data.RolePermissions, x => (x.RoleId, x.PermissionId));
            Synchronize(userPermissions, data.UserPermissions, x => (x.UserId, x.PermissionId));
            await db.AuditLogs.AddRangeAsync(data.Audits, cancellationToken);
            var state = await db.SecurityStates.SingleAsync(x => x.Id == 1, cancellationToken);
            state.Revision = data.Revision;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex) when (CatalogPersistenceConflicts.IsConcurrentChange(ex) || IsSecurityConflict(ex))
        {
            throw new CatalogConflictException("La configuración de seguridad cambió. Recarga antes de guardar.");
        }
    }

    private static bool IsSecurityConflict(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is SqlException { Number: 2601 or 2627 or 51018 }) return true;
        return false;
    }

    private async Task<SecurityData> LoadAsync(bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<T> Query<T>() where T : class => tracking ? db.Set<T>() : db.Set<T>().AsNoTracking();
        var state = await Query<SecurityState>().SingleAsync(x => x.Id == 1, cancellationToken);
        return new SecurityData
        {
            Revision = state.Revision,
            Users = await Query<AdminUser>().ToListAsync(cancellationToken),
            Roles = await Query<SecurityRole>().ToListAsync(cancellationToken),
            Permissions = await Query<SecurityPermission>().ToListAsync(cancellationToken),
            UserRoles = await Query<SecurityUserRole>().ToListAsync(cancellationToken),
            RolePermissions = await Query<SecurityRolePermission>().ToListAsync(cancellationToken),
            UserPermissions = await Query<SecurityUserPermission>().ToListAsync(cancellationToken)
        };
    }

    private void Synchronize<TEntity, TKey>(IReadOnlyCollection<TEntity> original,
        IReadOnlyCollection<TEntity> current, Func<TEntity, TKey> key) where TEntity : class where TKey : notnull
    {
        var next = current.Select(key).ToHashSet();
        var previous = original.Select(key).ToHashSet();
        db.RemoveRange(original.Where(x => !next.Contains(key(x))));
        db.AddRange(current.Where(x => !previous.Contains(key(x))));
    }

    private async Task LockAsync(string mode, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer()) return;
        // The mode is fixed by this class; parameters keep the SQL text invariant.
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = N'PRODIMT.Security', @LockMode = {mode}, @LockOwner = N'Transaction', @LockTimeout = 15000; IF @result < 0 THROW 51018, 'Security configuration is busy.', 1;", cancellationToken);
        }
        catch (Exception ex) when (CatalogPersistenceConflicts.IsConcurrentChange(ex) || IsSecurityConflict(ex))
        {
            throw new CatalogConflictException("La configuración de seguridad está ocupada. Vuelve a intentar.");
        }
    }
}
