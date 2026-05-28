namespace Prodimt.Pedidos.Application.Auth;

public sealed record AdminLoginRequest(
    string UserName,
    string Password);
