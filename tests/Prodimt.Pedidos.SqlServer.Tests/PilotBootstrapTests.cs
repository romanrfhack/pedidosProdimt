using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class PilotBootstrapTests(SqlServerFixture fixture)
{
    // Synthetic credentials used only in disposable, independently generated test databases.
    private static readonly PilotBootstrapOptions Options = new(
        "bootstrap-test-admin", "Synthetic bootstrap administrator", "Bootstrap-test-only-2026!");
    private readonly PasswordHashService _passwords = new();

    [Fact]
    public async Task Empty_database_is_migrated_and_receives_only_channels_and_initial_administrator()
    {
        await using var database = new IsolatedDatabase(fixture.ConnectionString);
        Assert.False(await database.ExistsAsync());
        await using var db = database.CreateDbContext();

        var result = await PilotBootstrapper.InitializeAsync(db, _passwords, Options);

        Assert.True(result.AdministratorCreated);
        Assert.Equal(3, result.ChannelsCreated);
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var admin = Assert.Single(await db.AdminUsers.AsNoTracking().ToArrayAsync());
        Assert.Equal(Options.AdminUserName, admin.UserName);
        Assert.Equal(Options.AdminDisplayName, admin.DisplayName);
        Assert.True(admin.IsActive);
        Assert.NotEqual(Options.AdminPassword, admin.PasswordHash);
        Assert.True(_passwords.VerifyPassword(admin.PasswordHash, Options.AdminPassword));
        Assert.Collection((await db.SalesChannels.AsNoTracking().ToArrayAsync()).OrderBy(x => x.Type),
            channel => AssertChannel(channel, SalesChannelType.Customer, "Cliente", false),
            channel => AssertChannel(channel, SalesChannelType.InternalCounter, "Mostrador", true),
            channel => AssertChannel(channel, SalesChannelType.AdminManualCapture, "Captura administrativa", true));
        await AssertNoOperationalDataAsync(db);
    }

    [Fact]
    public async Task Retry_preserves_existing_credentials_account_state_and_channels_without_duplicates()
    {
        await using var database = new IsolatedDatabase(fixture.ConnectionString);
        await using (var setup = database.CreateDbContext())
        {
            await PilotBootstrapper.InitializeAsync(setup, _passwords, Options);
            var admin = await setup.AdminUsers.SingleAsync();
            admin.DisplayName = "Edited synthetic administrator";
            admin.PasswordHash = _passwords.HashPassword("Changed-test-password-2026!");
            admin.Deactivate();
            var channel = await setup.SalesChannels.SingleAsync(x => x.Type == SalesChannelType.InternalCounter);
            channel.Name = "Edited synthetic counter";
            await setup.SaveChangesAsync();
        }

        await using var db = database.CreateDbContext();
        var before = await db.AdminUsers.AsNoTracking().SingleAsync();
        var channelsBefore = await db.SalesChannels.AsNoTracking().OrderBy(x => x.Type)
            .Select(x => new { x.Id, x.Type, x.Name, x.IsInternal }).ToArrayAsync();

        var result = await PilotBootstrapper.InitializeAsync(db, _passwords, Options with
        {
            AdminUserName = $" {Options.AdminUserName.ToUpperInvariant()} ",
            AdminDisplayName = "Replacement must not be applied",
            AdminPassword = "Replacement-test-password-2026!"
        });

        Assert.False(result.AdministratorCreated);
        Assert.Equal(0, result.ChannelsCreated);
        var after = Assert.Single(await db.AdminUsers.AsNoTracking().ToArrayAsync());
        AssertSameAdministrator(before, after);
        Assert.False(after.IsActive);
        Assert.True(_passwords.VerifyPassword(after.PasswordHash, "Changed-test-password-2026!"));
        Assert.False(_passwords.VerifyPassword(after.PasswordHash, "Replacement-test-password-2026!"));
        var channelsAfter = await db.SalesChannels.AsNoTracking().OrderBy(x => x.Type)
            .Select(x => new { x.Id, x.Type, x.Name, x.IsInternal }).ToArrayAsync();
        Assert.Equal(channelsBefore, channelsAfter);
        await AssertNoOperationalDataAsync(db);
    }

    [Fact]
    public async Task Different_existing_administrator_rejects_bootstrap_without_adding_channels_or_users()
    {
        await using var database = new IsolatedDatabase(fixture.ConnectionString);
        AdminUser before;
        await using (var setup = database.CreateDbContext())
        {
            await setup.Database.MigrateAsync();
            before = AdminUser.Create("existing-test-admin", "Existing synthetic administrator",
                _passwords.HashPassword("Existing-test-password-2026!"), SqlServerFixture.Now);
            setup.AdminUsers.Add(before);
            await setup.SaveChangesAsync();
        }

        await using (var db = database.CreateDbContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PilotBootstrapper.InitializeAsync(db, _passwords, Options));

        await using var verify = database.CreateDbContext();
        AssertSameAdministrator(before, Assert.Single(await verify.AdminUsers.AsNoTracking().ToArrayAsync()));
        Assert.Empty(await verify.SalesChannels.ToArrayAsync());
        await AssertNoOperationalDataAsync(verify);
    }

    [Fact]
    public async Task Invalid_bootstrap_credentials_are_rejected_before_creating_database()
    {
        await using var database = new IsolatedDatabase(fixture.ConnectionString);
        Assert.False(await database.ExistsAsync());
        await using var db = database.CreateDbContext();

        await Assert.ThrowsAsync<ArgumentException>(() => PilotBootstrapper.InitializeAsync(db, _passwords,
            Options with { AdminPassword = "too-short" }));

        Assert.False(await database.ExistsAsync());
    }

    private static void AssertChannel(SalesChannel channel, SalesChannelType type, string name, bool isInternal)
    {
        Assert.NotEqual(Guid.Empty, channel.Id);
        Assert.Equal(type, channel.Type);
        Assert.Equal(name, channel.Name);
        Assert.Equal(isInternal, channel.IsInternal);
    }

    private static void AssertSameAdministrator(AdminUser expected, AdminUser actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.UserName, actual.UserName);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.PasswordHash, actual.PasswordHash);
        Assert.Equal(expected.IsActive, actual.IsActive);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
    }

    private static async Task AssertNoOperationalDataAsync(PedidosDbContext db)
    {
        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.Products.CountAsync());
        Assert.Equal(0, await db.Machines.CountAsync());
        Assert.Equal(0, await db.CustomerFrequentProducts.CountAsync());
        Assert.Equal(0, await db.CustomerMachineAssignments.CountAsync());
        Assert.Equal(0, await db.CustomerAccessTokens.CountAsync());
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Equal(0, await db.OrderLines.CountAsync());
        Assert.Equal(0, await db.OrderOperations.CountAsync());
        Assert.Equal(0, await db.OrderAuditLogs.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    private sealed class IsolatedDatabase : IAsyncDisposable
    {
        private readonly string _databaseName = $"ProdimtBootstrapTest_{Guid.NewGuid():N}";
        private readonly string _connectionString;
        private readonly string _masterConnectionString;

        public IsolatedDatabase(string fixtureConnectionString)
        {
            var builder = new SqlConnectionStringBuilder(fixtureConnectionString) { InitialCatalog = "master" };
            _masterConnectionString = builder.ConnectionString;
            builder.InitialCatalog = _databaseName;
            _connectionString = builder.ConnectionString;
        }

        public PedidosDbContext CreateDbContext() => new(new DbContextOptionsBuilder<PedidosDbContext>()
            .UseSqlServer(_connectionString).Options);

        public async Task<bool> ExistsAsync()
        {
            await using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DB_ID(@databaseName)";
            command.Parameters.AddWithValue("@databaseName", _databaseName);
            var id = await command.ExecuteScalarAsync();
            return id is not null and not DBNull;
        }

        public async ValueTask DisposeAsync()
        {
            // This connection always targets our generated catalog, never the configured or fixture database.
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
