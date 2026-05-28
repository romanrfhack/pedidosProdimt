namespace Prodimt.Pedidos.Application.Auth;

public sealed class AuthenticationFailedException(string message) : Exception(message);
