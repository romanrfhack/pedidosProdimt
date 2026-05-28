namespace Prodimt.Pedidos.Application.Abstractions;

public sealed record JwtTokenResult(
    string AccessToken,
    DateTimeOffset ExpiresAt);
