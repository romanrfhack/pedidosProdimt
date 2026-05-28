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
