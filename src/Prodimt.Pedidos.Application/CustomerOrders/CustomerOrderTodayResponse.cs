using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed record CustomerOrderTodayResponse(
    Guid CustomerId,
    string CustomerName,
    DateOnly OrderDate,
    TimeOnly? PreferredDeliveryTime,
    TimeOnly? PreferredDeliveryWindowStart,
    TimeOnly? PreferredDeliveryWindowEnd,
    string? DeliveryNotes,
    CustomerCurrentOrderSummaryResponse? CurrentOrder,
    IReadOnlyList<ProductSuggestionDto> Products);

public sealed record CustomerCurrentOrderSummaryResponse(
    Guid OrderId,
    OrderStatus Status,
    int SequenceNumber,
    DateTimeOffset SubmittedAt,
    bool IsLate,
    bool RequiresAdminReview,
    AdminReviewReason? AdminReviewReason);
