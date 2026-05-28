namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record PendingCustomerOrderResponse(
    Guid CustomerId,
    string CustomerName,
    string PhoneNumber,
    TimeOnly? PreferredDeliveryTime,
    TimeOnly? PreferredDeliveryWindowStart,
    TimeOnly? PreferredDeliveryWindowEnd,
    string? DeliveryNotes,
    int FrequentProductsCount);
