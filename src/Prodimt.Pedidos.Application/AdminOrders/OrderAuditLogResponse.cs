using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record OrderAuditLogResponse(
    Guid Id,
    Guid OrderId,
    Guid? CustomerId,
    OrderAuditEventType EventType,
    DateTimeOffset OccurredAt,
    AuditActorType ActorType,
    string? ActorId,
    string? ActorDisplayName,
    OrderStatus? OrderStatus,
    AdminReviewReason? AdminReviewReason,
    AdminDecision? AdminDecision,
    string Summary,
    string? Metadata);
