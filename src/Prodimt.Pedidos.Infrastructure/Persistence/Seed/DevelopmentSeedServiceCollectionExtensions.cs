using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Seed;

public static class DevelopmentSeedServiceCollectionExtensions
{
    public static async Task ApplyDevelopmentSeedAsync(
        this IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!ReadBoolean(configuration, "DevelopmentSeed:Enabled", defaultValue: true))
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetService<PedidosDbContext>();

        if (dbContext is null)
        {
            logger.LogInformation("Development seed skipped because EF Core persistence is not registered.");
            return;
        }

        if (ReadBoolean(configuration, "DevelopmentSeed:ApplyMigrations", defaultValue: true))
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        await PedidosDevelopmentSeeder.SeedAsync(dbContext, cancellationToken);
    }

    private static bool ReadBoolean(IConfiguration configuration, string key, bool defaultValue)
    {
        var value = configuration[key];
        return bool.TryParse(value, out var parsedValue) ? parsedValue : defaultValue;
    }
}
