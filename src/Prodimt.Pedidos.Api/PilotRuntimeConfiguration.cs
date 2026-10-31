using System.Text;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.Api;

public static class PilotRuntimeConfiguration
{
    public static void ValidateProduction(IConfiguration configuration, JwtSettings jwt)
    {
        var key = configuration["Authentication:Jwt:SigningKey"] ?? configuration["PRODIMT_JWT_SIGNING_KEY"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32 ||
            key == JwtSettings.DevelopmentSigningKey || key.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production requiere una clave JWT propia de al menos 32 bytes; no puede usar la clave demo.");
        if (jwt.AccessTokenMinutes is < 1 or > 1440)
            throw new InvalidOperationException("La vigencia del token debe estar entre 1 y 1440 minutos.");
        if (!string.Equals(configuration["Persistence:Provider"] ?? "SqlServer", "SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El piloto Production requiere persistencia SQL Server.");
        var connection = configuration.GetConnectionString("Pedidos") ?? configuration["PRODIMT_PEDIDOS_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(connection) || connection.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production requiere configurar la conexion SQL Server del piloto.");
        if (IsTrue(configuration["DevelopmentSeed:Enabled"]) || IsTrue(configuration["DevelopmentSeed:ApplyMigrations"]))
            throw new InvalidOperationException("Production no admite semilla demo ni migraciones automaticas; usa la inicializacion explicita del piloto.");
    }

    private static bool IsTrue(string? value) => bool.TryParse(value, out var enabled) && enabled;
}
