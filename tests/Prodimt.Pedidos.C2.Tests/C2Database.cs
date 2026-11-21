using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Data.Common;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.C2.Tests;

// No connection is taken from api.env, the operational service or legacy test env vars.
public sealed record C2Configuration(string RunId, string DataSource, string ServerName,
    string AdminPassword, string Directory, string? WebRoot = null)
{
    public static C2Configuration Read(string file)
    {
        var full = Path.GetFullPath(file);
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(full) &
            (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
            throw new InvalidOperationException("c2_config_permissions_invalid");
        var value = JsonSerializer.Deserialize<C2Configuration>(File.ReadAllText(full), C2Json.Options)
            ?? throw new InvalidOperationException("c2_config_invalid");
        if (!Regex.IsMatch(value.RunId, "^[a-f0-9]{32}$") ||
            !Regex.IsMatch(value.DataSource, "^127\\.0\\.0\\.1,[0-9]{4,5}$") ||
            !int.TryParse(value.DataSource.Split(',')[1], out var port) || port is < 1024 or > 65535 || port == 1433 ||
            value.ServerName != "c2-" + value.RunId || value.AdminPassword.Length < 20 ||
            Path.GetFullPath(value.Directory) != Path.GetDirectoryName(full))
            throw new InvalidOperationException("c2_isolation_invalid");
        return value;
    }
}

public static class C2Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
}

public sealed class C2Clock : IDateTimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-04T18:00:00Z");
    public DateOnly Today => Prodimt.Pedidos.Application.CustomerOrders.DeliveryDatePolicy.LocalDate(Now);
    public TimeOnly LocalTimeOfDay => Prodimt.Pedidos.Application.CustomerOrders.DeliveryDatePolicy.LocalTime(Now);
}

public sealed class C2Database(C2Configuration configuration) : IAsyncDisposable
{
    public static string Password { get; } = Guid.NewGuid().ToString("N") + "Aa1!";
    public string Name { get; } = "C2_" + Guid.NewGuid().ToString("N");
    private string Login => "c2_app_" + Name[3..];
    private readonly string appPassword = Guid.NewGuid().ToString("N") + "Aa1!";
    private bool registered;
    public bool CleanupVerified { get; private set; }
    public C2Clock Clock { get; } = new();
    public string AdminConnection => Connection("master", "sa", configuration.AdminPassword);
    public string ApplicationConnection => Connection(Name, Login, appPassword);
    private string Connection(string database, string user, string password) => new SqlConnectionStringBuilder
    {
        DataSource = configuration.DataSource,
        InitialCatalog = database,
        UserID = user,
        Password = password,
        Encrypt = true,
        TrustServerCertificate = true,
        ConnectTimeout = 5,
        ApplicationName = "PRODIMT C2 isolated validation",
        PersistSecurityInfo = false
    }.ConnectionString;

    public PedidosDbContext Context(bool setup = false) => new(new DbContextOptionsBuilder<PedidosDbContext>()
        .UseSqlServer(setup ? Connection(Name, "sa", configuration.AdminPassword) : ApplicationConnection).Options);

