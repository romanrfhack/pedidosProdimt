using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class OrderNoteValidationApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    [Theory]
    [InlineData("delivery", 500, HttpStatusCode.OK)]
    [InlineData("delivery", 501, HttpStatusCode.BadRequest)]
    [InlineData("internal", 1000, HttpStatusCode.OK)]
    [InlineData("internal", 1001, HttpStatusCode.BadRequest)]
    public async Task ReviewEndpoint_EnforcesNoteBoundaryAndKeepsOriginalOrderOn400(string field, int length, HttpStatusCode expected)
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<AdminOrderService>(provider =>
                ActivatorUtilities.CreateInstance<AdminOrderService>(provider, new ReviewClock()))));
        using var client = factory.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/admin/login",
            new AdminLoginRequest(DevelopmentAuthSeedValues.DefaultAdminUserName, DevelopmentAuthSeedValues.DefaultAdminPassword));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        using var submitted = await client.PostAsJsonAsync($"/api/admin/customers/{DevelopmentSeedIds.GranTakitoCustomerId}/orders/submit",
            new AdminSubmitCustomerOrderRequest([new(DevelopmentSeedIds.ProductNineAndHalfId, 20, null)],
                null, null, null, "Original", null, new DateOnly(2026, 11, 2)));
        submitted.EnsureSuccessStatusCode();
        var created = await submitted.Content.ReadFromJsonAsync<AdminOrderSummaryResponse>(JsonOptions);
        var orderId = created!.OrderId;
        var before = await client.GetFromJsonAsync<AdminOrderDetailResponse>($"/api/admin/orders/{orderId}", JsonOptions);
        var auditBefore = await client.GetStringAsync($"/api/admin/orders/{orderId}/audit");
        var note = new string('ñ', length);
        using var response = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/review",
            new ReviewOrderRequest(AdminDecision.AcceptedWithChanges, field == "internal" ? note : "Revisado",
                DeliveryNotes: field == "delivery" ? note : "Revisado", ExpectedVersion: before!.Version));
        Assert.Equal(expected, response.StatusCode);
        var after = await client.GetFromJsonAsync<AdminOrderDetailResponse>($"/api/admin/orders/{orderId}", JsonOptions);
        if (expected == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(field == "delivery" ? "entrega" : "internas", problem.GetProperty("error").GetString());
            Assert.Equal(before.Status, after!.Status);
            Assert.Equal(before.Version, after.Version);
            Assert.Equal(before.DeliveryNotes, after.DeliveryNotes);
            Assert.Equal(before.InternalNotes, after.InternalNotes);
            Assert.Equal(auditBefore, await client.GetStringAsync($"/api/admin/orders/{orderId}/audit"));
        }
        else
        {
            Assert.Equal(OrderStatus.Accepted, after!.Status);
            Assert.Equal(note, field == "delivery" ? after.DeliveryNotes : after.InternalNotes);
        }
    }

    private sealed class ReviewClock : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 11, 2, 11, 0, 0, TimeSpan.FromHours(-6));
        public DateOnly Today => new(2026, 11, 2);
        public TimeOnly LocalTimeOfDay => new(11, 0);
    }
}
