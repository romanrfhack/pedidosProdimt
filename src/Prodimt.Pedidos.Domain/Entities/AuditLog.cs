using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Domain.Entities;

public sealed class AuditLog
{
    public Guid Id { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public AuditActorType ActorType { get; set; }

    public string? ActorId { get; set; }

    public string? ActorDisplayName { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    public static AuditLog Create(
        string entityType,
        string entityId,
        string eventType,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        string summary,
        string? actorId = null,
        string? actorDisplayName = null,
        string? metadataJson = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Entity type is required.", nameof(entityType));
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("Entity id is required.", nameof(entityId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type is required.", nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Summary is required.", nameof(summary));
        }

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType.Trim(),
            EntityId = entityId.Trim(),
            EventType = eventType.Trim(),
            OccurredAt = occurredAt,
            ActorType = actorType,
            ActorId = actorId,
            ActorDisplayName = actorDisplayName,
            Summary = summary.Trim(),
            MetadataJson = metadataJson
        };
    }
}
