using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed record CustomerOrderResponse(
    Guid OrderId,
    Guid CustomerId,
    DateOnly OrderDate,
    OrderStatus Status,
    int SequenceNumber,
    bool IsLate,
    bool RequiresAdminReview,
    AdminReviewReason? AdminReviewReason);
