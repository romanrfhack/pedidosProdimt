namespace Prodimt.Pedidos.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public TimeOnly? PreferredDeliveryTime { get; set; }

    public TimeOnly? PreferredDeliveryWindowStart { get; set; }

    public TimeOnly? PreferredDeliveryWindowEnd { get; set; }

    public string? DeliveryNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
