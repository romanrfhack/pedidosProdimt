namespace Prodimt.Pedidos.Application.Abstractions;

public interface IPasswordHashService
{
    string HashPassword(string password);

    bool VerifyPassword(string passwordHash, string password);
}
