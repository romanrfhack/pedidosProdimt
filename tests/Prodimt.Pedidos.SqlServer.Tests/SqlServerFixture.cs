using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.SqlServer.Tests;

[CollectionDefinition("SQL Server", DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;

public sealed class SqlServerFixture : IAsyncLifetime
{
    // These credentials exist only in a newly created disposable test database.
    private const string AdminPassword = "Sql-test-only-admin-2026!";
    private readonly string _databaseName = $"ProdimtI02Test_{Guid.NewGuid():N}";
    private string _masterConnection = string.Empty;
    private bool _created;
    public string ConnectionString { get; private set; } = string.Empty;
    public static DateTimeOffset Now => new(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);
    public Guid SalesChannelId { get; } = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("PRODIMT_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Set PRODIMT_SQL_TEST_CONNECTION to an isolated SQL Server test instance. These tests never substitute SQLite or skip persistence checks.");

        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5
        };
        _masterConnection = builder.ConnectionString;
        builder.InitialCatalog = _databaseName;
        ConnectionString = builder.ConnectionString;

        await using (var connection = new SqlConnection(_masterConnection))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            // The database identifier is generated here, never read from user configuration.
            create.CommandText = $"CREATE DATABASE [{_databaseName}]";
            await create.ExecuteNonQueryAsync();
            _created = true;
        }

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        db.SalesChannels.Add(new SalesChannel
        {
            Id = SalesChannelId, Name = "SQL Test customer", Type = SalesChannelType.Customer
        });
        db.SalesChannels.Add(new SalesChannel
        {
            Id = Guid.NewGuid(), Name = "SQL Test admin", Type = SalesChannelType.AdminManualCapture, IsInternal = true
        });
        db.AdminUsers.Add(AdminUser.Create("sql-test-admin", "SQL Test Admin", new PasswordHashService().HashPassword(AdminPassword), Now));
        await db.SaveChangesAsync();
    }

    public PedidosDbContext CreateDbContext() => new(new DbContextOptionsBuilder<PedidosDbContext>()
        .UseSqlServer(ConnectionString).Options);

    public SqlApiFactory CreateFactory() => new(ConnectionString);

    public static async Task<HttpClient> CreateAdminClientAsync(SqlApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsJsonAsync("/api/auth/admin/login", new { userName = "sql-test-admin", password = AdminPassword });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    public async Task<TestCatalog> CreateCatalogAsync()
    {
        var code = Guid.NewGuid().ToString("N");
        var customer = Customer.Create($"Synthetic client {code}", null, null, null, null, null, Now);
        customer.SetExternalCode($"C-{code}", Now);
        customer.SetPilotDelivery("SQL-TEST", 1, true, "cash_on_delivery", "Synthetic delivery instruction", Now);
        var product = Product.Create($"Synthetic kg {code}", null);
        product.SetExternalCode($"P-{code}");
        product.SetUnitCode("kg");
        var second = Product.Create($"Synthetic package {code}", null);
        second.SetExternalCode($"Q-{code}");
        second.SetUnitCode("package");
        await using var db = CreateDbContext();
        db.Customers.Add(customer);
        db.Products.AddRange(product, second);
        await db.SaveChangesAsync();
        return new TestCatalog(customer.Id, customer.ExternalCode!, product.Id, product.ExternalCode!, second.Id, second.ExternalCode!);
    }

    public async Task DisposeAsync()
    {
        if (!_created) return;
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    public sealed record TestCatalog(Guid CustomerId, string CustomerCode, Guid ProductId, string ProductCode, Guid SecondProductId, string SecondProductCode);

    public sealed class SqlApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public SqlApiFactory(string connectionString)
        {
            _connectionString = connectionString;
            // The dedicated test project runs in its own process; no application-test env is shared.
            Environment.SetEnvironmentVariable("Persistence__Provider", "SqlServer");
            Environment.SetEnvironmentVariable("ConnectionStrings__Pedidos", connectionString);
            Environment.SetEnvironmentVariable("DevelopmentSeed__Enabled", "false");
            Environment.SetEnvironmentVariable("Authentication__Jwt__SigningKey", JwtSettings.DevelopmentSigningKey);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:Pedidos"] = _connectionString,
                ["DevelopmentSeed:Enabled"] = "false",
                ["Authentication:Jwt:SigningKey"] = JwtSettings.DevelopmentSigningKey
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDateTimeProvider>();
                services.AddSingleton<IDateTimeProvider, FixedClock>();
                // Business-date assertions are deterministic; JWT validity still uses the real clock.
                services.RemoveAll<IJwtTokenService>();
                services.AddSingleton<IJwtTokenService>(provider => new JwtTokenService(
                    provider.GetRequiredService<IConfiguration>(), new SystemDateTimeProvider()));
            });
        }
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset Now => SqlServerFixture.Now;
        public DateOnly Today => new(2026, 10, 24);
        public TimeOnly LocalTimeOfDay => new(8, 0);
    }
}
