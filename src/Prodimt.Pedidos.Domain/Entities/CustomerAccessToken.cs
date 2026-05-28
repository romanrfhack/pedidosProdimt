namespace Prodimt.Pedidos.Domain.Entities;

public sealed class CustomerAccessToken
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
