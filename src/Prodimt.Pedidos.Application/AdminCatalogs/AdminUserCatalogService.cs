using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed class AdminUserCatalogService(
    IAdminUserRepository adminUsers,
    IPasswordHashService passwordHashService,
    IAuditLogRepository auditLogs,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<IReadOnlyList<AdminUserCatalogResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var users = await adminUsers.GetAllAsync(cancellationToken);
        return users.Select(MapUser).ToArray();
    }

    public async Task<AdminUserCatalogResponse> CreateAsync(
        CreateAdminUserRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("La contrasena es obligatoria.", nameof(request));
        }

        var existing = await adminUsers.GetByUserNameAsync(request.UserName, cancellationToken);
        if (existing is not null)
        {
            throw new ArgumentException("Ya existe un usuario administrativo con ese nombre.", nameof(request));
        }

        var user = AdminUser.Create(
            request.UserName,
            request.DisplayName,
            passwordHashService.HashPassword(request.Password),
            dateTimeProvider.Now);

        await adminUsers.AddAsync(user, cancellationToken);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.AdminUser,
            user.Id,
            CatalogAuditEventTypes.AdminUserCreated,
            dateTimeProvider.Now,
            actor,
            $"Usuario administrativo creado: {user.UserName}."), cancellationToken);
        await adminUsers.SaveChangesAsync(cancellationToken);

        return MapUser(user);
    }

    public async Task<AdminUserCatalogResponse> ActivateAsync(
        Guid userId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        user.Activate();
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.AdminUser,
            user.Id,
            CatalogAuditEventTypes.AdminUserActivated,
            dateTimeProvider.Now,
            actor,
            $"Usuario administrativo activado: {user.UserName}."), cancellationToken);
        await adminUsers.SaveChangesAsync(cancellationToken);

        return MapUser(user);
    }

    public async Task<AdminUserCatalogResponse> DeactivateAsync(
        Guid userId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        user.Deactivate();
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.AdminUser,
            user.Id,
            CatalogAuditEventTypes.AdminUserDeactivated,
            dateTimeProvider.Now,
            actor,
            $"Usuario administrativo desactivado: {user.UserName}."), cancellationToken);
        await adminUsers.SaveChangesAsync(cancellationToken);

        return MapUser(user);
    }

    private async Task<AdminUser> GetRequiredUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await adminUsers.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException("Admin user was not found.");
        }

        return user;
    }

    private static AdminUserCatalogResponse MapUser(AdminUser user)
    {
        return new AdminUserCatalogResponse(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.IsActive,
            user.CreatedAt);
    }
}
