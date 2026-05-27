namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed record CustomerOrderTodayResponse(
    Guid CustomerId,
    string CustomerName,
    DateOnly OrderDate,
    TimeOnly? PreferredDeliveryTime,
    TimeOnly? PreferredDeliveryWindowStart,
    TimeOnly? PreferredDeliveryWindowEnd,
    string? DeliveryNotes,
    IReadOnlyList<ProductSuggestionDto> Products);
