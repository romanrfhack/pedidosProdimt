namespace Prodimt.Pedidos.Application.Auth;

public sealed record AdminLoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string DisplayName);
