using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Authentication;

public static class AdminSessionStamp
{
    public static string Source(AdminUser user) => user.SessionRevision == Guid.Empty ? user.PasswordHash : user.PasswordHash + ":" + user.SessionRevision.ToString("D");
    public static string Create(IConfiguration configuration, string passwordHash) => Base64UrlEncoder.Encode(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(JwtSettings.FromConfiguration(configuration).SigningKey),
            Encoding.UTF8.GetBytes("prodimt-admin-session-v1:" + passwordHash)));

    public static bool Matches(IConfiguration configuration, string passwordHash, string? supplied)
    {
        if (supplied is null || supplied.Length != 43) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Create(configuration, passwordHash)),
            Encoding.UTF8.GetBytes(supplied));
    }
}
