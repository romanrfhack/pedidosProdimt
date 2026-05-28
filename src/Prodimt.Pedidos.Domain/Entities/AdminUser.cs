namespace Prodimt.Pedidos.Domain.Entities;

public sealed class AdminUser
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public static AdminUser Create(
        string userName,
        string displayName,
        string passwordHash,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("El usuario administrativo es obligatorio.", nameof(userName));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("El nombre visible del usuario administrativo es obligatorio.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("El hash de contrasena es obligatorio.", nameof(passwordHash));
        }

        return new AdminUser
        {
            Id = Guid.NewGuid(),
            UserName = userName.Trim(),
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
            IsActive = true,
            CreatedAt = now
        };
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
