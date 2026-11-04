using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public bool LegacyCaptureSchema { get; init; }
    // These credentials exist only in a newly created disposable test database.
    private const string AdminPassword = "Sql-test-only-admin-2026!";
    private readonly string _databaseName = $"ProdimtI02Test_{Guid.NewGuid():N}";
    // The portable deployment validator uses only this generated name to
    // independently verify cleanup after a lost CREATE or DROP response.
    public string DisposableDatabaseName => _databaseName;
    // The portable supervisor records every UUID before CREATE, including a
    // nested migration test, so timeout cleanup never relies on child finally.
    public static event Action<string>? DisposableDatabaseCreating;
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

        DisposableDatabaseCreating?.Invoke(_databaseName);
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
        if (LegacyCaptureSchema) await db.GetService<IMigrator>().MigrateAsync("20261028202031_AddCustomerCaptureAliases");
        else await db.Database.MigrateAsync();
        db.SalesChannels.Add(new SalesChannel
        {
            Id = SalesChannelId, Name = "SQL Test customer", Type = SalesChannelType.Customer
        });
        db.SalesChannels.Add(new SalesChannel
        {
            Id = Guid.NewGuid(), Name = "SQL Test admin", Type = SalesChannelType.AdminManualCapture, IsInternal = true
        });
        var administrator = AdminUser.Create("sql-test-admin", "SQL Test Admin", new PasswordHashService().HashPassword(AdminPassword), Now);
        db.AdminUsers.Add(administrator);
        if (!LegacyCaptureSchema)
            db.SecurityUserRoles.Add(new SecurityUserRole { UserId = administrator.Id,
                RoleId = Prodimt.Pedidos.Application.Security.SecurityDefaults.AdministratorRoleId });
        await db.SaveChangesAsync();
    }

    public PedidosDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PedidosDbContext>().UseSqlServer(ConnectionString);
        if (LegacyCaptureSchema) options.ReplaceService<IModelCustomizer, LegacyCaptureModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        return new(options.Options);
    }

    public SqlApiFactory CreateFactory() => new(ConnectionString, LegacyCaptureSchema);

    public static async Task<HttpClient> CreateAdminClientAsync(SqlApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsJsonAsync("/api/auth/admin/login", new { userName = "sql-test-admin", password = AdminPassword });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    public async Task<TestCatalog> CreateCatalogAsync(Func<PedidosDbContext>? contextFactory = null)
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
        await using var db = (contextFactory ?? CreateDbContext)();
        db.Customers.Add(customer);
        db.Products.AddRange(product, second);
        await db.SaveChangesAsync();
        return new TestCatalog(customer.Id, customer.ExternalCode!, product.Id, product.ExternalCode!, second.Id, second.ExternalCode!);
    }

    public enum HistoricalCatalogSchema { SharedProductsI11, RecoverableAccessI12 }

    public async Task<TestCatalog> CreateHistoricalCatalogAsync(HistoricalCatalogSchema schema)
    {
        var (migration, count) = schema switch
        {
            HistoricalCatalogSchema.SharedProductsI11 => ("20261101154618_AddSharedProductsAndCustomerPrices", 12),
            HistoricalCatalogSchema.RecoverableAccessI12 => ("20261101190000_AddRecoverableCustomerAccessTokens", 13),
            _ => throw new ArgumentOutOfRangeException(nameof(schema))
        };
        await using var db = CreateDbContext();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(count, applied.Length);
        Assert.Equal(migration, applied[^1]);
        var code = Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var customerCode = $"C-{code}";
        var productCode = $"P-{code}";
        var secondCode = $"Q-{code}";
        // These fixtures intentionally stop before later Customer schedule and
        // Product machine fields. Use only columns shared by the exact I11/I12 targets.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.Customers
                (Id,Name,PhoneNumber,IsActive,OrderingProfileVersion,IsOrderProfileApproved,RestDaysMask,
                 CreatedAt,UpdatedAt,ExternalCode,RouteCode,RouteStopOrder,HasStoreKey,PaymentTermCode,InternalDeliveryInstructions)
            VALUES
                ({customerId},{"Synthetic client " + code},N'',1,0,0,0,{Now},{Now},{customerCode},
                 N'SQL-TEST',1,1,N'cash_on_delivery',N'Synthetic delivery instruction');
            INSERT INTO dbo.Products (Id,Name,UnitCode,ExternalCode,IsActive)
            VALUES ({productId},{"Synthetic kg " + code},N'kg',{productCode},1),
                   ({secondId},{"Synthetic package " + code},N'package',{secondCode},1);
            """);
        await transaction.CommitAsync();
        return new TestCatalog(customerId, customerCode, productId, productCode, secondId, secondCode);
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
        private readonly bool _legacyCaptureSchema;

        public SqlApiFactory(string connectionString, bool legacyCaptureSchema = false)
        {
            _connectionString = connectionString;
            _legacyCaptureSchema = legacyCaptureSchema;
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
                ["Authentication:Jwt:SigningKey"] = JwtSettings.DevelopmentSigningKey,
                // Fictional test-only encryption key; production requires its own persisted secret.
                ["CustomerAccessTokens:EncryptionKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning"
            }));
            builder.ConfigureServices(services =>
            {
                if (_legacyCaptureSchema)
                {
                    services.AddDbContext<PedidosDbContext>(options => options
                        .ReplaceService<IModelCustomizer, LegacyCaptureModelCustomizer>()
                        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
                    services.RemoveAll<ICommercialCatalogRepository>();
                    services.AddScoped<ICommercialCatalogRepository, LegacyCommercialCatalogRepository>();
                    services.RemoveAll<Prodimt.Pedidos.Application.Security.ISecurityStore>();
                    services.AddScoped<Prodimt.Pedidos.Application.Security.ISecurityStore, LegacySecurityStore>();
                    services.RemoveAll<ICustomerProductSetupRepository>();
                    services.AddScoped<ICustomerProductSetupRepository, LegacyCustomerProductSetupRepository>();
                    services.RemoveAll<IOrderingScheduleRepository>();
                    services.AddScoped<IOrderingScheduleRepository, LegacyOrderingScheduleRepository>();
                }
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
