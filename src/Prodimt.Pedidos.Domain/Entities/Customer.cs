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

    public static Customer Create(
        string name,
        string? phoneNumber,
        TimeOnly? preferredDeliveryTime,
        TimeOnly? preferredDeliveryWindowStart,
        TimeOnly? preferredDeliveryWindowEnd,
        string? deliveryNotes,
        DateTimeOffset now)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            CreatedAt = now
        };

        customer.Update(
            name,
            phoneNumber,
            preferredDeliveryTime,
            preferredDeliveryWindowStart,
            preferredDeliveryWindowEnd,
            deliveryNotes,
            now);

        return customer;
    }

    public void Update(
        string name,
        string? phoneNumber,
        TimeOnly? preferredDeliveryTime,
        TimeOnly? preferredDeliveryWindowStart,
        TimeOnly? preferredDeliveryWindowEnd,
        string? deliveryNotes,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del cliente es obligatorio.", nameof(name));
        }

        Name = name.Trim();
        PhoneNumber = phoneNumber?.Trim() ?? string.Empty;
        PreferredDeliveryTime = preferredDeliveryTime;
        PreferredDeliveryWindowStart = preferredDeliveryWindowStart;
        PreferredDeliveryWindowEnd = preferredDeliveryWindowEnd;
        DeliveryNotes = string.IsNullOrWhiteSpace(deliveryNotes) ? null : deliveryNotes.Trim();
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
