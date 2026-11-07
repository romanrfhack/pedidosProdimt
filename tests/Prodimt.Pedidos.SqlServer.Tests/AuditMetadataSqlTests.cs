using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class AuditMetadataSqlTests(SqlServerFixture database)
{
    [Theory]
    [InlineData(9)]
    [InlineData(20)]
    public async Task LargeProfileAudit_PreservesEveryProductAndRollsBackOnAuditFailure(int count)
    {
        var catalog = await database.CreateCatalogAsync();
        var items = new List<UpdateCustomerFrequentProductItemRequest>();
        await using (var seed = database.CreateDbContext())
        {
            for (var i = 0; i < count; i++)
            {
                var product = Product.Create($"Producto sintético {Guid.NewGuid():N}", null);
                product.SetUnitCode("kg");
                seed.Products.Add(product);
                items.Add(new(product.Id, i + 1, i + 1, true, null, i + 1, "kg", ((char)('A' + i)).ToString(), false));
            }
            await seed.SaveChangesAsync();
        }
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        using var response = await admin.PutAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}/frequent-products", new UpdateCustomerFrequentProductsRequest(items, true, 0));
        response.EnsureSuccessStatusCode();
        string metadataBefore;
        await using (var check = database.CreateDbContext())
        {
            var customer = await check.Customers.SingleAsync(x => x.Id == catalog.CustomerId);
            Assert.Equal(1, customer.OrderingProfileVersion);
            Assert.Equal(count, await check.CustomerFrequentProducts.CountAsync(x => x.CustomerId == catalog.CustomerId));
            var audit = await check.AuditLogs.SingleAsync(x => x.EntityId == catalog.CustomerId.ToString() && x.EventType == "CustomerFrequentProductsUpdated");
            metadataBefore = audit.MetadataJson!;
            Assert.True(metadataBefore.Length > 2000);
            using var metadata = JsonDocument.Parse(metadataBefore);
            Assert.Equal(items.Select(x => x.ProductId), metadata.RootElement.GetProperty("productIds").EnumerateArray().Select(x => x.GetGuid()));
            Assert.Equal(count, metadata.RootElement.GetProperty("captureAliases").GetArrayLength());
            Assert.Equal(count, metadata.RootElement.GetProperty("machineInheritance").GetArrayLength());
        }
        await using (var attempt = database.CreateDbContext())
        {
            var service = new AdminCustomerCatalogService(new EfCustomerRepository(attempt), new EfProductRepository(attempt),
                new EfMachineRepository(attempt), new EfAuditLogRepository(attempt), new Clock(), new EfDeliveryRepository(attempt));
            // SQL rejects this synthetic actor name (>200). Profile rows/version/audit must roll back together.
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ReplaceFrequentProductsAsync(catalog.CustomerId,
                new(items.Select(x => x with { DefaultQuantity = 999 }).ToArray(), false, 1), new("synthetic", new string('A', 201)), default));
        }
        await using var final = database.CreateDbContext();
        Assert.Equal(1, (await final.Customers.SingleAsync(x => x.Id == catalog.CustomerId)).OrderingProfileVersion);
        var savedItems = await final.CustomerFrequentProducts.Where(x => x.CustomerId == catalog.CustomerId).OrderBy(x => x.SortOrder).ToArrayAsync();
        Assert.Equal(items.Select(x => x.DefaultQuantity), savedItems.Select(x => x.DefaultQuantity));
        Assert.Equal(metadataBefore, (await final.AuditLogs.SingleAsync(x => x.EntityId == catalog.CustomerId.ToString()
            && x.EventType == "CustomerFrequentProductsUpdated")).MetadataJson);
    }

    [Fact]
    public async Task ExpandedAuditColumn_RoundTripsUnicodeWithoutTruncation()
    {
        var metadata = JsonSerializer.Serialize(new { note = string.Concat(Enumerable.Repeat("ñ漢字🧾", 1000)) });
        var audit = AuditLog.Create("SyntheticAudit", Guid.NewGuid().ToString(), "UnicodeBoundary", SqlServerFixture.Now,
            AuditActorType.Admin, "Metadatos sintéticos completos", metadataJson: metadata);
        await using (var db = database.CreateDbContext())
        {
            db.AuditLogs.Add(audit);
            await db.SaveChangesAsync();
        }
        await using var check = database.CreateDbContext();
        Assert.Equal(metadata, (await check.AuditLogs.SingleAsync(x => x.Id == audit.Id)).MetadataJson);
    }

    [Fact]
    public async Task AuditMigration_PreservesExistingRowsAndRefusesLossyDowngrade()
    {
        // A separate UUID database keeps historical-schema transitions isolated from the shared fixture.
        var isolated = new SqlServerFixture();
        try
        {
            await isolated.InitializeAsync();
            const string previousMigration = "20261103030000_AddOrderingSchedules";
            const string expandedMigration = "20261104041928_ExpandAuditMetadata";
            await using (var old = isolated.CreateDbContext())
                await old.GetService<IMigrator>().MigrateAsync(previousMigration);
            var existing = AuditLog.Create("SyntheticAudit", Guid.NewGuid().ToString(), "PreserveExisting", SqlServerFixture.Now,
                AuditActorType.Admin, "Evidencia anterior sintética", metadataJson: "{\"nota\":\"ñ漢字🧾\"}");
            await using (var seed = isolated.CreateDbContext())
            {
                seed.AuditLogs.Add(existing);
                await seed.SaveChangesAsync();
                await seed.GetService<IMigrator>().MigrateAsync(expandedMigration);
            }
            var oversized = AuditLog.Create("SyntheticAudit", Guid.NewGuid().ToString(), "NoTruncation", SqlServerFixture.Now,
                AuditActorType.Admin, "Evidencia extensa sintética", metadataJson: "{}" + new string(' ', 1999));
            await using (var expanded = isolated.CreateDbContext())
            {
                Assert.Equal(existing.MetadataJson, (await expanded.AuditLogs.SingleAsync(x => x.Id == existing.Id)).MetadataJson);
                expanded.AuditLogs.Add(oversized);
                await expanded.SaveChangesAsync();
                var error = await Assert.ThrowsAsync<SqlException>(() => expanded.GetService<IMigrator>().MigrateAsync(previousMigration));
                Assert.Equal(51024, error.Number);
            }
            await using var check = isolated.CreateDbContext();
            Assert.Contains(expandedMigration, await check.Database.GetAppliedMigrationsAsync());
            Assert.Equal(oversized.MetadataJson, (await check.AuditLogs.SingleAsync(x => x.Id == oversized.Id)).MetadataJson);
            Assert.Equal(existing.MetadataJson, (await check.AuditLogs.SingleAsync(x => x.Id == existing.Id)).MetadataJson);
        }
        finally { await isolated.DisposeAsync(); }
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset Now => SqlServerFixture.Now;
        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
        public TimeOnly LocalTimeOfDay => new(8, 0);
    }
}
