namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed record ProductSuggestionDto(
    Guid ProductId,
    string Name,
    string? Description,
    decimal SuggestedQuantity);
