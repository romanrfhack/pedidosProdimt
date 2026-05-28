namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record AdminSubmitCustomerOrderRequest(
    IReadOnlyList<AdminSubmitCustomerOrderLineRequest> Lines,
    TimeOnly? RequestedDeliveryTime,
    TimeOnly? RequestedDeliveryWindowStart,
    TimeOnly? RequestedDeliveryWindowEnd,
    string? DeliveryNotes,
    string? InternalNotes);

public sealed record AdminSubmitCustomerOrderLineRequest(Guid ProductId, decimal Quantity, string? Notes);
