using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class CustomerChannelRepairSqlTests : IAsyncLifetime
{
    private readonly SqlServerFixture database = new() { LegacyCaptureSchema = true };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task MissingCustomerChannel_LimitedDmlRepairIsAtomicIdempotentAndPortalCanSave()
    {
        await Execute("DELETE FROM dbo.SalesChannels WHERE Type=N'Customer';");
        var catalog = await database.CreateCatalogAsync();
        var sql = await RepairSql();
        await Execute("CREATE USER [customer_channel_repair_test] WITHOUT LOGIN;\n" + string.Join("\n",
            new[] { "__EFMigrationsHistory", "Orders", "OrderOperations", "Customers", "Products",
                    "CustomerFrequentProducts", "SalesChannels", "AuditLogs" }
                .Select(table => $"GRANT SELECT ON dbo.[{table}] TO [customer_channel_repair_test];")) +
            "\nGRANT INSERT ON dbo.SalesChannels TO [customer_channel_repair_test];" +
            " GRANT INSERT ON dbo.AuditLogs TO [customer_channel_repair_test];");
        using (var receipt = JsonDocument.Parse(await Scalar("EXECUTE AS USER=N'customer_channel_repair_test';\n" + sql + "\nREVERT;")))
        {
            Assert.Equal(1, receipt.RootElement.GetProperty("created").GetInt32());
            Assert.Equal(1, receipt.RootElement.GetProperty("audit_inserted").GetInt32());
            Assert.Equal(1, receipt.RootElement.GetProperty("existing_data_counts_preserved").GetInt32());
        }
        await Execute("UPDATE dbo.SalesChannels SET Name=N'Synthetic customized customer channel' WHERE Type=N'Customer';");
        using (var repeated = JsonDocument.Parse(await Scalar(sql)))
            Assert.Equal(0, repeated.RootElement.GetProperty("created").GetInt32());
        Guid repairedChannelId;
        var plain = "synthetic-customer-channel-" + Guid.NewGuid().ToString("N");
        await using (var db = database.CreateDbContext())
        {
            var channel = await db.SalesChannels.SingleAsync(x => x.Type == SalesChannelType.Customer);
            repairedChannelId = channel.Id;
            Assert.False(channel.IsInternal);
            Assert.Equal("Synthetic customized customer channel", channel.Name);
            Assert.Single(await db.SalesChannels.Where(x => x.Type == SalesChannelType.AdminManualCapture).ToArrayAsync());
            var audit = Assert.Single(await db.AuditLogs.Where(x => x.EventType == "CustomerSalesChannelRepaired").ToArrayAsync());
            Assert.Equal(AuditActorType.System, audit.ActorType);
            Assert.Empty(await db.Orders.ToArrayAsync());
            Assert.Empty(await db.OrderOperations.ToArrayAsync());
            Assert.Single(await db.Customers.ToArrayAsync());
            Assert.Equal(2, await db.Products.CountAsync());
            Assert.Single(await db.AdminUsers.ToArrayAsync());
            var customer = await db.Customers.SingleAsync(x => x.Id == catalog.CustomerId);
            customer.RecordOrderingProfileChange(true, SqlServerFixture.Now);
            db.CustomerFrequentProducts.Add(new CustomerFrequentProduct
                { CustomerId = customer.Id, ProductId = catalog.ProductId, SortOrder = 1, InterpretationPosition = 1 });
            db.CustomerAccessTokens.Add(CustomerAccessToken.Create(customer.Id,
                new CustomerAccessTokenHasher().HashToken(plain), "Synthetic repair verification",
                DateTimeOffset.UtcNow.AddHours(6), SqlServerFixture.Now));
            await db.SaveChangesAsync();
        }
        await using var host = database.CreateFactory();
        using var client = host.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = plain });
        loginResponse.EnsureSuccessStatusCode();
        var login = (await loginResponse.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var url = $"/api/customer-orders/{catalog.CustomerId}";
        var next = (await client.GetFromJsonAsync<CustomerNextOrderResponse>(url + "/next", JsonOptions))!;
        var request = new SubmitCustomerOrderCaptureRequest(Guid.NewGuid(), next.ProfileVersion, next.DeliveryDate,
            Lines: [new(catalog.ProductId, 8, "kg")]);
        using var savedResponse = await client.PostAsJsonAsync(url + "/capture", request);
        savedResponse.EnsureSuccessStatusCode();
        using var replayResponse = await client.PostAsJsonAsync(url + "/capture", request);
        replayResponse.EnsureSuccessStatusCode();
        var replay = (await replayResponse.Content.ReadFromJsonAsync<SubmitCustomerOrderCaptureResponse>(JsonOptions))!;
        Assert.True(replay.Replayed);
        await using var persisted = database.CreateDbContext();
        var order = Assert.Single(await persisted.Orders.ToArrayAsync());
        Assert.Equal(repairedChannelId, order.SalesChannelId);
        Assert.Single(await persisted.OrderOperations.ToArrayAsync());
    }

    [Fact]
    public async Task AuditFailure_RollsBackCustomerChannelAndRetryCreatesOneAudit()
    {
        await Execute("DELETE FROM dbo.SalesChannels WHERE Type=N'Customer';");
        var sql = await RepairSql();
        await Execute("ALTER TABLE dbo.AuditLogs ADD CONSTRAINT CK_CustomerChannelRepairTest CHECK (EventType<>N'CustomerSalesChannelRepaired');");
        await Assert.ThrowsAsync<SqlException>(() => Scalar(sql));
        await using (var db = database.CreateDbContext())
        {
            Assert.False(await db.SalesChannels.AnyAsync(x => x.Type == SalesChannelType.Customer));
            Assert.Empty(await db.AuditLogs.ToArrayAsync());
        }
        await Execute("ALTER TABLE dbo.AuditLogs DROP CONSTRAINT CK_CustomerChannelRepairTest;");
        using var repaired = JsonDocument.Parse(await Scalar(sql));
        Assert.Equal(1, repaired.RootElement.GetProperty("created").GetInt32());
        using var repeated = JsonDocument.Parse(await Scalar(sql));
        Assert.Equal(0, repeated.RootElement.GetProperty("created").GetInt32());
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.SalesChannels.Where(x => x.Type == SalesChannelType.Customer).ToArrayAsync());
        Assert.Single(await verify.AuditLogs.Where(x => x.EventType == "CustomerSalesChannelRepaired").ToArrayAsync());
    }

    [Theory]
    [InlineData(true, "Synthetic incompatible customer channel")]
    [InlineData(false, "")]
    public async Task ExistingInvalidCustomerChannel_IsRejectedWithoutOverwrite(bool isInternal, string name)
    {
        Guid channelId;
        await using (var db = database.CreateDbContext())
        {
            var channel = await db.SalesChannels.SingleAsync(x => x.Type == SalesChannelType.Customer);
            channelId = channel.Id;
            channel.IsInternal = isInternal;
            channel.Name = name;
            await db.SaveChangesAsync();
        }
        var sql = await RepairSql();
        await Assert.ThrowsAsync<SqlException>(() => Scalar(sql));
        await using var verify = database.CreateDbContext();
        var unchanged = await verify.SalesChannels.SingleAsync(x => x.Type == SalesChannelType.Customer);
        Assert.Equal(channelId, unchanged.Id);
        Assert.Equal(isInternal, unchanged.IsInternal);
        Assert.Equal(name, unchanged.Name);
        Assert.Empty(await verify.AuditLogs.ToArrayAsync());
        Assert.Empty(await verify.Orders.ToArrayAsync());
    }

    private async Task Execute(string sql)
    {
        await using var db = database.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<string> Scalar(string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> RepairSql()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        const string relative = "scripts/pilot/repair-native-customer-channel.py";
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import runpy,sys;m=runpy.run_path(sys.argv[1]);sys.stdout.write(m['OPTIONS']+m['REPAIR_SQL'])");
        start.ArgumentList.Add(Path.Combine(root!.FullName, relative));
        using var process = Process.Start(start)!;
        var sql = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        const string guard = "DB_NAME()<>N'ProdimtPedidos'";
        Assert.Contains(guard, sql);
        var target = new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;
        Assert.StartsWith("ProdimtI02Test_", target);
        return sql.Replace(guard, $"DB_NAME()<>N'{target}'", StringComparison.Ordinal);
    }
}
