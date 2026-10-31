namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record AdminMarkNoOrderRequest(string? InternalNotes, DateOnly? ServiceDate = null,
    bool AutoDeliveryDate = false, DateOnly? ExpectedDeliveryDate = null);
