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

    public static CustomerAccessToken Create(
        Guid customerId,
        string tokenHash,
        string? description,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("El hash del token es obligatorio.", nameof(tokenHash));
        }

        return new CustomerAccessToken
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            TokenHash = tokenHash,
            DisplayName = string.IsNullOrWhiteSpace(description) ? "Token de cliente" : description.Trim(),
            ExpiresAt = expiresAt,
            IsActive = true,
            CreatedAt = now
        };
    }

    public void Revoke()
    {
        IsActive = false;
    }
}
