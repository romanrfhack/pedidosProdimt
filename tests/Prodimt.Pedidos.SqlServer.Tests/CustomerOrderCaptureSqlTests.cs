using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class CustomerOrderCaptureSqlTests(SqlServerFixture database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private static string Url(Guid customer) => $"/api/customer-orders/{customer}";

    [Fact]
    public async Task CustomerCapture_ReplaysAcrossHostsAndDoesNotExposeInternalContext()
    {
        var (catalog, plain) = await Prepare();
        SubmitCustomerOrderCaptureRequest request;
        Guid orderId;
        await using (var host = database.CreateFactory())
        using (var client = await CustomerClient(host, plain))
        {
            var next = (await client.GetFromJsonAsync<CustomerNextOrderResponse>(Url(catalog.CustomerId) + "/next", JsonOptions))!;
            using var previewResponse = await client.PostAsJsonAsync(Url(catalog.CustomerId) + "/preview",
                new PreviewCustomerOrderCaptureRequest("13+2HN", next.DeliveryDate));
            previewResponse.EnsureSuccessStatusCode();
            var preview = (await previewResponse.Content.ReadFromJsonAsync<PreviewCustomerOrderCaptureResponse>(JsonOptions))!;
            request = new(Guid.NewGuid(), next.ProfileVersion, next.DeliveryDate, preview.Text, preview.Fingerprint);
            using var response = await client.PostAsJsonAsync(Url(catalog.CustomerId) + "/capture", request);
            response.EnsureSuccessStatusCode();
            var saved = (await response.Content.ReadFromJsonAsync<SubmitCustomerOrderCaptureResponse>(JsonOptions))!;
            orderId = saved.Order.OrderId;
            Assert.False(saved.Replayed);
            var publicText = await client.GetStringAsync(Url(catalog.CustomerId) + "/next");
            Assert.DoesNotContain("machine", publicText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("internal", publicText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hasStoreKey", publicText, StringComparison.OrdinalIgnoreCase);
        }
        await using var secondHost = database.CreateFactory();
        using var secondClient = await CustomerClient(secondHost, plain);
        using var replay = await secondClient.PostAsJsonAsync(Url(catalog.CustomerId) + "/capture", request);
        replay.EnsureSuccessStatusCode();
        var repeated = (await replay.Content.ReadFromJsonAsync<SubmitCustomerOrderCaptureResponse>(JsonOptions))!;
        Assert.True(repeated.Replayed); Assert.Equal(orderId, repeated.Order.OrderId);
        using var changed = await secondClient.PostAsJsonAsync(Url(catalog.CustomerId) + "/capture",
            request with { DeliveryNotes = "Otro contenido" });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        await using var db = database.CreateDbContext();
        var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.CustomerId == catalog.CustomerId);
        Assert.Equal(15, order.Lines.Sum(x => x.Quantity));
        Assert.Contains(order.Lines, x => x.UnitCode == "package" && x.Quantity == 2);
        Assert.Single(await db.OrderOperations.Where(x => x.OperationKey.StartsWith($"CUSTOMER-CAPTURE:{catalog.CustomerId:N}:")).ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentCustomerCaptures_CreateOneDeliveryAndOneReceipt()
    {
        var (catalog, plain) = await Prepare();
        await using var host = database.CreateFactory();
        using var first = await CustomerClient(host, plain);
        using var second = await CustomerClient(host, plain);
        var next = (await first.GetFromJsonAsync<CustomerNextOrderResponse>(Url(catalog.CustomerId) + "/next", JsonOptions))!;
        SubmitCustomerOrderCaptureRequest Request() => new(Guid.NewGuid(), next.ProfileVersion, next.DeliveryDate,
            Lines: [new(catalog.ProductId, 16, "kg")]);
        var responses = await Task.WhenAll(first.PostAsJsonAsync(Url(catalog.CustomerId) + "/capture", Request()),
            second.PostAsJsonAsync(Url(catalog.CustomerId) + "/capture", Request()));
        try
        {
            Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var db = database.CreateDbContext();
        Assert.Single(await db.Orders.Where(x => x.CustomerId == catalog.CustomerId).ToArrayAsync());
        Assert.Single(await db.OrderOperations.Where(x => x.OperationKey.StartsWith($"CUSTOMER-CAPTURE:{catalog.CustomerId:N}:")).ToArrayAsync());
    }

    private async Task<(SqlServerFixture.TestCatalog Catalog, string Plain)> Prepare()
    {
        var catalog = await database.CreateCatalogAsync();
        var plain = "fictional-i09-" + Guid.NewGuid().ToString("N");
        await using var db = database.CreateDbContext();
        var customer = await db.Customers.SingleAsync(x => x.Id == catalog.CustomerId);
        customer.RecordOrderingProfileChange(true, SqlServerFixture.Now);
        db.CustomerFrequentProducts.AddRange(
            new CustomerFrequentProduct { CustomerId = customer.Id, ProductId = catalog.ProductId, SortOrder = 1, InterpretationPosition = 1 },
            new CustomerFrequentProduct { CustomerId = customer.Id, ProductId = catalog.SecondProductId, SortOrder = 2, InterpretationPosition = 2, CaptureAlias = "HN" });
        // Fixture business time is fixed, while JWT validity intentionally uses the real clock.
        db.CustomerAccessTokens.Add(CustomerAccessToken.Create(customer.Id, new CustomerAccessTokenHasher().HashToken(plain),
            "Acceso ficticio I09", DateTimeOffset.UtcNow.AddHours(6), SqlServerFixture.Now));
        await db.SaveChangesAsync();
        return (catalog, plain);
    }

    private static async Task<HttpClient> CustomerClient(SqlServerFixture.SqlApiFactory host, string plain)
    {
        var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = plain });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
}
