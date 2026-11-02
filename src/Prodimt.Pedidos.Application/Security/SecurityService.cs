using System.Text.Json;
using System.Text.RegularExpressions;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.Security;

public sealed class SecurityService(ISecurityStore store, IPasswordHashService passwords, IDateTimeProvider clock)
{
    public async Task<SecuritySnapshot> GetAsync(CancellationToken ct) => Map(await store.ReadAsync(ct));

    public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken ct)
        => store.GetPermissionsAsync(userId, ct);

    public Task<SecuritySnapshot> SaveUserAsync(Guid? id, SaveSecurityUserRequest request, AdminActorContext actor, CancellationToken ct)
    {
        var userName = Required(request.UserName, 120, "usuario");
        if (!Regex.IsMatch(userName, @"^[\p{L}\p{N}_.@+\-]+$"))
            throw new ArgumentException("El usuario admite letras, números y los caracteres _.@+-.");
        var displayName = Required(request.DisplayName, 200, "nombre visible");
        var password = request.Password;
        if (!id.HasValue || !string.IsNullOrEmpty(password)) ValidatePassword(password);
        // Compute once outside the locked transaction. Do not put passwords in audit metadata.
        var hash = string.IsNullOrEmpty(password) ? null : passwords.HashPassword(password);
        return store.ExecuteAsync(request.ExpectedRevision, data =>
        {
            RequireManager(data, actor);
            if (data.Users.Any(x => x.Id != id && string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase)))
                throw new CatalogConflictException("Ya existe un usuario con ese nombre, incluso si está archivado.");
            var roles = ValidateIds(request.RoleIds, data.Roles.Select(x => x.Id), "rol");
            var grants = ValidateIds(request.DirectPermissionIds, data.Permissions.Select(x => x.Id), "permiso");
            var user = id.HasValue ? User(data, id.Value) : AdminUser.Create(userName, displayName, hash!, clock.Now);
            if (!id.HasValue) data.Users.Add(user);
            user.UserName = userName;
            user.DisplayName = displayName;
            if (user.IsActive && !request.IsActive) user.SessionRevision = Guid.NewGuid();
            user.IsActive = request.IsActive;
            if (hash is not null) user.PasswordHash = hash;
            data.UserRoles.RemoveAll(x => x.UserId == user.Id);
            data.UserRoles.AddRange(roles.Select(roleId => new SecurityUserRole { UserId = user.Id, RoleId = roleId }));
            data.UserPermissions.RemoveAll(x => x.UserId == user.Id);
            data.UserPermissions.AddRange(grants.Select(permissionId => new SecurityUserPermission { UserId = user.Id, PermissionId = permissionId }));
            ProtectManagers(data, actor);
            Audit(data, "SecurityUser", user.Id, id.HasValue ? "UserUpdated" : "UserCreated", actor,
                new { user.UserName, user.DisplayName, user.IsActive, roleIds = roles, directPermissionIds = grants, passwordChanged = hash is not null });
            return Map(data);
        }, ct);
    }

    public Task<SecuritySnapshot> DeleteUserAsync(Guid id, Guid revision, AdminActorContext actor, CancellationToken ct)
        => store.ExecuteAsync(revision, data =>
        {
            RequireManager(data, actor);
            var user = User(data, id);
            user.IsActive = false;
            user.SessionRevision = Guid.NewGuid();
            user.IsDeleted = true;
            data.UserRoles.RemoveAll(x => x.UserId == id);
            data.UserPermissions.RemoveAll(x => x.UserId == id);
            ProtectManagers(data, actor);
            Audit(data, "SecurityUser", id, "UserArchived", actor, new { user.UserName });
            return Map(data);
        }, ct);

    public Task<SecuritySnapshot> SaveRoleAsync(Guid? id, SaveSecurityRoleRequest request, AdminActorContext actor, CancellationToken ct)
    {
        var name = Required(request.Name, 120, "nombre del rol");
        var description = Optional(request.Description, 500);
        return store.ExecuteAsync(request.ExpectedRevision, data =>
        {
            RequireManager(data, actor);
            if (data.Roles.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new CatalogConflictException("Ya existe un rol con ese nombre.");
            var permissions = ValidateIds(request.PermissionIds, data.Permissions.Select(x => x.Id), "permiso");
            var role = id.HasValue ? Role(data, id.Value) : new SecurityRole { Id = Guid.NewGuid() };
            if (role.IsSystem && (name != role.Name || !request.IsActive ||
                !permissions.ToHashSet().SetEquals(data.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.PermissionId))))
                throw new CatalogConflictException("El rol Administrador del sistema conserva su nombre, estado y permisos de recuperación.");
            if (!id.HasValue) data.Roles.Add(role);
            role.Name = name; role.Description = description; role.IsActive = request.IsActive;
            data.RolePermissions.RemoveAll(x => x.RoleId == role.Id);
            data.RolePermissions.AddRange(permissions.Select(permissionId => new SecurityRolePermission { RoleId = role.Id, PermissionId = permissionId }));
            ProtectManagers(data, actor);
            Audit(data, "SecurityRole", role.Id, id.HasValue ? "RoleUpdated" : "RoleCreated", actor,
                new { role.Name, role.Description, role.IsActive, permissionIds = permissions });
            return Map(data);
        }, ct);
    }

    public Task<SecuritySnapshot> DeleteRoleAsync(Guid id, Guid revision, AdminActorContext actor, CancellationToken ct)
        => store.ExecuteAsync(revision, data =>
        {
            RequireManager(data, actor);
            var role = Role(data, id);
            if (role.IsSystem || data.UserRoles.Any(x => x.RoleId == id))
                throw new CatalogConflictException("No se puede eliminar un rol del sistema o asignado a usuarios. Retira sus asignaciones primero.");
            data.RolePermissions.RemoveAll(x => x.RoleId == id);
            data.Roles.Remove(role);
            ProtectManagers(data, actor);
            Audit(data, "SecurityRole", id, "RoleDeleted", actor, new { role.Name });
            return Map(data);
        }, ct);

    public Task<SecuritySnapshot> SavePermissionAsync(Guid? id, SaveSecurityPermissionRequest request, AdminActorContext actor, CancellationToken ct)
    {
        var key = Required(request.Key, 160, "clave del permiso");
        if (!Regex.IsMatch(key, @"^[a-z][a-z0-9]*(?:[.][a-z][a-z0-9]*)+$"))
            throw new ArgumentException("La clave debe usar minúsculas y puntos, por ejemplo pedidos.consultar.");
        var name = Required(request.Name, 200, "nombre del permiso");
        var module = Required(request.Module, 120, "módulo");
        var description = Optional(request.Description, 500);
        return store.ExecuteAsync(request.ExpectedRevision, data =>
        {
            RequireManager(data, actor);
            if (data.Permissions.Any(x => x.Id != id && x.Key == key))
                throw new CatalogConflictException("Ya existe un permiso con esa clave.");
            var permission = id.HasValue ? Permission(data, id.Value) : new SecurityPermission { Id = Guid.NewGuid() };
            if (permission.IsSystem && (permission.Key != key || !request.IsActive))
                throw new CatalogConflictException("Las claves y el estado de los permisos del sistema están protegidos.");
            if (id.HasValue && permission.Key != key && (data.RolePermissions.Any(x => x.PermissionId == id) || data.UserPermissions.Any(x => x.PermissionId == id)))
                throw new CatalogConflictException("Retira las asignaciones antes de cambiar la clave del permiso.");
            if (!id.HasValue) data.Permissions.Add(permission);
            permission.Key = key; permission.Name = name; permission.Module = module;
            permission.Description = description; permission.IsActive = request.IsActive;
            ProtectManagers(data, actor);
            Audit(data, "SecurityPermission", permission.Id, id.HasValue ? "PermissionUpdated" : "PermissionCreated", actor,
                new { permission.Key, permission.Name, permission.Module, permission.Description, permission.IsActive });
            return Map(data);
        }, ct);
    }

    public Task<SecuritySnapshot> DeletePermissionAsync(Guid id, Guid revision, AdminActorContext actor, CancellationToken ct)
        => store.ExecuteAsync(revision, data =>
        {
            RequireManager(data, actor);
            var permission = Permission(data, id);
            if (permission.IsSystem || data.RolePermissions.Any(x => x.PermissionId == id) || data.UserPermissions.Any(x => x.PermissionId == id))
                throw new CatalogConflictException("No se puede eliminar un permiso del sistema o asignado. Retira sus asignaciones primero.");
            data.Permissions.Remove(permission);
            ProtectManagers(data, actor);
            Audit(data, "SecurityPermission", id, "PermissionDeleted", actor, new { permission.Key });
            return Map(data);
        }, ct);

    public async Task<AdminUserCatalogResponse> CreateLegacyAsync(CreateAdminUserRequest request, AdminActorContext actor, CancellationToken ct)
    {
        var current = await store.ReadAsync(ct);
        var snapshot = await SaveUserAsync(null, new(current.Revision, request.UserName, request.DisplayName,
            request.Password, true, [], []), actor, ct);
        var user = snapshot.Users.Single(x => x.UserName.Equals(request.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
        return new(user.Id, user.UserName, user.DisplayName, user.IsActive, user.CreatedAt);
    }

    public async Task<AdminUserCatalogResponse> SetLegacyActiveAsync(Guid id, bool active, AdminActorContext actor, CancellationToken ct)
    {
        var current = await store.ReadAsync(ct);
        return await store.ExecuteAsync(current.Revision, data =>
        {
            RequireManager(data, actor);
            var user = User(data, id);
            if (user.IsActive && !active) user.SessionRevision = Guid.NewGuid();
            user.IsActive = active;
            ProtectManagers(data, actor);
            Audit(data, "SecurityUser", id, active ? "UserActivated" : "UserDeactivated", actor, new { user.UserName });
            return new AdminUserCatalogResponse(user.Id, user.UserName, user.DisplayName, user.IsActive, user.CreatedAt);
        }, ct);
    }

    public static IReadOnlyList<EffectivePermissionDto> Resolve(SecurityData data, Guid userId)
    {
        var user = data.Users.SingleOrDefault(x => x.Id == userId);
        if (user is null || !user.IsActive || user.IsDeleted) return [];
        var roles = data.Roles.Where(x => x.IsActive && data.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == x.Id)).ToArray();
        return data.Permissions.Where(x => x.IsActive).Select(permission =>
        {
            var roleNames = roles.Where(role => data.RolePermissions.Any(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id))
                .Select(role => role.Name).Order(StringComparer.Ordinal).ToArray();
            return new EffectivePermissionDto(permission.Id, permission.Key, permission.Name, permission.Module,
                roleNames, data.UserPermissions.Any(up => up.UserId == userId && up.PermissionId == permission.Id));
        }).Where(x => x.IsDirect || x.RoleNames.Count > 0).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
    }

    private static SecuritySnapshot Map(SecurityData data) => new(data.Revision,
        data.Users.Where(x => !x.IsDeleted).OrderBy(x => x.UserName).Select(user => new SecurityUserDto(user.Id, user.UserName,
            user.DisplayName, user.IsActive, data.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.RoleId).ToArray(),
            data.UserPermissions.Where(x => x.UserId == user.Id).Select(x => x.PermissionId).ToArray(), Resolve(data, user.Id), user.CreatedAt)).ToArray(),
        data.Roles.OrderBy(x => x.Name).Select(role => new SecurityRoleDto(role.Id, role.Name, role.Description, role.IsActive,
            role.IsSystem, data.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.PermissionId).ToArray())).ToArray(),
        data.Permissions.OrderBy(x => x.Module).ThenBy(x => x.Key).Select(x => new SecurityPermissionDto(x.Id, x.Key, x.Name,
            x.Module, x.Description, x.IsActive, x.IsSystem)).ToArray());

    private static void RequireManager(SecurityData data, AdminActorContext actor)
    {
        if (!Guid.TryParse(actor.ActorId, out var id) || !Resolve(data, id).Any(x => x.Key == "security.manage"))
            throw new UnauthorizedAccessException("No tienes permiso para administrar la seguridad.");
    }
    private static void ProtectManagers(SecurityData data, AdminActorContext actor)
    {
        if (!data.Users.Any(x => Resolve(data, x.Id).Any(p => p.Key == "security.manage")))
            throw new CatalogConflictException("Debe permanecer al menos un administrador de seguridad activo.");
        if (!Guid.TryParse(actor.ActorId, out var actorId) || !Resolve(data, actorId).Any(x => x.Key == "security.manage"))
            throw new CatalogConflictException("No puedes quitarte tu propio acceso a la administración de seguridad.");
    }
    private void Audit(SecurityData data, string type, Guid id, string action, AdminActorContext actor, object details)
        => data.Audits.Add(AuditLog.Create(type, id.ToString(), action, clock.Now, AuditActorType.Admin,
            $"Administración de seguridad: {action}.", actor.ActorId, actor.ActorDisplayName,
            JsonSerializer.Serialize(details, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    private static Guid[] ValidateIds(Guid[]? ids, IEnumerable<Guid> existing, string name)
    {
        var result = (ids ?? []).Distinct().ToArray();
        if (result.Length > 1000 || result.Except(existing).Any()) throw new ArgumentException($"Se indicó un {name} inexistente o demasiadas asignaciones.");
        return result;
    }
    private static string Required(string? value, int max, string name)
    {
        var result = value?.Trim() ?? "";
        if (result.Length == 0 || result.Length > max || result.Any(char.IsControl))
            throw new ArgumentException($"El {name} es obligatorio, admite hasta {max} caracteres y no admite caracteres de control.");
        return result;
    }
    private static string Optional(string? value, int max)
    {
        var result = value?.Trim() ?? "";
        if (result.Length > max || result.Any(char.IsControl)) throw new ArgumentException($"La descripción admite hasta {max} caracteres y no admite caracteres de control.");
        return result;
    }
    private static void ValidatePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 || value.Any(char.IsControl))
            throw new ArgumentException("La contraseña debe tener entre 16 y 128 caracteres y no contener caracteres de control.");
    }
    private static AdminUser User(SecurityData data, Guid id) => data.Users.SingleOrDefault(x => x.Id == id && !x.IsDeleted)
        ?? throw new KeyNotFoundException("Usuario no encontrado.");
    private static SecurityRole Role(SecurityData data, Guid id) => data.Roles.SingleOrDefault(x => x.Id == id)
        ?? throw new KeyNotFoundException("Rol no encontrado.");
    private static SecurityPermission Permission(SecurityData data, Guid id) => data.Permissions.SingleOrDefault(x => x.Id == id)
        ?? throw new KeyNotFoundException("Permiso no encontrado.");
}
