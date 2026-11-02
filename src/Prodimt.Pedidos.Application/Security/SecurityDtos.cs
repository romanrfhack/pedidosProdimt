namespace Prodimt.Pedidos.Application.Security;

public sealed record SecuritySnapshot(Guid Revision, IReadOnlyList<SecurityUserDto> Users,
    IReadOnlyList<SecurityRoleDto> Roles, IReadOnlyList<SecurityPermissionDto> Permissions);
public sealed record SecurityUserDto(Guid Id, string UserName, string DisplayName, bool IsActive,
    IReadOnlyList<Guid> RoleIds, IReadOnlyList<Guid> DirectPermissionIds,
    IReadOnlyList<EffectivePermissionDto> EffectivePermissions, DateTimeOffset CreatedAt);
public sealed record SecurityRoleDto(Guid Id, string Name, string Description, bool IsActive, bool IsSystem,
    IReadOnlyList<Guid> PermissionIds);
public sealed record SecurityPermissionDto(Guid Id, string Key, string Name, string Module,
    string Description, bool IsActive, bool IsSystem);
public sealed record EffectivePermissionDto(Guid Id, string Key, string Name, string Module,
    IReadOnlyList<string> RoleNames, bool IsDirect);
public sealed record SaveSecurityUserRequest(Guid ExpectedRevision, string UserName, string DisplayName,
    string? Password, bool IsActive, Guid[]? RoleIds, Guid[]? DirectPermissionIds);
public sealed record SaveSecurityRoleRequest(Guid ExpectedRevision, string Name, string? Description,
    bool IsActive, Guid[]? PermissionIds);
public sealed record SaveSecurityPermissionRequest(Guid ExpectedRevision, string Key, string Name,
    string Module, string? Description, bool IsActive);
