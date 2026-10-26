using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.Auth;

public sealed class AdminPasswordService(IAdminUserRepository users, IPasswordHashService passwords, IDateTimeProvider clock)
{
    public async Task ChangeAsync(Guid userId, ChangeAdminPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 1024)
            throw new ArgumentException("Escribe la contraseña actual.");
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length is < 16 or > 128
            || request.NewPassword.Any(char.IsControl))
            throw new ArgumentException("La nueva contraseña debe tener entre 16 y 128 caracteres y no contener caracteres de control.");
        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            throw new ArgumentException("La nueva contraseña y su confirmación no coinciden.");
        var user = await users.GetByIdAsync(userId, cancellationToken);
        var observedHash = user?.PasswordHash;
        if (user is null || !user.IsActive || !passwords.VerifyPassword(observedHash!, request.CurrentPassword))
            throw new AuthenticationFailedException("La contraseña actual es incorrecta.");
        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
            throw new ArgumentException("Elige una contraseña diferente de la actual.");
        var audit = AuditLog.Create("AdminUser", user.Id.ToString(), "AdminUserPasswordChanged", clock.Now,
            AuditActorType.Admin, "Contraseña administrativa actualizada.", user.Id.ToString(), user.DisplayName);
        if (!await users.TryChangePasswordAsync(user.Id, observedHash!, passwords.HashPassword(request.NewPassword), audit, cancellationToken))
            throw new CatalogConflictException("La cuenta cambió durante la operación. Inicia sesión de nuevo antes de reintentar.");
    }
}
