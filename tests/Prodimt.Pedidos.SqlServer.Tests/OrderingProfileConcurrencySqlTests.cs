using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class OrderingProfileConcurrencySqlTests(SqlServerFixture database)
{
    private static readonly AdminActorContext Actor = new("profile-test", "Synthetic profile editor");

    [Fact]
    public async Task AddingApprovedProduct_ConcurrentWithRemovingItsUnit_CannotCommitAnInvalidApproval()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var profileDb = database.CreateDbContext();
        await using var productDb = database.CreateDbContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profileAudit = new PausedAudit(new EfAuditLogRepository(profileDb), release.Task);
        var productAudit = new PausedAudit(new EfAuditLogRepository(productDb), release.Task);
        var profileService = new AdminCustomerCatalogService(new EfCustomerRepository(profileDb),
            new EfProductRepository(profileDb), new EfMachineRepository(profileDb), profileAudit, new Clock());
        var productService = new AdminProductCatalogService(new EfProductRepository(productDb),
            productAudit, new Clock(), new EfCustomerRepository(productDb));
        var productName = (await productDb.Products.AsNoTracking().SingleAsync(x => x.Id == catalog.ProductId)).Name;

        // Pause after both services have validated their inputs and staged their audits,
        // but before either saves. The product edit has observed no referencing profile;
        // the profile editor has observed kg. Without a shared serializable scope,
        // both stale decisions can commit and leave an approved unitless product.
        var profileWrite = CaptureFailureAsync(() => profileService.ReplaceFrequentProductsAsync(catalog.CustomerId,
            Profile(catalog.ProductId, 0, "kg"), Actor, timeout.Token));
        Task<Exception?>? productWrite = null;
        try
        {
            await profileAudit.Reached.Task.WaitAsync(timeout.Token);
            productWrite = CaptureFailureAsync(() => productService.UpdateAsync(catalog.ProductId,
                new(productName, null, null), Actor, timeout.Token));
            await productAudit.Reached.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            release.TrySetResult();
        }

        var outcomes = await Task.WhenAll(profileWrite, productWrite!);
        Assert.Contains(outcomes, error => error is null);
        Assert.All(outcomes, error => Assert.True(error is null or CatalogConflictException,
            $"Unexpected concurrent result: {error?.GetType().Name}: {error?.Message}"));

        await using var persisted = database.CreateDbContext();
        var customer = await persisted.Customers.SingleAsync(x => x.Id == catalog.CustomerId);
        var product = await persisted.Products.SingleAsync(x => x.Id == catalog.ProductId);
        var membership = await persisted.CustomerFrequentProducts.SingleOrDefaultAsync(x =>
            x.CustomerId == catalog.CustomerId && x.ProductId == catalog.ProductId);
        if (customer.IsOrderProfileApproved)
        {
            Assert.NotNull(membership);
            Assert.Equal("kg", product.UnitCode);
            Assert.True(product.IsActive);
        }
        if (product.UnitCode is null)
            Assert.False(customer.IsOrderProfileApproved);

        var customerKey = catalog.CustomerId.ToString();
        var productKey = catalog.ProductId.ToString();
        Assert.Equal(outcomes[0] is null ? 1 : 0, await persisted.AuditLogs.CountAsync(x =>
            x.EntityId == customerKey && x.EventType == "CustomerFrequentProductsUpdated"));
        Assert.Equal(outcomes[1] is null ? 1 : 0, await persisted.AuditLogs.CountAsync(x =>
            x.EntityId == productKey && x.EventType == "ProductUpdated"));
    }

    [Fact]
    public async Task ProfileSnapshot_ReflectsProductInvalidation_AndRejectsStaleVersionOrUnit()
    {
        var catalog = await database.CreateCatalogAsync();
        await using var factory = database.CreateFactory();
        using var client = await SqlServerFixture.CreateAdminClientAsync(factory);
        var profileUrl = $"/api/admin/customers/{catalog.CustomerId}/frequent-products";
        var snapshotUrl = $"/api/admin/customers/{catalog.CustomerId}/ordering-profile";
        using var initial = await client.PutAsJsonAsync(profileUrl, Profile(catalog.ProductId, 0, "kg"));
        initial.EnsureSuccessStatusCode();
        var original = await client.GetFromJsonAsync<AdminCustomerOrderingProfileResponse>(snapshotUrl);
        Assert.NotNull(original);
        Assert.Equal(1, original.OrderingProfileVersion);
        Assert.True(original.IsOrderProfileApproved);
        var originalItem = Assert.Single(original.Items);
        Assert.Equal("kg", originalItem.UnitCode);
        Assert.True(originalItem.ProductIsActive);

        using var changed = await client.PutAsJsonAsync($"/api/admin/products/{catalog.ProductId}",
            new UpsertAdminProductRequest(originalItem.ProductName, null, "piece"));
        changed.EnsureSuccessStatusCode();
        var current = await client.GetFromJsonAsync<AdminCustomerOrderingProfileResponse>(snapshotUrl);
        Assert.NotNull(current);
        Assert.Equal(2, current.OrderingProfileVersion);
        Assert.False(current.IsOrderProfileApproved);
        Assert.Equal("piece", Assert.Single(current.Items).UnitCode);

        using var staleVersion = await client.PutAsJsonAsync(profileUrl, Profile(catalog.ProductId, 1, "piece"));
        Assert.Equal(HttpStatusCode.Conflict, staleVersion.StatusCode);
        using var staleMeaning = await client.PutAsJsonAsync(profileUrl, Profile(catalog.ProductId, 2, "kg"));
        Assert.Equal(HttpStatusCode.Conflict, staleMeaning.StatusCode);
        var unchanged = await client.GetFromJsonAsync<AdminCustomerOrderingProfileResponse>(snapshotUrl);
        Assert.NotNull(unchanged);
        Assert.Equal(2, unchanged.OrderingProfileVersion);
        Assert.False(unchanged.IsOrderProfileApproved);
        Assert.Equal("piece", Assert.Single(unchanged.Items).UnitCode);
    }

    private static UpdateCustomerFrequentProductsRequest Profile(Guid productId, int version, string expectedUnit)
        => new([new(productId, null, 1, true, InterpretationPosition: 1, ExpectedUnitCode: expectedUnit)],
            ApproveForInterpretation: true, ExpectedProfileVersion: version);

    private static async Task<Exception?> CaptureFailureAsync<T>(Func<Task<T>> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }

    private sealed class PausedAudit(IAuditLogRepository inner, Task release) : IAuditLogRepository
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken)
        {
            await inner.AddAsync(auditLog, cancellationToken);
            Reached.TrySetResult();
            await release.WaitAsync(cancellationToken);
        }
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset Now => SqlServerFixture.Now;
        public DateOnly Today => new(2026, 10, 24);
        public TimeOnly LocalTimeOfDay => new(8, 0);
    }
}
