namespace Prodimt.Pedidos.Domain.Entities;

public sealed class SecurityRole
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
}

public sealed class SecurityPermission
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
}

public sealed class SecurityUserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}

public sealed class SecurityRolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public sealed class SecurityUserPermission
{
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
}

public sealed class SecurityState
{
    public int Id { get; set; } = 1;
    public Guid Revision { get; set; }
}
