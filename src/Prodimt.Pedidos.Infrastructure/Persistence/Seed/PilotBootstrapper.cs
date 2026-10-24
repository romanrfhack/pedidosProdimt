using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Seed;

public sealed record PilotBootstrapOptions(string AdminUserName, string AdminDisplayName, string AdminPassword)
{
    public static PilotBootstrapOptions FromConfiguration(IConfiguration configuration) => new(
        configuration["PilotBootstrap:AdminUserName"] ?? string.Empty,
        configuration["PilotBootstrap:AdminDisplayName"] ?? string.Empty,
        configuration["PilotBootstrap:AdminPassword"] ?? string.Empty);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AdminUserName) || AdminUserName.Trim().Length > 120 ||
            string.IsNullOrWhiteSpace(AdminDisplayName) || AdminDisplayName.Trim().Length > 200)
            throw new ArgumentException("Configura el usuario y nombre del administrador inicial del piloto.");
        if (string.IsNullOrWhiteSpace(AdminPassword) || AdminPassword.Length is < 16 or > 200 ||
            AdminPassword == DevelopmentAuthSeedValues.DefaultAdminPassword ||
            AdminPassword.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            AdminPassword.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Configura una contrasena propia de 16 a 200 caracteres para el administrador inicial.");
    }
}

public sealed record PilotBootstrapResult(bool AdministratorCreated, int ChannelsCreated);

public static class PilotBootstrapper
{
    public static async Task<PilotBootstrapResult> InitializeAsync(PedidosDbContext db, IPasswordHashService passwords,
        PilotBootstrapOptions options, CancellationToken cancellationToken = default)
    {
        // Validate before touching schema. This command is separate from HTTP startup.
        options.Validate();
        if (!db.Database.IsSqlServer())
            throw new InvalidOperationException("La inicializacion del piloto requiere SQL Server.");

        await db.Database.MigrateAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var administrators = await db.AdminUsers.ToArrayAsync(cancellationToken);
        if (administrators.Length > 0 && !administrators.Any(x =>
                x.UserName.Equals(options.AdminUserName.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ya existe administracion configurada; la inicializacion no agrega ni reemplaza usuarios en una base existente.");

        var createdChannels = 0;
        foreach (var channel in new[]
        {
            new SalesChannel { Id = Guid.NewGuid(), Name = "Cliente", Type = SalesChannelType.Customer, IsInternal = false },
            new SalesChannel { Id = Guid.NewGuid(), Name = "Mostrador", Type = SalesChannelType.InternalCounter, IsInternal = true },
            new SalesChannel { Id = Guid.NewGuid(), Name = "Captura administrativa", Type = SalesChannelType.AdminManualCapture, IsInternal = true }
        })
        {
            if (await db.SalesChannels.AnyAsync(x => x.Type == channel.Type, cancellationToken)) continue;
            db.SalesChannels.Add(channel);
            createdChannels++;
        }
        var createdAdmin = administrators.Length == 0;
        if (createdAdmin)
            db.AdminUsers.Add(AdminUser.Create(options.AdminUserName, options.AdminDisplayName,
                passwords.HashPassword(options.AdminPassword), DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(createdAdmin, createdChannels);
    }
}
