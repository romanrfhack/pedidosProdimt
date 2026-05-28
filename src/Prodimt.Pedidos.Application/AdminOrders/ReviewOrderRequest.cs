using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record ReviewOrderRequest(AdminDecision Decision, string? InternalNotes);
