namespace Prodimt.Pedidos.Application.Abstractions;

public interface ICustomerAccessTokenHasher
{
    string HashToken(string token);
}
