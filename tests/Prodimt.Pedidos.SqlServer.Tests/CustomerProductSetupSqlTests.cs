using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class CustomerProductSetupSqlTests(SqlServerFixture database)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static string CustomerUrl(Guid id) => $"/api/customer-orders/{id}";
    private static string SetupUrl(Guid id) => $"/api/admin/product-setups/{id}";

    [Fact]
    public async Task CaptureAcrossHosts_ReplaysOrderAndFollowUp_WithoutChangingProfileOrAssigningCustomerMachine()
    {
        var (catalog, plain, version) = await Prepare();
        var body = Capture(catalog, version);
        Guid orderId;
        await using (var host = database.CreateFactory())
        using (var customer = await CustomerClient(host, plain))
        {
            var available = (await customer.GetFromJsonAsync<AdditionalCustomerProduct[]>(CustomerUrl(catalog.CustomerId) + "/additional-products"))!;
            Assert.Contains(available, x => x.ProductId == catalog.SecondProductId && x.UnitCode == "package");
            Assert.DoesNotContain(available, x => x.ProductId == catalog.ProductId);
            var json = await customer.GetStringAsync(CustomerUrl(catalog.CustomerId) + "/additional-products");
            Assert.DoesNotContain("description", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("machine", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
            using var response = await customer.PostAsJsonAsync(CustomerUrl(catalog.CustomerId) + "/capture", body);
            response.EnsureSuccessStatusCode();
            var saved = (await response.Content.ReadFromJsonAsync<SubmitCustomerOrderCaptureResponse>(JsonOptions))!;
            orderId = saved.Order.OrderId; Assert.False(saved.Replayed); Assert.False(saved.Order.RequiresAdminReview);
        }
        await using var nextHost = database.CreateFactory();
        using var second = await CustomerClient(nextHost, plain);
        using var repeated = await second.PostAsJsonAsync(CustomerUrl(catalog.CustomerId) + "/capture", body);
        repeated.EnsureSuccessStatusCode();
        var replay = (await repeated.Content.ReadFromJsonAsync<SubmitCustomerOrderCaptureResponse>(JsonOptions))!;
        Assert.True(replay.Replayed); Assert.Equal(orderId, replay.Order.OrderId);
        await using var db = database.CreateDbContext();
        var line = await db.OrderLines.SingleAsync(x => x.OrderId == orderId);
        Assert.Null(line.AssignedMachineId); Assert.Null(line.UnitPriceSnapshot); Assert.Null(line.LineAmount);
        var request = await db.CustomerProductSetupRequests.Include(x => x.Orders).SingleAsync(x => x.CustomerId == catalog.CustomerId);
        Assert.Single(request.Orders); Assert.Equal("Customer", request.Orders[0].Source);
        Assert.Equal(version, (await db.Customers.SingleAsync(x => x.Id == catalog.CustomerId)).OrderingProfileVersion);
        Assert.Single(await db.CustomerFrequentProducts.Where(x => x.CustomerId == catalog.CustomerId).ToArrayAsync());
        Assert.Single(await db.OrderOperations.Where(x => x.OperationKey.StartsWith($"CUSTOMER-CAPTURE:{catalog.CustomerId:N}:")).ToArrayAsync());
    }

    [Fact]
    public async Task ResolutionAcrossHosts_UsesEffectivePriceAuditsAndReplaysWithoutRepricingAgain()
    {
        var (catalog, plain, version) = await Prepare();
        await using var host = database.CreateFactory();
        using var customer = await CustomerClient(host, plain);
        using var captured = await customer.PostAsJsonAsync(CustomerUrl(catalog.CustomerId) + "/capture", Capture(catalog, version));
        captured.EnsureSuccessStatusCode();
        var orderId = (await captured.Content.ReadFromJsonAsync<SubmitCustomerOrderCaptureResponse>(JsonOptions))!.Order.OrderId;
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var request = (await admin.GetFromJsonAsync<ProductSetupResponse[]>("/api/admin/product-setups", JsonOptions))!.Single(x => x.CustomerId == catalog.CustomerId);
        var missing = (await admin.GetFromJsonAsync<ProductSetupResolutionPreview>(SetupUrl(request.Id) + "/resolution-preview"))!;
        Assert.False(missing.CanComplete);
        await ConfigureExtra(admin, catalog);
        using var price = await admin.PostAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}/products/{catalog.SecondProductId}/prices",
            new ChangeCustomerProductPriceRequest(35.1250m, Day, "Tarifa ficticia", 0, Guid.NewGuid(), "package"));
        price.EnsureSuccessStatusCode();
        var preview = (await admin.GetFromJsonAsync<ProductSetupResolutionPreview>(SetupUrl(request.Id) + "/resolution-preview"))!;
        Assert.True(preview.CanComplete); Assert.Equal(35.1250m, Assert.Single(preview.Lines).ProposedPrice);
        var completion = new CompleteProductSetupRequest(Guid.NewGuid(), preview.Version, preview.Fingerprint, "Configuración ficticia revisada");
        using var completed = await admin.PostAsJsonAsync(SetupUrl(request.Id) + "/complete", completion);
        completed.EnsureSuccessStatusCode();
        await using var anotherHost = database.CreateFactory();
        using var anotherAdmin = await SqlServerFixture.CreateAdminClientAsync(anotherHost);
        using var repeated = await anotherAdmin.PostAsJsonAsync(SetupUrl(request.Id) + "/complete", completion);
        repeated.EnsureSuccessStatusCode();
        Assert.True((await repeated.Content.ReadFromJsonAsync<CompleteProductSetupResponse>())!.Replayed);
        await using var db = database.CreateDbContext();
        var line = await db.OrderLines.SingleAsync(x => x.OrderId == orderId);
        Assert.Equal(35.1250m, line.UnitPriceSnapshot); Assert.Equal(70.25m, line.LineAmount);
        Assert.Equal(Day, line.PriceEffectiveFrom); Assert.NotNull(line.PriceRevisionId); Assert.Null(line.AssignedMachineId);
        Assert.True((await db.CustomerProductSetupRequests.SingleAsync(x => x.Id == request.Id)).IsResolved);
        Assert.Single(await db.OrderAuditLogs.Where(x => x.OrderId == orderId && x.EventType == OrderAuditEventType.OrderLineInitialPriceResolved).ToArrayAsync());
        Assert.Empty(await db.OrderChangeNotices.Where(x => x.OrderId == orderId).ToArrayAsync());
        Assert.False((await db.CustomerFrequentProducts.SingleAsync(x => x.CustomerId == catalog.CustomerId && x.ProductId == catalog.SecondProductId)).UseCustomerDefaultMachine);
    }

    [Fact]
    public async Task ConcurrentCaptures_CreateOneDeliveryAndOneSetupDespiteDifferentReferences()
    {
        var (catalog, plain, version) = await Prepare();
        await using var host1 = database.CreateFactory(); await using var host2 = database.CreateFactory();
        using var first = await CustomerClient(host1, plain); using var second = await CustomerClient(host2, plain);
        var replies = await Task.WhenAll(first.PostAsJsonAsync(CustomerUrl(catalog.CustomerId) + "/capture", Capture(catalog, version)),
            second.PostAsJsonAsync(CustomerUrl(catalog.CustomerId) + "/capture", Capture(catalog, version)));
        try { Assert.Single(replies, x => x.IsSuccessStatusCode); Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var reply in replies) reply.Dispose(); }
        await using var db = database.CreateDbContext();
        Assert.Single(await db.Orders.Where(x => x.CustomerId == catalog.CustomerId).ToArrayAsync());
        var setup = await db.CustomerProductSetupRequests.Include(x => x.Orders).SingleAsync(x => x.CustomerId == catalog.CustomerId);
        Assert.Single(setup.Orders);
    }

    [Fact]
    public async Task PublicCatalogAndResolution_EnforceCustomerScopeAndAdministrativePolicy()
    {
        var (catalog, plain, _) = await Prepare();
        var other = await database.CreateCatalogAsync();
        await using var host = database.CreateFactory(); using var customer = await CustomerClient(host, plain);
        using var cross = await customer.GetAsync(CustomerUrl(other.CustomerId) + "/additional-products");
        Assert.Equal(HttpStatusCode.Forbidden, cross.StatusCode);
        foreach (var path in new[] { "/api/admin/product-setups", "/api/admin/product-setups/count",
            $"/api/admin/customers/{catalog.CustomerId}/additional-products", SetupUrl(Guid.NewGuid()) + "/resolution-preview" })
        {
            using var forbidden = await customer.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using var forbiddenPost = await customer.PostAsJsonAsync(SetupUrl(Guid.NewGuid()) + "/complete",
            new CompleteProductSetupRequest(Guid.NewGuid(), 1, new string('a', 64), "No autorizado"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPost.StatusCode);
    }

    [Fact]
    public async Task StaleResolutionAfterTariffChange_DoesNotFillAnyInitialPrice()
    {
        var (catalog, plain, version) = await Prepare();
        await using var host = database.CreateFactory(); using var customer = await CustomerClient(host, plain);
        using var captured = await customer.PostAsJsonAsync(CustomerUrl(catalog.CustomerId) + "/capture", Capture(catalog, version)); captured.EnsureSuccessStatusCode();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host); await ConfigureExtra(admin, catalog);
        var path = $"/api/admin/customers/{catalog.CustomerId}/products/{catalog.SecondProductId}/prices";
        using var initial = await admin.PostAsJsonAsync(path, new ChangeCustomerProductPriceRequest(10, Day, "Primera tarifa ficticia", 0, Guid.NewGuid(), "package")); initial.EnsureSuccessStatusCode();
        var setup = (await admin.GetFromJsonAsync<ProductSetupResponse[]>("/api/admin/product-setups", JsonOptions))!.Single(x => x.CustomerId == catalog.CustomerId);
        var preview = (await admin.GetFromJsonAsync<ProductSetupResolutionPreview>(SetupUrl(setup.Id) + "/resolution-preview"))!;
        using var changed = await admin.PostAsJsonAsync(path, new ChangeCustomerProductPriceRequest(12, Day, "Tarifa ficticia cambiada", 1, Guid.NewGuid(), "package")); changed.EnsureSuccessStatusCode();
        using var stale = await admin.PostAsJsonAsync(SetupUrl(setup.Id) + "/complete", new CompleteProductSetupRequest(Guid.NewGuid(), preview.Version, preview.Fingerprint, "Vista previa anterior"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var db = database.CreateDbContext();
        Assert.Null((await db.OrderLines.Where(x => x.ProductId == catalog.SecondProductId).SingleAsync()).UnitPriceSnapshot);
        Assert.False((await db.CustomerProductSetupRequests.SingleAsync(x => x.Id == setup.Id)).IsResolved);
    }

    private static SubmitCustomerOrderCaptureRequest Capture(SqlServerFixture.TestCatalog catalog, int version)
        => new(Guid.NewGuid(), version, Day, Lines: [new(catalog.SecondProductId, 2, "package")]);
    private static async Task ConfigureExtra(HttpClient admin, SqlServerFixture.TestCatalog catalog)
    {
        var profile = (await admin.GetFromJsonAsync<AdminCustomerOrderingProfileResponse>($"/api/admin/customers/{catalog.CustomerId}/ordering-profile"))!;
        var items = profile.Items.Select(x => new UpdateCustomerFrequentProductItemRequest(x.ProductId, x.DefaultQuantity,
            x.SortOrder, x.IsActive, x.PreferredMachineId, x.InterpretationPosition, x.UnitCode, x.CaptureAlias, x.UseCustomerDefaultMachine))
            .Append(new(catalog.SecondProductId, null, 2, true, null, null, "package", null, false)).ToArray();
        using var saved = await admin.PutAsJsonAsync($"/api/admin/customers/{catalog.CustomerId}/frequent-products",
            new UpdateCustomerFrequentProductsRequest(items, false, profile.OrderingProfileVersion)); saved.EnsureSuccessStatusCode();
    }
    private async Task<(SqlServerFixture.TestCatalog Catalog, string Plain, int Version)> Prepare()
    {
        var catalog = await database.CreateCatalogAsync(); var plain = "fictional-extra-product-" + Guid.NewGuid().ToString("N");
        await using var db = database.CreateDbContext();
        var customer = await db.Customers.SingleAsync(x => x.Id == catalog.CustomerId);
        customer.RecordOrderingProfileChange(true, SqlServerFixture.Now);
        var machineNumber = (await db.Machines.Select(x => (int?)x.Number).MaxAsync() ?? 0) + 1;
        var machine = Machine.Create(machineNumber, "Máquina ficticia para herencia"); db.Machines.Add(machine);
        db.CustomerMachineAssignments.Add(new() { CustomerId = catalog.CustomerId, MachineId = machine.Id, IsDefault = true, IsActive = true });
        db.CustomerFrequentProducts.Add(new() { CustomerId = catalog.CustomerId, ProductId = catalog.ProductId, SortOrder = 1, InterpretationPosition = 1 });
        db.CustomerAccessTokens.Add(CustomerAccessToken.Create(customer.Id, new CustomerAccessTokenHasher().HashToken(plain),
            "Acceso ficticio adicional", DateTimeOffset.UtcNow.AddHours(6), SqlServerFixture.Now));
        await db.SaveChangesAsync(); return (catalog, plain, customer.OrderingProfileVersion);
    }
    private static async Task<HttpClient> CustomerClient(SqlServerFixture.SqlApiFactory host, string plain)
    {
        var client = host.CreateClient(); using var response = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = plain });
        response.EnsureSuccessStatusCode(); var login = (await response.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken); return client;
    }
}
