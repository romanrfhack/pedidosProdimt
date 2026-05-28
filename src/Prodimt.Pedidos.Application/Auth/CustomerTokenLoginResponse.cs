namespace Prodimt.Pedidos.Application.Auth;

public sealed record CustomerTokenLoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    Guid CustomerId,
    string CustomerName);
