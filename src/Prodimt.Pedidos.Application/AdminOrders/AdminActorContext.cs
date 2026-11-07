namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record AdminActorContext(string? ActorId, string? ActorDisplayName, bool IsSystemAdministrator = false);
