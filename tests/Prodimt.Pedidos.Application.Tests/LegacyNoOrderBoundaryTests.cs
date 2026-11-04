using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class LegacyNoOrderBoundaryTests
{
    [Theory]
    [InlineData("2026-11-02T15:00:00Z", HttpStatusCode.OK)]
    [InlineData("2026-11-02T23:59:59Z", HttpStatusCode.OK)]
    [InlineData("2026-11-03T00:00:00Z", HttpStatusCode.Conflict)]
    [InlineData("2026-11-03T15:00:00Z", HttpStatusCode.Conflict)]
    [InlineData("2026-11-04T15:00:00Z", HttpStatusCode.OK)]
    public async Task LegacyNoOrder_RespectsReceptionBoundary_WithoutChangingAuthenticationClock(
        string receivedAt, HttpStatusCode expected)
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        var clock = new CaptureOnlyClock(DateTimeOffset.Parse(receivedAt));
        await using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<CustomerOrderService>(provider =>
                ActivatorUtilities.CreateInstance<CustomerOrderService>(provider, clock))));
        using var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/customer-token",
            new CustomerTokenLoginRequest(DevelopmentAuthSeedValues.DefaultCustomerToken));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var response = await client.PostAsync($"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/no-order", null);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(1, clock.Reads);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (expected == HttpStatusCode.Conflict)
        {
            Assert.Contains("siguiente día hábil", body.GetProperty("error").GetString());
            using var scope = factory.Services.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            Assert.Empty(await orders.GetByDateAsync(DeliveryDatePolicy.LocalDate(clock.ReceivedAt), CancellationToken.None));
            Assert.Empty(await orders.GetByDateAsync(DeliveryDatePolicy.GetDate(clock.ReceivedAt), CancellationToken.None));
        }
        else
        {
            Assert.Equal(DeliveryDatePolicy.LocalDate(clock.ReceivedAt).ToString("yyyy-MM-dd"), body.GetProperty("orderDate").GetString());
            Assert.Equal(clock.ReceivedAt, body.GetProperty("submittedAt").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task LegacyNoOrder_RepeatedDuringOpenDay_ReturnsSameOrderAndAudit()
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<CustomerOrderService>(provider => ActivatorUtilities.CreateInstance<CustomerOrderService>(
                provider, new CaptureOnlyClock(DateTimeOffset.Parse("2026-11-02T15:00:00Z"))))));
        using var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/customer-token",
            new CustomerTokenLoginRequest(DevelopmentAuthSeedValues.DefaultCustomerToken)))
            .Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        var path = $"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/no-order";
        var first = await client.PostAsync(path, null);
        var second = await client.PostAsync(path, null);
        first.EnsureSuccessStatusCode(); second.EnsureSuccessStatusCode();
        var saved = await first.Content.ReadFromJsonAsync<JsonElement>();
        var repeated = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saved.GetRawText(), repeated.GetRawText());
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
            .GetByDateAsync(new DateOnly(2026, 11, 2), CancellationToken.None));
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IOrderAuditLogRepository>()
            .GetByOrderIdAsync(saved.GetProperty("orderId").GetGuid(), CancellationToken.None));
    }

    // Count direct Now reads without altering the day represented by the fake.
    // Boundary assertions therefore also fail meaningfully on the old service,
    // which accepted NoOrder on closed days instead of returning 409.
    private sealed class CaptureOnlyClock(DateTimeOffset receivedAt) : IDateTimeProvider
    {
        public DateTimeOffset ReceivedAt => receivedAt;
        public int Reads { get; private set; }
        public DateTimeOffset Now => ++Reads == 1 ? receivedAt : throw new InvalidOperationException("Clock read twice.");
        public DateOnly Today => DeliveryDatePolicy.LocalDate(receivedAt);
        public TimeOnly LocalTimeOfDay => DeliveryDatePolicy.LocalTime(receivedAt);
    }
}
