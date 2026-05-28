using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record AdminOrderDetailResponse(
    Guid OrderId,
    Guid? CustomerId,
    string CustomerName,
    DateOnly OrderDate,
    DateTimeOffset SubmittedAt,
    OrderStatus Status,
    int SequenceNumber,
    bool IsLate,
    bool RequiresAdminReview,
    AdminReviewReason? AdminReviewReason,
    AdminDecision? AdminDecision,
    TimeOnly? RequestedDeliveryTime,
    TimeOnly? RequestedDeliveryWindowStart,
    TimeOnly? RequestedDeliveryWindowEnd,
    string? DeliveryNotes,
    string? InternalNotes,
    string? SalesChannelName,
    SalesChannelType? SalesChannelType,
    IReadOnlyList<AdminOrderLineResponse> Lines);

public sealed record AdminOrderLineResponse(
    Guid OrderLineId,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    string? Notes,
    Guid? AssignedMachineId,
    string? AssignedMachineName,
    int? AssignedMachineNumber);
