using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class AdminPasswordApiTests
{
    private const string Path = "/api/admin/account/change-password";
    private const string NewPassword = "Fictional-new-password-2026!";

    internal static async Task<string> LoginAsync(HttpClient client, string password, string? userName = null)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/admin/login", new
        { userName = userName ?? DevelopmentAuthSeedValues.DefaultAdminUserName, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminLoginResponse>())!.AccessToken;
    }

    private static HttpClient Client(AuthApiAuthorizationTests.AuthApiFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task ChangePassword_RequiresAdminAndRejectsCustomer()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = Client(factory);
        var body = new ChangeAdminPasswordRequest("fictional", NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Path, body)).StatusCode);
        var token = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = DevelopmentAuthSeedValues.DefaultCustomerToken });
        var login = await token.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Path, body)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RevokesAllPriorAdminSessionsAndPreservesCustomerAndOtherAdmin()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var first = Client(factory);
        using var second = Client(factory);
        var old = DevelopmentAuthSeedValues.DefaultAdminPassword;
        first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(first, old));
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(second, old));
        (await first.PostAsJsonAsync("/api/admin/users", new { userName = "other-fictional", displayName = "Other fixture", password = old })).EnsureSuccessStatusCode();
        using var other = Client(factory);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(other, old, "other-fictional"));
        var customerResponse = await first.PostAsJsonAsync("/api/auth/customer-token", new { token = DevelopmentAuthSeedValues.DefaultCustomerToken });
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        var response = await first.PostAsJsonAsync(Path, new { currentPassword = old, newPassword = NewPassword,
            confirmPassword = NewPassword, userId = Guid.NewGuid(), userName = "other-fictional" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/admin/users")).StatusCode);
        first.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PostAsJsonAsync("/api/auth/admin/login", new { userName = "admin", password = old })).StatusCode);
        first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(first, NewPassword));
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/admin/users")).StatusCode);
        first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync($"/api/customer-orders/{customer.CustomerId}/today")).StatusCode);
        var audit = Assert.Single(factory.Services.GetRequiredService<InMemoryDataStore>().AuditLogs,
            x => x.EventType == "AdminUserPasswordChanged");
        Assert.Null(audit.MetadataJson);
        Assert.DoesNotContain(old, audit.Summary);
        Assert.DoesNotContain(NewPassword, audit.Summary);
        Assert.Equal(audit.EntityId, audit.ActorId);
    }

    [Theory]
    [InlineData("wrong-current", NewPassword, NewPassword)]
    [InlineData(null, NewPassword, "different")]
    [InlineData(null, "short", "short")]
    [InlineData(null, "prodimt-admin-demo", "prodimt-admin-demo")]
    public async Task InvalidChange_DoesNotWriteOrRevokeSession(string? current, string next, string confirm)
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = Client(factory);
        var old = DevelopmentAuthSeedValues.DefaultAdminPassword;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, old));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path,
            new ChangeAdminPasswordRequest(current ?? old, next, confirm))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Empty(factory.Services.GetRequiredService<InMemoryDataStore>().AuditLogs);
        Assert.NotEmpty(await LoginAsync(client, old));
    }

    [Fact]
    public async Task DeactivatingAccount_InvalidatesItsExistingToken()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await LoginAsync(client, DevelopmentAuthSeedValues.DefaultAdminPassword));
        factory.Services.GetRequiredService<InMemoryDataStore>().AdminUsers.Single().IsActive = false;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
    }
}
