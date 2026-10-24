using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class SqlServerOrderTests(SqlServerFixture database)
{
    private const string Header = "externalOrderKey,serviceDate,customerExternalCode,action,productExternalCode,quantity,unitCode,notes";

    [Fact]
    public async Task CleanDatabase_MigratesTwice_AndHasNoPendingModelChanges()
    {
        await using var db = database.CreateDbContext();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.True((await db.Database.GetAppliedMigrationsAsync()).Count() >= 7);
    }

    [Fact]
    public async Task Import_PreviewDoesNotWrite_ReplaySurvivesNewHost_AndCrossBatchDeduplicates()
    {
        var catalog = await database.CreateCatalogAsync();
        var request = ImportRequest(catalog, "10");
        Guid orderId;
        await using (var factory = database.CreateFactory())
        using (var client = await SqlServerFixture.CreateAdminClientAsync(factory))
        {
            using var preview = await client.PostAsJsonAsync("/api/admin/daily-orders/validate", request);
            var previewJson = await JsonAsync(preview);
            Assert.True(previewJson.GetProperty("canApply").GetBoolean());
            await using (var db = database.CreateDbContext())
                Assert.False(await db.Orders.AnyAsync(x => x.CustomerId == catalog.CustomerId));

            using var apply = await client.PostAsJsonAsync("/api/admin/daily-orders/apply", request);
            var applied = await JsonAsync(apply);
            Assert.True(applied.GetProperty("applied").GetBoolean());
            orderId = applied.GetProperty("orders")[0].GetProperty("orderId").GetGuid();
        }

        // A new web host and independent DB connection rule out an in-process replay cache.
        await using var secondFactory = database.CreateFactory();
        using var secondClient = await SqlServerFixture.CreateAdminClientAsync(secondFactory);
        using var replay = await secondClient.PostAsJsonAsync("/api/admin/daily-orders/apply", request);
        Assert.True((await JsonAsync(replay)).GetProperty("replayed").GetBoolean());
        using var anotherBatch = await secondClient.PostAsJsonAsync("/api/admin/daily-orders/apply", request with { batchKey = Guid.NewGuid().ToString("N") });
        var another = await JsonAsync(anotherBatch);
        Assert.Equal("existing", another.GetProperty("orders")[0].GetProperty("disposition").GetString());
        Assert.Equal(orderId, another.GetProperty("orders")[0].GetProperty("orderId").GetGuid());
        await using var persisted = database.CreateDbContext();
        var order = await persisted.Orders.Include(x => x.Lines).SingleAsync(x => x.CustomerId == catalog.CustomerId);
        Assert.Equal(orderId, order.Id);
        Assert.Equal(new DateOnly(2026, 10, 25), order.OrderDate);
        Assert.Equal(10m, Assert.Single(order.Lines).Quantity);
        Assert.Equal("kg", Assert.Single(order.Lines).UnitCode);
        Assert.True(order.HasStoreKey);
        Assert.Equal("SQL-TEST", order.RouteCode);
        Assert.True(await persisted.OrderAuditLogs.AnyAsync(x => x.OrderId == orderId));
    }

    [Fact]
    public async Task Import_ChangedPayloadConflicts_AndConcurrentRequestsNeverDuplicate()
    {
        var catalog = await database.CreateCatalogAsync();
        var request = ImportRequest(catalog, "10");
        await using var factory = database.CreateFactory();
        using var firstClient = await SqlServerFixture.CreateAdminClientAsync(factory);
        using var secondClient = await SqlServerFixture.CreateAdminClientAsync(factory);
        var results = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/admin/daily-orders/apply", request),
            secondClient.PostAsJsonAsync("/api/admin/daily-orders/apply", request));
        try
        {
            Assert.Contains(results, result => result.IsSuccessStatusCode);
            Assert.All(results, result => Assert.True(result.IsSuccessStatusCode || result.StatusCode == HttpStatusCode.Conflict,
                $"Concurrent request returned unexpected status {result.StatusCode}."));
        }
        finally { foreach (var result in results) result.Dispose(); }

        using var retry = await firstClient.PostAsJsonAsync("/api/admin/daily-orders/apply", request);
        retry.EnsureSuccessStatusCode();
        using var conflict = await firstClient.PostAsJsonAsync("/api/admin/daily-orders/apply", request with { content = request.content.Replace(",10,kg,", ",12,kg,") });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var db = database.CreateDbContext();
        Assert.Equal(1, await db.Orders.CountAsync(x => x.CustomerId == catalog.CustomerId));
        Assert.Equal(10m, (await db.OrderLines.SingleAsync(x => x.OrderId == db.Orders.Where(o => o.CustomerId == catalog.CustomerId).Select(o => o.Id).Single())).Quantity);
    }

    [Fact]
    public async Task Import_DatabaseFailureRollsBackWholeBatch_AndSameKeyCanRetry()
    {
        var first = await database.CreateCatalogAsync();
        var rejected = await database.CreateCatalogAsync();
        var request = new ImportBody($"{Header}\n{Row(first, "first", "4")}\n{Row(rejected, "second", "6")}", "sql-test.csv", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        var constraint = $"CK_I02Test_{Guid.NewGuid():N}";
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        await using var db = database.CreateDbContext();
        var receiptCount = await db.OrderOperations.CountAsync();
        using var preview = await client.PostAsJsonAsync("/api/admin/daily-orders/validate", request);
        Assert.True((await JsonAsync(preview)).GetProperty("canApply").GetBoolean());
        // Audit rows have a foreign key to Orders, so this fails after order inserts, not in preview.
        await ExecuteGeneratedTestDdlAsync($"ALTER TABLE [OrderAuditLogs] ADD CONSTRAINT [{constraint}] CHECK ([CustomerId] <> '{rejected.CustomerId}')");
        try
        {
            HttpResponseMessage? failed = null;
            Exception? failure = null;
            try { failed = await client.PostAsJsonAsync("/api/admin/daily-orders/apply", request); }
            catch (Exception ex) { failure = ex; }
            using (failed) Assert.True(failure is not null
                ? IsConstraintFailure(failure)
                : failed?.StatusCode == HttpStatusCode.InternalServerError);
            Assert.False(await db.Orders.AnyAsync(x => x.CustomerId == first.CustomerId || x.CustomerId == rejected.CustomerId));
            Assert.Equal(receiptCount, await db.OrderOperations.CountAsync());
        }
        finally
        {
            await ExecuteGeneratedTestDdlAsync($"ALTER TABLE [OrderAuditLogs] DROP CONSTRAINT [{constraint}]");
        }

        using var retry = await client.PostAsJsonAsync("/api/admin/daily-orders/apply", request);
        Assert.True((await JsonAsync(retry)).GetProperty("applied").GetBoolean());
        Assert.Equal(2, await db.Orders.CountAsync(x => x.CustomerId == first.CustomerId || x.CustomerId == rejected.CustomerId));
    }

    [Fact]
    public async Task Correction_ReplacesQuantity_WithHistory_AndRejectsStaleOrChangedReplay()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        using var imported = await client.PostAsJsonAsync("/api/admin/daily-orders/apply", ImportRequest(catalog, "10"));
        var orderId = (await JsonAsync(imported)).GetProperty("orders")[0].GetProperty("orderId").GetGuid();
        using var detail = await client.GetAsync($"/api/admin/orders/{orderId}");
        var version = (await JsonAsync(detail)).GetProperty("version").GetInt32();
        var correction = new { operationKey = Guid.NewGuid().ToString("N"), expectedVersion = version, reason = "Synthetic correction from 10 to 7", lines = new[] { new { productId = catalog.ProductId, quantity = 7m, notes = "Corrected quantity" } } };
        using var corrected = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction);
        var result = await JsonAsync(corrected);
        Assert.Equal(version + 1, result.GetProperty("version").GetInt32());
        Assert.Equal(7m, result.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        using var replay = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction);
        Assert.Equal(version + 1, (await JsonAsync(replay)).GetProperty("version").GetInt32());
        using var stale = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction with { operationKey = Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var changedReplay = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", correction with { reason = "Different payload" });
        Assert.Equal(HttpStatusCode.Conflict, changedReplay.StatusCode);
        await using var db = database.CreateDbContext();
        var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId);
        Assert.Equal(7m, Assert.Single(order.Lines).Quantity);
        Assert.Equal("SQL-TEST", order.RouteCode);
        var audit = await db.OrderAuditLogs.Where(x => x.OrderId == orderId).ToArrayAsync();
        Assert.Contains(audit, x => x.MetadataJson != null && x.MetadataJson.Contains("10") && x.MetadataJson.Contains("7"));
        Assert.Single(audit, x => x.EventType.ToString().Contains("Correct", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConcurrentCorrections_OnlyOneVersionWins_AndFailedCommandDoesNotLeaveReceipt()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var firstClient = await SqlServerFixture.CreateAdminClientAsync(factory);
        using var secondClient = await SqlServerFixture.CreateAdminClientAsync(factory);
        using var imported = await firstClient.PostAsJsonAsync("/api/admin/daily-orders/apply", ImportRequest(catalog, "10"));
        var orderId = (await JsonAsync(imported)).GetProperty("orders")[0].GetProperty("orderId").GetGuid();
        using var detail = await firstClient.GetAsync($"/api/admin/orders/{orderId}");
        var version = (await JsonAsync(detail)).GetProperty("version").GetInt32();
        var first = new { operationKey = Guid.NewGuid().ToString("N"), expectedVersion = version, reason = "Concurrent correction A", lines = new[] { new { productId = catalog.ProductId, quantity = 6m, notes = (string?)null } } };
        var second = new { operationKey = Guid.NewGuid().ToString("N"), expectedVersion = version, reason = "Concurrent correction B", lines = new[] { new { productId = catalog.ProductId, quantity = 8m, notes = (string?)null } } };
        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", first),
            secondClient.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", second));
        bool firstWon;
        try
        {
            Assert.Single(responses, response => response.IsSuccessStatusCode);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            firstWon = responses[0].IsSuccessStatusCode;
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var db = database.CreateDbContext();
        var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId);
        Assert.Equal(version + 1, order.Version);
        Assert.Equal(firstWon ? 6m : 8m, Assert.Single(order.Lines).Quantity);
        var loser = firstWon ? second : first;
        // A concurrency loser must remain rejected when retried unchanged, never replayed as a success.
        using var staleRetry = await firstClient.PostAsJsonAsync($"/api/admin/orders/{orderId}/corrections", loser);
        Assert.Equal(HttpStatusCode.Conflict, staleRetry.StatusCode);
        var audit = await db.OrderAuditLogs.Where(x => x.OrderId == orderId).ToArrayAsync();
        Assert.Single(audit, x => x.EventType.ToString().Contains("Correct", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PastServiceDate_CanBeReviewedByDate_AndOnlyThenAddsAuthorizedDemand()
    {
        var catalog = await database.CreateCatalogAsync();
        var request = ImportRequest(catalog, "9");
        request = request with { content = request.content.Replace("2026-10-25", "2026-10-23") };
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        using var imported = await client.PostAsJsonAsync("/api/admin/daily-orders/apply", request);
        var orderId = (await JsonAsync(imported)).GetProperty("orders")[0].GetProperty("orderId").GetGuid();
        using var pending = await client.GetAsync("/api/admin/orders/pending-review?date=2026-10-23");
        var pendingOrders = (await JsonAsync(pending)).EnumerateArray().ToArray();
        Assert.Contains(pendingOrders, item => item.GetProperty("orderId").GetGuid() == orderId);
        using var before = await client.GetAsync("/api/admin/operations/plan?date=2026-10-23");
        Assert.DoesNotContain((await JsonAsync(before)).GetProperty("machineDemand").EnumerateArray(),
            item => item.GetProperty("productId").GetGuid() == catalog.ProductId);
        using var review = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/review", new { decision = "Accepted", expectedVersion = 1 });
        review.EnsureSuccessStatusCode();
        using var after = await client.GetAsync("/api/admin/operations/plan?date=2026-10-23");
        var demand = (await JsonAsync(after)).GetProperty("machineDemand").EnumerateArray().ToArray();
        Assert.Equal(9m, Assert.Single(demand, item => item.GetProperty("productId").GetGuid() == catalog.ProductId)
            .GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task ProfilePositions_CanSwap_AndFailedReplacementPreservesVersionAndRows()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        object Profile(int first, int second) => new
        {
            approveForInterpretation = true,
            items = new[]
            {
                new { productId = catalog.ProductId, defaultQuantity = (decimal?)null, sortOrder = first, isActive = true, interpretationPosition = first },
                new { productId = catalog.SecondProductId, defaultQuantity = (decimal?)null, sortOrder = second, isActive = true, interpretationPosition = second }
            }
        };
        using var original = await client.PutAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}/frequent-products", Profile(1, 2));
        original.EnsureSuccessStatusCode();
        using var swap = await client.PutAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}/frequent-products", Profile(2, 1));
        swap.EnsureSuccessStatusCode();
        int expectedVersion;
        await using (var db = database.CreateDbContext())
        {
            var rows = await db.CustomerFrequentProducts.Where(x => x.CustomerId == catalog.CustomerId).ToArrayAsync();
            Assert.Equal(2, Assert.Single(rows, x => x.ProductId == catalog.ProductId).InterpretationPosition);
            expectedVersion = (await db.Customers.SingleAsync(x => x.Id == catalog.CustomerId)).OrderingProfileVersion;
            var customer = await db.Customers.SingleAsync(x => x.Id == catalog.CustomerId);
            customer.RecordOrderingProfileChange(false, SqlServerFixture.Now);
            var repository = new EfCustomerRepository(db);
            await repository.ReplaceFrequentProductsAsync(catalog.CustomerId,
                [new CustomerFrequentProduct { CustomerId = catalog.CustomerId, ProductId = Guid.NewGuid(), SortOrder = 1, InterpretationPosition = 1 }], default);
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync(default));
        }
        await using var persisted = database.CreateDbContext();
        Assert.Equal(expectedVersion, (await persisted.Customers.SingleAsync(x => x.Id == catalog.CustomerId)).OrderingProfileVersion);
        Assert.Equal(2, await persisted.CustomerFrequentProducts.CountAsync(x => x.CustomerId == catalog.CustomerId));
        Assert.Equal(2, (await persisted.CustomerFrequentProducts.SingleAsync(x => x.CustomerId == catalog.CustomerId && x.ProductId == catalog.ProductId)).InterpretationPosition);
    }

    private static ImportBody ImportRequest(SqlServerFixture.TestCatalog catalog, string quantity) => new(
        $"{Header}\n{Row(catalog, "order", quantity)}", "sql-test.csv", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));

    private static string Row(SqlServerFixture.TestCatalog catalog, string key, string quantity) =>
        $"{key},2026-10-25,{catalog.CustomerCode},order,{catalog.ProductCode},{quantity},kg,Synthetic SQL test";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private async Task ExecuteGeneratedTestDdlAsync(string commandText)
    {
        // DDL identifiers cannot be SQL parameters. Callers use only locally generated GUIDs,
        // never API, CSV, environment or user-provided text, in this disposable database.
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static bool IsConstraintFailure(Exception exception) => exception is SqlException { Number: 547 } ||
        exception.InnerException is not null && IsConstraintFailure(exception.InnerException);

    private sealed record ImportBody(string content, string fileName, string source, string batchKey);
}
