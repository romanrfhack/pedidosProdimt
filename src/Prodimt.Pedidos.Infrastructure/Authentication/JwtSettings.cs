using Microsoft.Extensions.Configuration;

namespace Prodimt.Pedidos.Infrastructure.Authentication;

public sealed record JwtSettings(
    string Issuer,
    string Audience,
    string SigningKey,
    int AccessTokenMinutes)
{
    public const string DevelopmentSigningKey =
        "development-only-prodimt-pedidos-jwt-signing-key-change-before-production-2026";

    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var accessTokenMinutes = int.TryParse(
            configuration["Authentication:Jwt:AccessTokenMinutes"],
            out var parsedMinutes)
            ? parsedMinutes
            : 720;

        return new JwtSettings(
            configuration["Authentication:Jwt:Issuer"] ?? "Prodimt.Pedidos",
            configuration["Authentication:Jwt:Audience"] ?? "Prodimt.Pedidos",
            configuration["Authentication:Jwt:SigningKey"]
                ?? configuration["PRODIMT_JWT_SIGNING_KEY"]
                ?? DevelopmentSigningKey,
            accessTokenMinutes);
    }
}