    private async Task<SqlConnection> OpenMaster()
    {
        var connection = new SqlConnection(AdminConnection);
        await connection.OpenAsync();
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName'))";
        if (!string.Equals((string?)await check.ExecuteScalarAsync(), configuration.ServerName, StringComparison.OrdinalIgnoreCase))
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException("c2_server_identity_mismatch");
        }
        return connection;
    }

    public async Task InitializeAsync()
    {
        await using var master = await OpenMaster();
        // Persist before CREATE; the parent owns the entire UUID-labelled container
        // and can recover even if this process dies between CREATE and its response.
        using (var stream = new FileStream(Path.Combine(configuration.Directory, "databases.jsonl"),
            FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            var row = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { database = Name, owner = configuration.RunId, server = configuration.ServerName }) + "\n");
            stream.Write(row); stream.Flush(true); registered = true;
        }
        await using (var create = master.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE [{Name}]";
            await create.ExecuteNonQueryAsync();
        }
        await using (var db = Context(true))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_addextendedproperty @name=N'C2Owner', @value={configuration.RunId}");
            await db.Database.MigrateAsync();
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(21, migrations.Length);
            Assert.Equal("20261117145405_AddMachineWorkspace", migrations[^1]);
            var admin = AdminUser.Create("c2-admin", "Administración C2 sintética", new PasswordHashService().HashPassword(Password), Clock.Now);
            db.AdminUsers.Add(admin);
            db.SecurityUserRoles.Add(new() { UserId = admin.Id, RoleId = SecurityDefaults.AdministratorRoleId });
            db.SalesChannels.AddRange(new SalesChannel { Id = Guid.NewGuid(), Name = "Cliente C2", Type = SalesChannelType.Customer },
                new SalesChannel { Id = Guid.NewGuid(), Name = "Administración C2", Type = SalesChannelType.AdminManualCapture, IsInternal = true });
            await db.SaveChangesAsync();
        }
        await using (var login = master.CreateCommand())
        {
            // Both identifier and password are generated locally from GUIDs.
            login.CommandText = $"CREATE LOGIN [{Login}] WITH PASSWORD='{appPassword}', CHECK_POLICY=OFF";
            await login.ExecuteNonQueryAsync();
        }
        await SetupSql($"""
            CREATE USER [{Login}] FOR LOGIN [{Login}];
            ALTER ROLE db_datareader ADD MEMBER [{Login}];
            ALTER ROLE db_datawriter ADD MEMBER [{Login}];
            """);
        // Reproduce the installed broad base grants and the operational table DENYs.
        // AuditLogs deliberately remains 1111 (F24 is outside C2).
        foreach (var table in new[] { "CustomerProductPriceRevisions", "ProductConsolidationOperations",
            "CustomerProductSetupOrders", "ProductionBatches", "DeliveryEvents",
            "MachineTeamChanges", "MachineWorkers", "MachineProductionNotices", "ProductionLotHandoffs" })
            await SetupSql($"DENY UPDATE,DELETE ON dbo.[{table}] TO [{Login}]");
        foreach (var table in new[] { "CustomerProductPriceBooks", "CustomerProductSetupRequests", "ProductionAllocations",
            "DeliveryRoutes", "DeliveryRouteDayDrivers", "CustomerDeliveryLocations", "DeliveryTrips", "DeliveryAssignments",
            "RouteOrderSchedules", "MachineTeamTemplates" })
            await SetupSql($"DENY DELETE ON dbo.[{table}] TO [{Login}]");
        await SetupSql($"DENY UPDATE ON dbo.MachineOperators TO [{Login}]");
        await VerifyPermissions();
    }

    public async Task VerifyPermissions()
    {
        await using var db = Context();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT IS_SRVROLEMEMBER('sysadmin'), IS_ROLEMEMBER('db_owner'), HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CREATE TABLE')";
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            for (var i = 0; i < 3; i++) Assert.Equal(0, reader.GetInt32(i));
        }
        foreach (var (table, mask) in new[] { ("AuditLogs", "1111"), ("RouteOrderSchedules", "1110"),
            ("DeliveryEvents", "1100"), ("ProductionBatches", "1100"), ("DeliveryAssignments", "1110"), ("DeliveryRouteDayDrivers", "1110"),
            ("CustomerProductPriceRevisions", "1100"), ("Orders", "1111"), ("OrderOperations", "1111"), ("OrderAuditLogs", "1111"),
            ("MachineOperators", "1101"), ("MachineTeamTemplates", "1110"), ("MachineTeamChanges", "1100"),
            ("MachineWorkers", "1100"), ("MachineProductionNotices", "1100"), ("ProductionLotHandoffs", "1100") })
        {
            command.CommandText = "SELECT " + string.Join(',', new[] { "SELECT", "INSERT", "UPDATE", "DELETE" }
                .Select(op => $"HAS_PERMS_BY_NAME('dbo.{table}','OBJECT','{op}')"));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(mask, string.Concat(Enumerable.Range(0, 4).Select(reader.GetInt32)));
        }
    }

    public async Task SetupSql(string sql)
    {
        await using var db = Context(true);
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task<C2Scene> Seed(string? name = null, DateTimeOffset? expires = null)
    {
        await using var db = Context(true);
        var suffix = Guid.NewGuid().ToString("N");
        var customer = Customer.Create(name ?? "Cliente C2 " + suffix, null, null, null, null, null, Clock.Now);
        customer.SetPilotDelivery("C2-" + suffix[..12], 1, false, null, "Instrucción sintética", Clock.Now);
        customer.RecordOrderingProfileChange(true, Clock.Now);
        var product = Product.Create("Producto C2 " + suffix, null); product.SetUnitCode("kg");
        var token = "c2-synthetic-" + suffix;
        var access = CustomerAccessToken.Create(customer.Id, new CustomerAccessTokenHasher().HashToken(token), "C2", expires, Clock.Now);
        var driver = AdminUser.Create("driver-" + suffix, "Chofer C2 " + suffix, new PasswordHashService().HashPassword(Password), Clock.Now);
        var denied = AdminUser.Create("denied-" + suffix, "Sin permisos C2", new PasswordHashService().HashPassword(Password), Clock.Now);
        db.AddRange(customer, product, access, driver, denied);
        db.SecurityUserRoles.Add(new() { UserId = driver.Id, RoleId = Guid.Parse("22220000-0000-0000-0000-000000000001") });
        db.CustomerFrequentProducts.Add(new() { CustomerId = customer.Id, ProductId = product.Id, SortOrder = 1, InterpretationPosition = 1 });
        await db.SaveChangesAsync();
        return new(customer.Id, customer.Name, product.Id, product.Name, customer.RouteCode!, token, access.Id, driver.Id, driver.UserName, denied.UserName);
    }

    public C2Api Host(string? webRoot = null) => new(ApplicationConnection, Clock, webRoot ?? configuration.WebRoot);

    public async ValueTask DisposeAsync()
    {
        if (!registered) { CleanupVerified = true; return; }
        Assert.Matches("^C2_[a-f0-9]{32}$", Name);
        SqlConnection.ClearAllPools();
        await using var master = await OpenMaster();
        await using var exists = master.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@name";
        exists.Parameters.AddWithValue("@name", Name);
        if ((int)(await exists.ExecuteScalarAsync())! == 1)
        {
            // Refuse a database owned by another run, even on the managed server.
            await using var owned = Context(true);
            await owned.Database.OpenConnectionAsync();
            await using var check = owned.Database.GetDbConnection().CreateCommand();
            check.CommandText = "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name='C2Owner'";
            Assert.Equal(configuration.RunId, (string?)await check.ExecuteScalarAsync());
            await owned.Database.CloseConnectionAsync();
            SqlConnection.ClearAllPools();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]";
            await drop.ExecuteNonQueryAsync();
        }
        await using var dropLogin = master.CreateCommand();
        dropLogin.CommandText = $"IF SUSER_ID('{Login}') IS NOT NULL DROP LOGIN [{Login}]";
        await dropLogin.ExecuteNonQueryAsync();
        Assert.Equal(0, (int)(await exists.ExecuteScalarAsync())!);
        CleanupVerified = true;
    }
}

