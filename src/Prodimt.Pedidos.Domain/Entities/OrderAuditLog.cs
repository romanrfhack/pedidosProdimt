using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Domain.Entities;

public sealed class OrderAuditLog
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid? CustomerId { get; set; }

    public OrderAuditEventType EventType { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public AuditActorType ActorType { get; set; }

    public string? ActorId { get; set; }

    public string? ActorDisplayName { get; set; }

    public OrderStatus? OrderStatus { get; set; }

    public AdminReviewReason? AdminReviewReason { get; set; }

    public AdminDecision? AdminDecision { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static OrderAuditLog Create(
        Order order,
        OrderAuditEventType eventType,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        string summary,
        string? actorId = null,
        string? actorDisplayName = null,
        string? metadataJson = null)
    {
        return new OrderAuditLog
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            EventType = eventType,
            OccurredAt = occurredAt,
            ActorType = actorType,
            ActorId = actorId,
            ActorDisplayName = actorDisplayName,
            OrderStatus = order.Status,
            AdminReviewReason = order.AdminReviewReason,
            AdminDecision = order.AdminDecision,
            Summary = summary,
            MetadataJson = metadataJson,
            CreatedAt = occurredAt
        };
    }
}
