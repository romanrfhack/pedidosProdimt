using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record AdminOrderSummaryResponse(
    Guid OrderId,
    Guid? CustomerId,
    DateOnly OrderDate,
    DateTimeOffset SubmittedAt,
    OrderStatus Status,
    int SequenceNumber,
    bool IsLate,
    bool RequiresAdminReview,
    AdminReviewReason? AdminReviewReason,
    TimeOnly? RequestedDeliveryTime,
    TimeOnly? RequestedDeliveryWindowStart,
    TimeOnly? RequestedDeliveryWindowEnd,
    string? DeliveryNotes);