public sealed record C2Scene(Guid CustomerId, string CustomerName, Guid ProductId, string ProductName,
    string RouteCode, string Token, Guid TokenId, Guid DriverId, string DriverName, string DeniedName);

public sealed class C2Api : WebApplicationFactory<Program>
{
    private static readonly string SigningKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private static readonly string EncryptionKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public C2SqlFaults SqlFaults { get; } = new();
    private readonly string connection;
    private readonly C2Clock clock;
    private readonly string? webRoot;
    public C2Api(string connection, C2Clock clock, string? webRoot)
    {
        this.connection = connection; this.clock = clock; this.webRoot = webRoot;
        UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        if (webRoot is not null) builder.UseWebRoot(Path.GetFullPath(webRoot));
        var ownedConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "SqlServer",
            ["ConnectionStrings:Pedidos"] = connection,
            ["DevelopmentSeed:Enabled"] = "false",
            ["Hosting:UseHttpsRedirection"] = "false",
            ["Authentication:Jwt:SigningKey"] = SigningKey,
            ["CustomerAccessTokens:EncryptionKey"] = EncryptionKey,
            ["Logging:LogLevel:Default"] = "None"
        }).Build();
        builder.ConfigureAppConfiguration((_, config) => config.AddConfiguration(ownedConfiguration));
        builder.ConfigureServices(services =>
        {
            // Program registers Infrastructure before the minimal-host factory's
            // final configuration callback. Replace its captured DbContext options
            // and register the same SQL implementations against our owned config.
            services.RemoveAll<PedidosDbContext>();
            services.RemoveAll<DbContextOptions<PedidosDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PedidosDbContext>>();
            services.AddInfrastructure(ownedConfiguration);
            services.AddDbContext<PedidosDbContext>(options => options.AddInterceptors(SqlFaults));
            services.RemoveAll<IDateTimeProvider>(); services.AddSingleton<IDateTimeProvider>(clock);
            services.RemoveAll<IJwtTokenService>();
            services.AddSingleton<IJwtTokenService>(new JwtTokenService(ownedConfiguration, clock));
            // Real signed JWTs and real account/token validation, with the same injected
            // business clock for lifetime checks. No OS clock change or test endpoint.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(SigningKey));
                options.TokenValidationParameters.LifetimeValidator = (from, to, _, _) =>
                    from <= clock.Now.UtcDateTime && to > clock.Now.UtcDateTime;
            });
        });
    }

    public HttpClient Client()
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        using (var scope = Services.CreateScope())
        {
            var actualDatabase = scope.ServiceProvider.GetRequiredService<PedidosDbContext>();
            if (actualDatabase.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
                throw new InvalidOperationException("c2_api_provider_invalid");
            var actualConnection = new SqlConnectionStringBuilder(actualDatabase.Database.GetDbConnection().ConnectionString);
            var expectedConnection = new SqlConnectionStringBuilder(connection);
            if (actualConnection.DataSource != expectedConnection.DataSource)
                throw new InvalidOperationException("c2_api_server_binding_invalid");
            if (actualConnection.InitialCatalog != expectedConnection.InitialCatalog)
                throw new InvalidOperationException("c2_api_database_binding_invalid");
            if (actualConnection.UserID != expectedConnection.UserID || actualConnection.IntegratedSecurity)
                throw new InvalidOperationException("c2_api_identity_binding_invalid");
        }
        var address = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var actual = new Uri(address);
        if (actual.Host != "127.0.0.1" || actual.Port < 1024 || actual.Port == 5088)
            throw new InvalidOperationException("c2_api_binding_invalid");
        client.BaseAddress = actual;
        return client;
    }
    public async Task<HttpClient> Admin(string userName = "c2-admin")
    {
        var client = Client();
        using var response = await client.PostAsJsonAsync("/api/auth/admin/login", new { userName, password = C2Database.Password });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<AdminLoginResponse>(C2Json.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
    public async Task<HttpClient> Customer(C2Scene scene)
    {
        var client = Client();
        using var response = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = scene.Token });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>(C2Json.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
}

// Observes SQL error numbers without replacing commands, results or repositories.
public sealed class C2SqlFaults : DbCommandInterceptor
{
    public ConcurrentQueue<int> Numbers { get; } = new();
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        if (eventData.Exception is SqlException sql) Numbers.Enqueue(sql.Number);
    }
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        CommandFailed(command, eventData); return Task.CompletedTask;
    }
}
