using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CustomerLinkAuthTests
{
    [Theory]
    [InlineData(5, 5)]
    [InlineData(120, 60)]
    public void CustomerJwt_ContainsOriginTokenAndNeverOutlivesLink(int linkMinutes, int sessionMinutes)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 31, 15, 0, 0, TimeSpan.Zero));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Jwt:SigningKey"] = JwtSettings.DevelopmentSigningKey,
            ["Authentication:Jwt:AccessTokenMinutes"] = "60"
        }).Build();
        var customer = new Customer { Id = Guid.NewGuid(), Name = "Cliente de prueba" };
        var origin = CustomerAccessToken.Create(customer.Id, "test-hash", null, clock.Now.AddMinutes(linkMinutes), clock.Now);
        var result = new JwtTokenService(configuration, clock).CreateCustomerToken(customer, origin);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        Assert.Equal(clock.Now.AddMinutes(sessionMinutes), result.ExpiresAt);
        Assert.Equal(result.ExpiresAt.UtcDateTime, jwt.ValidTo);
        Assert.Contains(jwt.Claims, c => c.Type == ProdimtAuthClaims.CustomerAccessTokenId && c.Value == origin.Id.ToString());
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("expire")]
    [InlineData("deactivate")]
    public async Task AlreadyAuthenticatedCustomer_LosesAccessWhenOriginOrCustomerIsDisabled(string action)
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        await CustomerLoginAsync(client);
        var path = $"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/today";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ICustomerAccessTokenRepository>();
            var token = await tokens.GetByIdAsync(DevelopmentSeedIds.GranTakitoAccessTokenId, CancellationToken.None);
            if (action == "revoke") token!.Revoke();
            else if (action == "expire") token!.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            else
            {
                var customers = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
                var customer = await customers.GetByIdForUpdateAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None);
                customer!.Deactivate(DateTimeOffset.UtcNow);
                await customers.SaveChangesAsync(CancellationToken.None);
            }
            await tokens.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            $"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/submit", new { lines = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task CustomerJwtWithoutOriginClaim_RequiresReauthentication()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var customer = await scope.ServiceProvider.GetRequiredService<ICustomerRepository>()
            .GetByIdAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None);
        var legacyJwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateCustomerToken(customer!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", legacyJwt.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(
            $"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/today")).StatusCode);
    }

    [Fact]
    public async Task ReminderCreation_RequiresSavedPhoneAndExpiry_AndPhonePatchPreservesOtherFields()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/admin/login", new AdminLoginRequest(
            DevelopmentAuthSeedValues.DefaultAdminUserName, DevelopmentAuthSeedValues.DefaultAdminPassword));
        var session = await login.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);
        var customerPath = $"/api/admin/customers/{DevelopmentSeedIds.GranTakitoCustomerId}";
        var before = await client.GetFromJsonAsync<AdminCustomerResponse>(customerPath);
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var missingPhone = await client.PostAsJsonAsync(customerPath + "/access-tokens", new CreateCustomerAccessTokenRequest(null, expiry, true));
        Assert.Equal(HttpStatusCode.BadRequest, missingPhone.StatusCode);

        var patch = await client.PatchAsJsonAsync(customerPath + "/phone", new UpdateCustomerPhoneRequest("55 1234-5678"));
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
        var after = await client.GetFromJsonAsync<AdminCustomerResponse>(customerPath);
        Assert.Equal("+525512345678", after!.PhoneNumber);
        Assert.Equal(before!.Name, after.Name);
        Assert.Equal(before.PreferredDeliveryWindowStart, after.PreferredDeliveryWindowStart);
        Assert.Equal(before.RouteCode, after.RouteCode);
        Assert.Equal(before.RestDays, after.RestDays);

        var noExpiry = await client.PostAsJsonAsync(customerPath + "/access-tokens", new CreateCustomerAccessTokenRequest(null, null, true));
        Assert.Equal(HttpStatusCode.BadRequest, noExpiry.StatusCode);
        var created = await client.PostAsJsonAsync(customerPath + "/access-tokens", new CreateCustomerAccessTokenRequest(null, expiry, true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = await created.Content.ReadFromJsonAsync<CreatedCustomerAccessTokenResponse>();
        Assert.Equal("Recuerda hacer tu próximo pedido", link!.Description);

        client.DefaultRequestHeaders.Authorization = null;
        var customerLogin = await client.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest(link.PlainToken));
        var customerSession = await customerLogin.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        Assert.Equal(link.ExpiresAt, customerSession!.AccessLinkExpiresAt);
        Assert.True(customerSession.ExpiresAt <= link.ExpiresAt);
        Assert.Equal("no-store", customerLogin.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", customerLogin.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task PhonePatch_RequiresAdminAndRejectsInvalidPhoneWithoutChangingCustomer()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        var path = $"/api/admin/customers/{DevelopmentSeedIds.GranTakitoCustomerId}/phone";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsJsonAsync(path, new { phoneNumber = "5512345678" })).StatusCode);
        await CustomerLoginAsync(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync(path, new { phoneNumber = "5512345678" })).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var login = await client.PostAsJsonAsync("/api/auth/admin/login", new AdminLoginRequest(
            DevelopmentAuthSeedValues.DefaultAdminUserName, DevelopmentAuthSeedValues.DefaultAdminPassword));
        var admin = await login.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin!.AccessToken);
        var customerPath = $"/api/admin/customers/{DevelopmentSeedIds.GranTakitoCustomerId}";
        var before = await client.GetFromJsonAsync<AdminCustomerResponse>(customerPath);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path, new { phoneNumber = "incorrecto" })).StatusCode);
        var after = await client.GetFromJsonAsync<AdminCustomerResponse>(customerPath);
        Assert.Equal(before!.PhoneNumber, after!.PhoneNumber);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    [Theory]
    [InlineData("5512345678", "+525512345678")]
    [InlineData("52 (55) 1234-5678", "+525512345678")]
    [InlineData("+1 (202) 555-0123", "+12025550123")]
    public void PhoneNumber_NormalizesMexicoAndExplicitInternationalNumbers(string input, string expected)
        => Assert.Equal(expected, CustomerPhoneNumber.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000001")]
    [InlineData("+123")]
    [InlineData("+525512345678ext2")]
    [InlineData("442079460018")]
    public void PhoneNumber_RejectsMissingInvalidOrAmbiguousInternationalNumber(string? value)
        => Assert.Throws<ArgumentException>(() => CustomerPhoneNumber.Normalize(value));

    private static async Task CustomerLoginAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest(DevelopmentAuthSeedValues.DefaultCustomerToken));
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset Now => now;
        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(now.UtcDateTime);
    }
}
