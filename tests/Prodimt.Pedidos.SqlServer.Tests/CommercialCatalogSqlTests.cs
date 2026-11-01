using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class CommercialCatalogSqlTests(SqlServerFixture database)
{
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private static ChangeCustomerProductPriceRequest Change(decimal? amount, int version = 0, Guid? operation = null)
        => new(amount, Day, "Precio ficticio confirmado", version, operation ?? Guid.NewGuid(), "kg");
    private static string Prices(Guid customer) => $"/api/admin/customers/{customer}/prices";
    private static string Price(Guid customer, Guid product) => $"/api/admin/customers/{customer}/products/{product}/prices";

    private static async Task Configure(HttpClient client, Guid customerId, Guid productId)
    {
        var customer = (await client.GetFromJsonAsync<AdminCustomerResponse>($"/api/admin/customers/{customerId}"))!;
        var request = new UpdateCustomerFrequentProductsRequest(
            [new(productId, null, 1, true, null, 1, "kg", "PP")], true, customer.OrderingProfileVersion);
        (await client.PutAsJsonAsync($"/api/admin/customers/{customerId}/frequent-products", request)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task TwoHostsRetrySamePriceOnce_AndConcurrentEditsRejectTheStaleVersion()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var host1 = database.CreateFactory(); await using var host2 = database.CreateFactory();
        using var first = await SqlServerFixture.CreateAdminClientAsync(host1);
        using var second = await SqlServerFixture.CreateAdminClientAsync(host2);
        await Configure(first, catalog.CustomerId, catalog.ProductId);
        var request = Change(20);
        var replies = await Task.WhenAll(first.PostAsJsonAsync(Price(catalog.CustomerId, catalog.ProductId), request),
            second.PostAsJsonAsync(Price(catalog.CustomerId, catalog.ProductId), request));
        foreach (var reply in replies) Assert.Contains(reply.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        (await second.PostAsJsonAsync(Price(catalog.CustomerId, catalog.ProductId), request)).EnsureSuccessStatusCode();
        var saved = (await second.GetFromJsonAsync<CustomerProductPriceResponse[]>(Prices(catalog.CustomerId)))!.Single();
        Assert.Equal(1, saved.Version); Assert.Single(saved.History);
        Assert.True(Guid.TryParse(saved.History[0].ActorId, out _));
        Assert.Equal("SQL Test Admin", saved.History[0].ActorDisplayName);
        var competing = await Task.WhenAll(first.PostAsJsonAsync(Price(catalog.CustomerId, catalog.ProductId), Change(21, 1)),
            second.PostAsJsonAsync(Price(catalog.CustomerId, catalog.ProductId), Change(22, 1)));
        Assert.Single(competing, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(competing, x => x.StatusCode == HttpStatusCode.Conflict);
        var final = (await first.GetFromJsonAsync<CustomerProductPriceResponse[]>(Prices(catalog.CustomerId)))!.Single();
        Assert.Equal(2, final.Version); Assert.Equal(2, final.History.Count);
        Assert.Equal(20, final.History.Single(x => x.Version == 1).UnitPrice);
    }

    [Fact]
    public async Task AdministrativePricesAndConsolidationAreNotAvailableToCustomers()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var host = database.CreateFactory();
        using var anonymous = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Prices(catalog.CustomerId))).StatusCode);
        var tokenId = Guid.NewGuid(); var plain = "synthetic-commercial-" + Guid.NewGuid();
        await using (var db = database.CreateDbContext())
        {
            db.CustomerAccessTokens.Add(new CustomerAccessToken { Id = tokenId, CustomerId = catalog.CustomerId,
                TokenHash = new CustomerAccessTokenHasher().HashToken(plain), DisplayName = "Synthetic test access",
                IsActive = true, CreatedAt = SqlServerFixture.Now });
            await db.SaveChangesAsync();
        }
        using var login = await anonymous.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest(plain));
        login.EnsureSuccessStatusCode();
        var identity = (await login.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", identity.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(Prices(catalog.CustomerId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync(Price(catalog.CustomerId, catalog.ProductId), Change(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("/api/admin/products/consolidation/candidates")).StatusCode);
    }

    [Fact]
    public async Task CapturedPricePersistsAcrossHostsAndLaterChangesDoNotRepriceTheOrder()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var firstHost = database.CreateFactory();
        using var first = await SqlServerFixture.CreateAdminClientAsync(firstHost);
        await Configure(first, catalog.CustomerId, catalog.ProductId);
        (await first.PostAsJsonAsync(Price(catalog.CustomerId, catalog.ProductId), Change(12.3456m))).EnsureSuccessStatusCode();
        var response = await first.PostAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}/orders/submit",
            new AdminSubmitCustomerOrderRequest([new(catalog.ProductId, 3, null)], null, null, null, null, null, Day));
        response.EnsureSuccessStatusCode();
        var order = (await response.Content.ReadFromJsonAsync<AdminOrderSummaryResponse>(JsonOptions))!;
        (await first.PostAsJsonAsync(Price(catalog.CustomerId, catalog.ProductId), Change(20, 1))).EnsureSuccessStatusCode();
        await using var freshHost = database.CreateFactory();
        using var fresh = await SqlServerFixture.CreateAdminClientAsync(freshHost);
        var detail = (await fresh.GetFromJsonAsync<AdminOrderDetailResponse>($"/api/admin/orders/{order.OrderId}", JsonOptions))!;
        Assert.Equal(12.3456m, detail.Lines.Single().UnitPrice);
        Assert.Equal(37.04m, detail.TotalAmount); Assert.False(detail.HasUnpricedLines);
        Assert.NotNull(detail.Lines.Single().PriceRevisionId);
        await using var db = database.CreateDbContext();
        Assert.Equal(12.3456m, (await db.OrderLines.SingleAsync(x => x.OrderId == order.OrderId)).UnitPriceSnapshot);
    }

    [Fact]
    public async Task PriceAndItsAuditRollbackTogetherWhenSqlAuditInsertFails()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var host = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(host);
        await Configure(client, catalog.CustomerId, catalog.ProductId);
        await using var setup = database.CreateDbContext();
        await setup.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.AuditLogs WITH NOCHECK ADD CONSTRAINT CK_I10_PriceAuditFailure CHECK (EventType <> N'CustomerProductPriceChanged');");
        try
        {
            await using var context = database.CreateDbContext();
            var service = new CustomerProductPricingService(new EfCustomerRepository(context), new EfProductRepository(context),
                new EfCommercialCatalogRepository(context), new EfAuditLogRepository(context), new TestClock());
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ChangeAsync(catalog.CustomerId, catalog.ProductId,
                Change(20), new("synthetic-actor", "Synthetic admin"), Ct));
            await using var verify = database.CreateDbContext();
            Assert.False(await verify.CustomerProductPriceBooks.AnyAsync(x => x.CustomerId == catalog.CustomerId));
            Assert.False(await verify.CustomerProductPriceRevisions.AnyAsync(x => x.Reason == "Precio ficticio confirmado"
                && verify.CustomerProductPriceBooks.Any(b => b.Id == x.PriceBookId && b.CustomerId == catalog.CustomerId)));
        }
        finally { await setup.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.AuditLogs DROP CONSTRAINT CK_I10_PriceAuditFailure;"); }
    }

    [Fact]
    public async Task MaximumSpanishReasonPersistsInPriceHistoryAndConsolidationReceipt()
    {
        var a = await database.CreateCatalogAsync(); var b = await database.CreateCatalogAsync();
        await using var host = database.CreateFactory(); using var client = await SqlServerFixture.CreateAdminClientAsync(host);
        await Configure(client, a.CustomerId, a.ProductId); await Configure(client, b.CustomerId, b.ProductId);
        var reason = new string('á', 500);
        (await client.PostAsJsonAsync(Price(a.CustomerId, a.ProductId), Change(20) with { Reason = reason })).EnsureSuccessStatusCode();
        var selection = new PreviewProductConsolidationRequest(a.ProductId, [b.ProductId], "Shared unicode test " + Guid.NewGuid().ToString("N"));
        var preview = (await (await client.PostAsJsonAsync("/api/admin/products/consolidation/preview", selection))
            .Content.ReadFromJsonAsync<ProductConsolidationPreview>())!;
        Assert.True(preview.CanApply);
        var request = new ApplyProductConsolidationRequest(a.ProductId, [b.ProductId], selection.SharedName, null,
            preview.Fingerprint, reason, Guid.NewGuid());
        (await client.PostAsJsonAsync("/api/admin/products/consolidation/apply", request)).EnsureSuccessStatusCode();
        await using var db = database.CreateDbContext();
        var book = await db.CustomerProductPriceBooks.SingleAsync(x => x.CustomerId == a.CustomerId && x.ProductId == a.ProductId);
        Assert.Equal(reason, (await db.CustomerProductPriceRevisions.SingleAsync(x => x.PriceBookId == book.Id)).Reason);
        var receipt = await db.ProductConsolidationOperations.SingleAsync(x => x.Id == request.OperationId);
        using var json = JsonDocument.Parse(receipt.ResultJson);
        Assert.Equal(reason, json.RootElement.GetProperty("reason").GetString());
        var ids = new[] { book.Id.ToString(), request.OperationId.ToString() };
        var audits = await db.AuditLogs.Where(x => ids.Contains(x.EntityId)).ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, x => Assert.InRange(x.MetadataJson!.Length, 1, 2000));
    }

    [Fact]
    public async Task ConsolidationMovesProfilesAndPriceAssignmentsAtomicallyAndReplaysAcrossHosts()
    {
        var a = await database.CreateCatalogAsync(); var b = await database.CreateCatalogAsync();
        await using var host = database.CreateFactory(); using var client = await SqlServerFixture.CreateAdminClientAsync(host);
        await Configure(client, a.CustomerId, a.ProductId); await Configure(client, b.CustomerId, b.ProductId);
        (await client.PostAsJsonAsync(Price(a.CustomerId, a.ProductId), Change(20))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Price(b.CustomerId, b.ProductId), Change(25))).EnsureSuccessStatusCode();
        var name = "Shared synthetic " + Guid.NewGuid().ToString("N");
        var selection = new PreviewProductConsolidationRequest(a.ProductId, [b.ProductId], name);
        var preview = (await (await client.PostAsJsonAsync("/api/admin/products/consolidation/preview", selection))
            .Content.ReadFromJsonAsync<ProductConsolidationPreview>())!;
        Assert.True(preview.CanApply);
        var request = new ApplyProductConsolidationRequest(a.ProductId, [b.ProductId], name, null,
            preview.Fingerprint, "Confirmed synthetic equivalence", Guid.NewGuid());
        (await client.PostAsJsonAsync("/api/admin/products/consolidation/apply", request)).EnsureSuccessStatusCode();
        await using var freshHost = database.CreateFactory(); using var fresh = await SqlServerFixture.CreateAdminClientAsync(freshHost);
        var replay = await fresh.PostAsJsonAsync("/api/admin/products/consolidation/apply", request);
        replay.EnsureSuccessStatusCode(); Assert.True((await replay.Content.ReadFromJsonAsync<ProductConsolidationResponse>())!.Replayed);
        var price = (await fresh.GetFromJsonAsync<CustomerProductPriceResponse[]>(Prices(b.CustomerId)))!.Single();
        Assert.Equal(a.ProductId, price.ProductId); Assert.Equal(25, price.UnitPrice); Assert.Single(price.History);
        await using var db = database.CreateDbContext();
        Assert.Equal(a.ProductId, (await db.CustomerFrequentProducts.SingleAsync(x => x.CustomerId == b.CustomerId)).ProductId);
        Assert.Equal(a.ProductId, (await db.Products.SingleAsync(x => x.Id == b.ProductId)).MergedIntoProductId);
        Assert.Single(await db.ProductConsolidationOperations.Where(x => x.Id == request.OperationId).ToArrayAsync());
    }

    [Fact]
    public async Task ConsolidationAuditFailureRollsBackDeletedProfilesProductChangesPricesAndReceipt()
    {
        var a = await database.CreateCatalogAsync(); var b = await database.CreateCatalogAsync();
        await using var host = database.CreateFactory(); using var client = await SqlServerFixture.CreateAdminClientAsync(host);
        await Configure(client, a.CustomerId, a.ProductId); await Configure(client, b.CustomerId, b.ProductId);
        (await client.PostAsJsonAsync(Price(b.CustomerId, b.ProductId), Change(25))).EnsureSuccessStatusCode();
        var name = "Rollback synthetic " + Guid.NewGuid().ToString("N");
        var selection = new PreviewProductConsolidationRequest(a.ProductId, [b.ProductId], name);
        var preview = (await (await client.PostAsJsonAsync("/api/admin/products/consolidation/preview", selection))
            .Content.ReadFromJsonAsync<ProductConsolidationPreview>())!;
        var request = new ApplyProductConsolidationRequest(a.ProductId, [b.ProductId], name, null,
            preview.Fingerprint, "Synthetic rollback test", Guid.NewGuid());
        await using var setup = database.CreateDbContext();
        var originalVersion = (await setup.Customers.AsNoTracking().SingleAsync(x => x.Id == b.CustomerId)).OrderingProfileVersion;
        var originalName = (await setup.Products.AsNoTracking().SingleAsync(x => x.Id == a.ProductId)).Name;
        await setup.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.AuditLogs WITH NOCHECK ADD CONSTRAINT CK_I10_MergeAuditFailure CHECK (EventType <> N'ProductsConsolidated');");
        try
        {
            await using var context = database.CreateDbContext();
            var merge = new ProductConsolidationService(new EfProductRepository(context), new EfCustomerRepository(context),
                new EfCommercialCatalogRepository(context), new EfAuditLogRepository(context), new TestClock());
            await Assert.ThrowsAsync<DbUpdateException>(() => merge.ApplyAsync(request, new("synthetic-actor", "Synthetic admin"), Ct));
            await using var verify = database.CreateDbContext();
            Assert.Equal(b.ProductId, (await verify.CustomerFrequentProducts.SingleAsync(x => x.CustomerId == b.CustomerId)).ProductId);
            Assert.Equal(b.ProductId, (await verify.CustomerProductPriceBooks.SingleAsync(x => x.CustomerId == b.CustomerId)).ProductId);
            Assert.True((await verify.Products.SingleAsync(x => x.Id == b.ProductId)).IsActive);
            Assert.Null((await verify.Products.SingleAsync(x => x.Id == b.ProductId)).MergedIntoProductId);
            Assert.Equal(originalName, (await verify.Products.SingleAsync(x => x.Id == a.ProductId)).Name);
            Assert.Equal(originalVersion, (await verify.Customers.SingleAsync(x => x.Id == b.CustomerId)).OrderingProfileVersion);
            Assert.False(await verify.ProductConsolidationOperations.AnyAsync(x => x.Id == request.OperationId));
        }
        finally { await setup.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.AuditLogs DROP CONSTRAINT CK_I10_MergeAuditFailure;"); }
    }

    // Only this fixture's GUID-based principal is embedded; SQL identifiers cannot be parameters.
#pragma warning disable EF1002
    [Fact]
    public async Task NativeInstallerRecognizesTheActualSchemaAndRestrictsHistoryEvenForAWriterRole()
    {
        var contract = await NativeContract();
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = contract.GetProperty("schema").GetString();
        var schema = JsonSerializer.Deserialize<Dictionary<string, int>>((string)(await command.ExecuteScalarAsync())!)!;
        var expected = JsonSerializer.Deserialize<Dictionary<string, int>>(contract.GetProperty("expectedSchema"))!;
        Assert.Equal(expected.OrderBy(x => x.Key), schema.OrderBy(x => x.Key));
        command.CommandText = "CREATE USER [prodimt_pedidos_app] WITHOUT LOGIN; ALTER ROLE [db_datareader] ADD MEMBER [prodimt_pedidos_app]; ALTER ROLE [db_datawriter] ADD MEMBER [prodimt_pedidos_app];";
        await command.ExecuteNonQueryAsync();
        try
        {
            command.CommandText = contract.GetProperty("grants").GetString();
            await command.ExecuteNonQueryAsync();
            command.CommandText = "EXECUTE AS USER=N'prodimt_pedidos_app'";
            await command.ExecuteNonQueryAsync();
            try
            {
                command.CommandText = contract.GetProperty("permissions").GetString();
                var permissions = JsonSerializer.Deserialize<Dictionary<string, int>>((string)(await command.ExecuteScalarAsync())!)!;
                var expectedPermissions = JsonSerializer.Deserialize<Dictionary<string, int>>(contract.GetProperty("expectedPermissions"))!;
                Assert.Equal(expectedPermissions.OrderBy(x => x.Key), permissions.OrderBy(x => x.Key));
                command.CommandText = contract.GetProperty("schema").GetString();
                var applicationSchema = JsonSerializer.Deserialize<Dictionary<string, int>>((string)(await command.ExecuteScalarAsync())!)!;
                Assert.Equal(expected.OrderBy(x => x.Key), applicationSchema.OrderBy(x => x.Key));
            }
            finally { command.CommandText = "REVERT"; await command.ExecuteNonQueryAsync(); }
        }
        finally { command.CommandText = "DROP USER [prodimt_pedidos_app]"; await command.ExecuteNonQueryAsync(); }
    }

    [Fact]
    public async Task RestrictedWriterCanAppendPriceRevisionsButCannotRewriteOrDeleteTheirHistory()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var host = database.CreateFactory(); using var client = await SqlServerFixture.CreateAdminClientAsync(host);
        await Configure(client, catalog.CustomerId, catalog.ProductId);
        var user = "i10_writer_" + Guid.NewGuid().ToString("N");
        await using var setup = database.CreateDbContext();
        await setup.Database.ExecuteSqlRawAsync($"CREATE USER [{user}] WITHOUT LOGIN; GRANT SELECT ON dbo.Customers TO [{user}]; GRANT SELECT ON dbo.Products TO [{user}]; GRANT SELECT ON dbo.CustomerFrequentProducts TO [{user}]; GRANT SELECT, INSERT, UPDATE ON dbo.CustomerProductPriceBooks TO [{user}]; GRANT SELECT, INSERT ON dbo.CustomerProductPriceRevisions TO [{user}]; DENY UPDATE, DELETE ON dbo.CustomerProductPriceRevisions TO [{user}]; GRANT INSERT ON dbo.AuditLogs TO [{user}];");
        try
        {
            await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
            await using (var impersonate = connection.CreateCommand()) { impersonate.CommandText = $"EXECUTE AS USER=N'{user}'"; await impersonate.ExecuteNonQueryAsync(); }
            try
            {
                await using var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlServer(connection).Options);
                var service = new CustomerProductPricingService(new EfCustomerRepository(db), new EfProductRepository(db),
                    new EfCommercialCatalogRepository(db), new EfAuditLogRepository(db), new TestClock());
                await service.ChangeAsync(catalog.CustomerId, catalog.ProductId, Change(20), new("synthetic-actor", "Restricted actor"), Ct);
                await service.ChangeAsync(catalog.CustomerId, catalog.ProductId, Change(25, 1), new("synthetic-actor", "Restricted actor"), Ct);
                var deniedUpdate = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE dbo.CustomerProductPriceRevisions SET UnitPrice=1"));
                Assert.Equal(229, deniedUpdate.Number);
                var deniedDelete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.CustomerProductPriceRevisions"));
                Assert.Equal(229, deniedDelete.Number);
            }
            finally { await using var revert = connection.CreateCommand(); revert.CommandText = "REVERT"; await revert.ExecuteNonQueryAsync(); }
        }
        finally { await setup.Database.ExecuteSqlRawAsync($"DROP USER [{user}];"); }
    }

#pragma warning restore EF1002

    private static async Task<JsonElement> NativeContract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        const string relative = "scripts/pilot/install-commercial-catalog-update.py";
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import json,runpy,sys;m=runpy.run_path(sys.argv[1]);print(json.dumps(dict(schema=m['NEW_SCHEMA'],expectedSchema=m['EXPECTED_SCHEMA'],grants=m['PRICE_GRANTS'],permissions=m['PRICE_PERMISSIONS'],expectedPermissions=m['EXPECTED_PRICE_PERMISSIONS'])))");
        start.ArgumentList.Add(Path.Combine(root!.FullName, relative));
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        return JsonSerializer.Deserialize<JsonElement>(output);
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset Now => SqlServerFixture.Now;
        public DateOnly Today => Day;
        public TimeOnly LocalTimeOfDay => new(8, 0);
    }
}
