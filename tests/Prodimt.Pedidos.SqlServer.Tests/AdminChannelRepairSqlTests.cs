using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class AdminChannelRepairSqlTests : IAsyncLifetime
{
    // A separate disposable database prevents removing the shared test channel
    // already referenced by other SQL integration tests.
    private readonly SqlServerFixture database = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task MissingChannel_FailsBeforeOrder_RepairWithLimitedDml_ThenSameAttemptSavesAndReplays()
    {
        await RemoveAdminChannel();
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        using var profile = await client.PutAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}/frequent-products",
            new UpdateCustomerFrequentProductsRequest([
                new(catalog.ProductId,null,1,true,InterpretationPosition:1,ExpectedUnitCode:"kg"),
                new(catalog.SecondProductId,null,2,true,InterpretationPosition:2,ExpectedUnitCode:"package")],true,0));
        profile.EnsureSuccessStatusCode();
        var url = $"/api/admin/customers/{catalog.CustomerId}/orders";
        using var previewResponse = await client.PostAsJsonAsync(url+"/preview", new PreviewOrderCaptureRequest("13+7",new(2026,10,25)));
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PreviewOrderCaptureResponse>())!;
        var request = new SubmitOrderCaptureRequest(Guid.NewGuid(), preview.Profile.OrderingProfileVersion,
            preview.ServiceDate, preview.Text, preview.Fingerprint);
        HttpResponseMessage? failureResponse = null;
        Exception? failure = null;
        try { failureResponse = await client.PostAsJsonAsync(url+"/capture", request); }
        catch (Exception ex) { failure = ex; }
        using (failureResponse) Assert.True(failure is not null || failureResponse?.StatusCode == HttpStatusCode.InternalServerError);
        await using (var db = database.CreateDbContext())
        {
            Assert.Empty(await db.Orders.ToArrayAsync());
            Assert.Empty(await db.OrderOperations.ToArrayAsync());
            Assert.Empty(await db.OrderAuditLogs.ToArrayAsync());
        }
        var sql = await RepairScript();
        var grants = "CREATE USER [channel_repair_test] WITHOUT LOGIN;\n" + string.Join("\n",
            new[] {"__EFMigrationsHistory","Orders","OrderOperations","Customers","Products","CustomerFrequentProducts","SalesChannels","AuditLogs"}
                .Select(table => $"GRANT SELECT ON dbo.[{table}] TO [channel_repair_test];")) +
            "\nGRANT INSERT ON dbo.SalesChannels TO [channel_repair_test]; GRANT INSERT ON dbo.AuditLogs TO [channel_repair_test];";
        await Execute(grants);
        using (var repaired = JsonDocument.Parse(await Scalar("EXECUTE AS USER=N'channel_repair_test';\n" + sql + "\nREVERT;")))
        {
            Assert.Equal(1,repaired.RootElement.GetProperty("created").GetInt32());
            Assert.Equal(1,repaired.RootElement.GetProperty("existing_data_counts_preserved").GetInt32());
        }
        await Execute("UPDATE dbo.SalesChannels SET Name=N'Synthetic customized channel' WHERE Type=N'AdminManualCapture';");
        using (var repeated = JsonDocument.Parse(await Scalar(sql)))
            Assert.Equal(0,repeated.RootElement.GetProperty("created").GetInt32());
        await using (var db = database.CreateDbContext())
        {
            var channel = await db.SalesChannels.SingleAsync(x=>x.Type==SalesChannelType.AdminManualCapture);
            Assert.Equal("Synthetic customized channel",channel.Name);
            Assert.True(channel.IsInternal);
            Assert.Equal(2,await db.SalesChannels.CountAsync());
            Assert.Single(await db.AuditLogs.Where(x=>x.EventType=="AdminSalesChannelRepaired").ToArrayAsync());
            Assert.Empty(await db.Orders.ToArrayAsync());
            Assert.Equal(1,await db.Customers.CountAsync());
            Assert.Equal(2,await db.Products.CountAsync());
            Assert.Equal(2,await db.CustomerFrequentProducts.CountAsync());
            Assert.Equal(1,await db.AdminUsers.CountAsync());
        }
        using var savedResponse = await client.PostAsJsonAsync(url+"/capture",request);
        savedResponse.EnsureSuccessStatusCode();
        var saved = (await savedResponse.Content.ReadFromJsonAsync<SubmitOrderCaptureResponse>(JsonOptions))!;
        Assert.False(saved.Replayed);
        using var replayResponse = await client.PostAsJsonAsync(url+"/capture",request);
        replayResponse.EnsureSuccessStatusCode();
        var replay = (await replayResponse.Content.ReadFromJsonAsync<SubmitOrderCaptureResponse>(JsonOptions))!;
        Assert.True(replay.Replayed);
        Assert.Equal(saved.Order.OrderId,replay.Order.OrderId);
        await using var persisted = database.CreateDbContext();
        Assert.Single(await persisted.Orders.ToArrayAsync());
        Assert.Single(await persisted.OrderOperations.ToArrayAsync());
        Assert.Equal(2,await persisted.OrderLines.CountAsync());
    }

    [Fact]
    public async Task AuditFailure_RollsBackChannel_AndRetryAddsExactlyOneChannelAndAudit()
    {
        await RemoveAdminChannel();
        var sql = await RepairScript();
        await Execute("ALTER TABLE dbo.AuditLogs ADD CONSTRAINT CK_ChannelRepairTest CHECK (EventType<>N'AdminSalesChannelRepaired');");
        await Assert.ThrowsAsync<SqlException>(()=>Scalar(sql));
        await using (var db = database.CreateDbContext())
        {
            Assert.False(await db.SalesChannels.AnyAsync(x=>x.Type==SalesChannelType.AdminManualCapture));
            Assert.Empty(await db.AuditLogs.ToArrayAsync());
            Assert.Empty(await db.Orders.ToArrayAsync());
        }
        await Execute("ALTER TABLE dbo.AuditLogs DROP CONSTRAINT CK_ChannelRepairTest;");
        using var repaired = JsonDocument.Parse(await Scalar(sql));
        Assert.Equal(1,repaired.RootElement.GetProperty("created").GetInt32());
        using var repeated = JsonDocument.Parse(await Scalar(sql));
        Assert.Equal(0,repeated.RootElement.GetProperty("created").GetInt32());
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.SalesChannels.Where(x=>x.Type==SalesChannelType.AdminManualCapture).ToArrayAsync());
        Assert.Single(await verify.AuditLogs.ToArrayAsync());
    }

    private async Task RemoveAdminChannel()
        => await Execute("DELETE FROM dbo.SalesChannels WHERE Type=N'AdminManualCapture';");

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
        command.CommandText=sql;
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> RepairScript()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"scripts/pilot/repair-native-admin-channel.py"))) root=root.Parent;
        Assert.NotNull(root);
        var start=new ProcessStartInfo("python3") { RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import runpy,sys;m=runpy.run_path(sys.argv[1]);sys.stdout.write(m['OPTIONS']+m['REPAIR_SQL'])");
        start.ArgumentList.Add(Path.Combine(root!.FullName,"scripts/pilot/repair-native-admin-channel.py"));
        using var process=Process.Start(start)!;
        var sql=await process.StandardOutput.ReadToEndAsync();
        var error=await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode==0,error);
        const string guard="DB_NAME()<>N'ProdimtPedidos'";
        Assert.Contains(guard,sql);
        var target=new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;
        // This fixture generates the disposable database name; it never targets
        // the production database or reads operational credentials.
        Assert.StartsWith("ProdimtI02Test_",target);
        return sql.Replace(guard,$"DB_NAME()<>N'{target}'",StringComparison.Ordinal);
    }
}
