namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record AdminOrderTemplateResponse(
    Guid CustomerId,
    string CustomerName,
    TimeOnly? PreferredDeliveryTime,
    TimeOnly? PreferredDeliveryWindowStart,
    TimeOnly? PreferredDeliveryWindowEnd,
    string? DeliveryNotes,
    IReadOnlyList<AdminOrderTemplateProductResponse> Products);

public sealed record AdminOrderTemplateProductResponse(
    Guid ProductId,
    string Name,
    string? Description,
    decimal SuggestedQuantity);
