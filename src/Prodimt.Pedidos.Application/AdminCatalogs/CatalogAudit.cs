using System.Text.Json;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

internal static class CatalogAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AuditLog Create(
        string entityType,
        Guid entityId,
        string eventType,
        DateTimeOffset occurredAt,
        AdminActorContext? actor,
        string summary,
        object? metadata = null)
    {
        return AuditLog.Create(
            entityType,
            entityId.ToString(),
            eventType,
            occurredAt,
            AuditActorType.Admin,
            summary,
            actor?.ActorId,
            actor?.ActorDisplayName,
            metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions));
    }
}
