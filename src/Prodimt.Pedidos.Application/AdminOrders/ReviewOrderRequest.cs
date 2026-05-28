using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record ReviewOrderRequest(
    AdminDecision Decision,
    string? InternalNotes,
    TimeOnly? RequestedDeliveryTime = null,
    TimeOnly? RequestedDeliveryWindowStart = null,
    TimeOnly? RequestedDeliveryWindowEnd = null,
    string? DeliveryNotes = null,
    IReadOnlyList<ReviewOrderLineAdjustmentRequest>? LineAdjustments = null);

public sealed record ReviewOrderLineAdjustmentRequest(Guid OrderLineId, decimal Quantity, string? Notes);
