using System.Security.Cryptography;
using System.Text;
using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Infrastructure.Authentication;

public sealed class CustomerAccessTokenHasher : ICustomerAccessTokenHasher
{
    public string HashToken(string token)
    {
        var normalizedToken = token.Trim();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedToken));

        return Convert.ToBase64String(hashBytes);
    }
}
