using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class AuthApiAuthorizationTests
{
    [Fact]
    public async Task CustomerEndpoint_RejectsRequestWithoutToken()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/today");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CustomerEndpoint_RejectsDifferentCustomerId()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        await SetCustomerBearerAsync(client);

        var response = await client.GetAsync($"/api/customer-orders/{DevelopmentSeedIds.DemoCustomer2Id}/today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CustomerEndpoint_AllowsOwnCustomerIdAndDoesNotExposeInternalData()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        await SetCustomerBearerAsync(client);

        var response = await client.GetAsync($"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/today");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("machine", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maquina", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminEndpoint_RejectsRequestWithoutToken()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/orders/today");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_AllowsAdminToken()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        await SetAdminBearerAsync(client);

        var response = await client.GetAsync("/api/admin/orders/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuditEndpoint_RequiresAdminToken()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        await SetCustomerBearerAsync(client);

        var submitResponse = await client.PostAsJsonAsync(
            $"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/submit",
            new SubmitCustomerOrderRequest(
            [
                new SubmitCustomerOrderLineRequest(DevelopmentSeedIds.ProductNineAndHalfId, 1, null)
            ]));
        var orderId = await ReadOrderIdAsync(submitResponse);

        client.DefaultRequestHeaders.Authorization = null;
        var anonymousAudit = await client.GetAsync($"/api/admin/orders/{orderId}/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAudit.StatusCode);

        await SetCustomerBearerAsync(client);
        var customerAudit = await client.GetAsync($"/api/admin/orders/{orderId}/audit");
        Assert.Equal(HttpStatusCode.Forbidden, customerAudit.StatusCode);

        await SetAdminBearerAsync(client);
        var adminAudit = await client.GetAsync($"/api/admin/orders/{orderId}/audit");
        Assert.Equal(HttpStatusCode.OK, adminAudit.StatusCode);
    }

    [Fact]
    public async Task AdminOrderDetailEndpoint_RequiresAdminTokenAndReturnsLines()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        await SetCustomerBearerAsync(client);

        var submitResponse = await client.PostAsJsonAsync(
            $"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/submit",
            new SubmitCustomerOrderRequest(
            [
                new SubmitCustomerOrderLineRequest(DevelopmentSeedIds.ProductNineAndHalfId, 1, null)
            ]));
        var orderId = await ReadOrderIdAsync(submitResponse);

        client.DefaultRequestHeaders.Authorization = null;
        var anonymousDetail = await client.GetAsync($"/api/admin/orders/{orderId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDetail.StatusCode);

        await SetCustomerBearerAsync(client);
        var customerDetail = await client.GetAsync($"/api/admin/orders/{orderId}");
        Assert.Equal(HttpStatusCode.Forbidden, customerDetail.StatusCode);

        await SetAdminBearerAsync(client);
        var adminDetail = await client.GetAsync($"/api/admin/orders/{orderId}");
        var body = await adminDetail.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, adminDetail.StatusCode);
        Assert.Contains("\"lines\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assignedMachineId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GET", "/api/admin/customers/pending-orders")]
    [InlineData("GET", "/api/admin/customers/11111111-1111-1111-1111-111111111111/order-template")]
    [InlineData("POST", "/api/admin/customers/11111111-1111-1111-1111-111111111111/orders/submit")]
    [InlineData("POST", "/api/admin/customers/11111111-1111-1111-1111-111111111111/orders/no-order")]
    public async Task AdminCustomerEndpoints_RejectCustomerJwt(string method, string path)
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        await SetCustomerBearerAsync(client);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/customers")]
    [InlineData("/api/admin/products")]
    [InlineData("/api/admin/machines")]
    [InlineData("/api/admin/users")]
    public async Task AdminCatalogEndpoints_RequireAdminAccess(string path)
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var anonymousResponse = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        await SetCustomerBearerAsync(client);
        var customerResponse = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);

        await SetAdminBearerAsync(client);
        var adminResponse = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task AdminImportEndpoints_RequireAdminAccess()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var validateRequest = new
        {
            content = "externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes\nT-API,Cliente API,555,true,,,,",
            fileName = "customers.csv"
        };

        var anonymousTemplates = await client.GetAsync("/api/admin/import/templates");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousTemplates.StatusCode);

        var anonymousValidate = await client.PostAsJsonAsync("/api/admin/import/customers/validate", validateRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousValidate.StatusCode);

        await SetCustomerBearerAsync(client);
        var customerTemplates = await client.GetAsync("/api/admin/import/templates");
        Assert.Equal(HttpStatusCode.Forbidden, customerTemplates.StatusCode);

        var customerValidate = await client.PostAsJsonAsync("/api/admin/import/customers/validate", validateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, customerValidate.StatusCode);

        await SetAdminBearerAsync(client);
        var adminTemplates = await client.GetAsync("/api/admin/import/templates");
        Assert.Equal(HttpStatusCode.OK, adminTemplates.StatusCode);

        var adminValidate = await client.PostAsJsonAsync("/api/admin/import/customers/validate", validateRequest);
        Assert.Equal(HttpStatusCode.OK, adminValidate.StatusCode);
    }

    private static async Task SetCustomerBearerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/customer-token",
            new CustomerTokenLoginRequest(DevelopmentAuthSeedValues.DefaultCustomerToken));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
    }

    private static async Task SetAdminBearerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/admin/login",
            new AdminLoginRequest(DevelopmentAuthSeedValues.DefaultAdminUserName, DevelopmentAuthSeedValues.DefaultAdminPassword));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
    }

    private static async Task<Guid> ReadOrderIdAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return Guid.Parse(document.RootElement.GetProperty("orderId").GetString()!);
    }

    private sealed class AuthApiFactory : WebApplicationFactory<Program>
    {
        public AuthApiFactory()
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", "InMemory");
            Environment.SetEnvironmentVariable("DevelopmentSeed__Enabled", "false");
            Environment.SetEnvironmentVariable("Authentication__Jwt__SigningKey", JwtSettings.DevelopmentSigningKey);
            Environment.SetEnvironmentVariable("Authentication__Jwt__Issuer", "Prodimt.Pedidos.Tests");
            Environment.SetEnvironmentVariable("Authentication__Jwt__Audience", "Prodimt.Pedidos.Tests");
            Environment.SetEnvironmentVariable("Authentication__Jwt__AccessTokenMinutes", "60");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Authentication:Jwt:SigningKey"] = JwtSettings.DevelopmentSigningKey,
                    ["Authentication:Jwt:Issuer"] = "Prodimt.Pedidos.Tests",
                    ["Authentication:Jwt:Audience"] = "Prodimt.Pedidos.Tests",
                    ["Authentication:Jwt:AccessTokenMinutes"] = "60",
                    ["DevelopmentSeed:Enabled"] = "false"
                });
            });
        }
    }
}
