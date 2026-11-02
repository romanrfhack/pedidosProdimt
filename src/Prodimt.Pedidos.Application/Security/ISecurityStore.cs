using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Security;

public interface ISecurityStore
{
    Task<SecurityData> ReadAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);
    Task<T> ExecuteAsync<T>(Guid expectedRevision, Func<SecurityData, T> mutation, CancellationToken cancellationToken);
}

public sealed class SecurityData
{
    public Guid Revision { get; set; }
    public List<AdminUser> Users { get; set; } = [];
    public List<SecurityRole> Roles { get; set; } = [];
    public List<SecurityPermission> Permissions { get; set; } = [];
    public List<SecurityUserRole> UserRoles { get; set; } = [];
    public List<SecurityRolePermission> RolePermissions { get; set; } = [];
    public List<SecurityUserPermission> UserPermissions { get; set; } = [];
    public List<AuditLog> Audits { get; set; } = [];
}
