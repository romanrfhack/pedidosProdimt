namespace Prodimt.Pedidos.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ExternalCode { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public TimeOnly? PreferredDeliveryTime { get; set; }

    public TimeOnly? PreferredDeliveryWindowStart { get; set; }

    public TimeOnly? PreferredDeliveryWindowEnd { get; set; }

    public string? DeliveryNotes { get; set; }

    public string? RouteCode { get; set; }

    public int? RouteStopOrder { get; set; }

    public bool? HasStoreKey { get; set; }

    public string? PaymentTermCode { get; set; }

    // Internal access instructions must never be included in customer-facing DTOs.
    public string? InternalDeliveryInstructions { get; set; }

    public int OrderingProfileVersion { get; set; }

    public bool IsOrderProfileApproved { get; set; }

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

    public void SetExternalCode(string? externalCode, DateTimeOffset now)
    {
        ExternalCode = string.IsNullOrWhiteSpace(externalCode) ? null : externalCode.Trim();
        UpdatedAt = now;
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

    public void SetPilotDelivery(
        string? routeCode,
        int? routeStopOrder,
        bool? hasStoreKey,
        string? paymentTermCode,
        string? internalDeliveryInstructions,
        DateTimeOffset now)
    {
        var route = string.IsNullOrWhiteSpace(routeCode) ? null : routeCode.Trim();
        var payment = string.IsNullOrWhiteSpace(paymentTermCode) ? null : paymentTermCode.Trim().ToLowerInvariant();
        var instructions = string.IsNullOrWhiteSpace(internalDeliveryInstructions) ? null : internalDeliveryInstructions.Trim();
        if (route?.Length > 80)
            throw new ArgumentException("El codigo de ruta admite hasta 80 caracteres.", nameof(routeCode));
        if (routeStopOrder <= 0)
            throw new ArgumentException("El orden de parada debe ser positivo.", nameof(routeStopOrder));
        if (routeStopOrder is not null && route is null)
            throw new ArgumentException("Indica una ruta para asignar el orden de parada.", nameof(routeCode));
        if (payment is not null && payment is not ("cash_on_delivery" or "credit"))
            throw new ArgumentException("Condicion de pago invalida. Usa cash_on_delivery o credit.", nameof(paymentTermCode));
        if (instructions?.Length > 1000)
            throw new ArgumentException("Las instrucciones internas admiten hasta 1000 caracteres.", nameof(internalDeliveryInstructions));

        RouteCode = route;
        RouteStopOrder = routeStopOrder;
        HasStoreKey = hasStoreKey;
        PaymentTermCode = payment;
        InternalDeliveryInstructions = instructions;
        UpdatedAt = now;
    }

    public void RecordOrderingProfileChange(bool approved, DateTimeOffset now)
    {
        OrderingProfileVersion = checked(OrderingProfileVersion + 1);
        IsOrderProfileApproved = approved;
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
