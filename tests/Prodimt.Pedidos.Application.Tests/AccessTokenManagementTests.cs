using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class AccessTokenManagementTests
{
    private static readonly string CustomerPath = $"/api/admin/customers/{DevelopmentSeedIds.GranTakitoCustomerId}";
    private static readonly string TokensPath = CustomerPath + "/access-tokens";

    [Fact]
    public async Task CreateReloadEditAndReveal_PreservesCredentialAndOmitsSecretsFromListsAndAudit()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        await AdminLoginAsync(client);
        var created = await CreateReminderAsync(client);
        var store = factory.Services.GetRequiredService<InMemoryDataStore>();
        var persisted = store.CustomerAccessTokens.Single(x => x.Id == created.TokenId);
        var hash = persisted.TokenHash;
        var ciphertext = persisted.ProtectedToken;
        var lastUsed = persisted.LastUsedAt;
        Assert.NotNull(ciphertext);
        Assert.DoesNotContain(created.PlainToken, ciphertext!, StringComparison.Ordinal);
        Assert.NotEqual(created.PlainToken, hash);

        var listResponse = await client.GetAsync(TokensPath + "?activeOnly=true");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.PlainToken, listBody, StringComparison.Ordinal);
        Assert.DoesNotContain("plainToken", listBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", listBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedToken", listBody, StringComparison.OrdinalIgnoreCase);
        var rows = await listResponse.Content.ReadFromJsonAsync<AdminCustomerAccessTokenResponse[]>();
        var row = rows!.Single(x => x.TokenId == created.TokenId);
        Assert.True(row.IsUsable);
        Assert.True(row.CanReveal);
        Assert.True(row.ReminderLink);
        Assert.Equal(1, row.Version);

        // A fresh administrative client has no transient plaintext from the creation form.
        using var reloaded = factory.CreateClient();
        await AdminLoginAsync(reloaded);
        var firstReveal = await reloaded.PostAsJsonAsync($"{TokensPath}/{created.TokenId}/reveal", new { });
        Assert.Equal(HttpStatusCode.OK, firstReveal.StatusCode);
        AssertNoStore(firstReveal);
        var revealed = await firstReveal.Content.ReadFromJsonAsync<CreatedCustomerAccessTokenResponse>();
        Assert.Equal(created.PlainToken, revealed!.PlainToken);

        var extended = DateTimeOffset.UtcNow.AddHours(3);
        var update = await reloaded.PatchAsJsonAsync($"{TokensPath}/{created.TokenId}",
            new UpdateCustomerAccessTokenRequest(row.Version, "Recordatorio actualizado", extended));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<AdminCustomerAccessTokenResponse>();
        Assert.Equal(created.TokenId, updated!.TokenId);
        Assert.Equal(2, updated.Version);
        Assert.Equal(hash, persisted.TokenHash);
        Assert.Equal(ciphertext, persisted.ProtectedToken);
        Assert.Equal(lastUsed, persisted.LastUsedAt);

        var secondReveal = await reloaded.PostAsJsonAsync($"{TokensPath}/{created.TokenId}/reveal", new { });
        var afterEdit = await secondReveal.Content.ReadFromJsonAsync<CreatedCustomerAccessTokenResponse>();
        Assert.Equal(created.PlainToken, afterEdit!.PlainToken);
        Assert.Equal("Recordatorio actualizado", afterEdit.Description);
        Assert.Equal(extended, afterEdit.ExpiresAt);
        Assert.Equal(2, afterEdit.Version);

        foreach (var audit in store.AuditLogs.Where(x => x.EntityId == created.TokenId.ToString()))
        {
            Assert.DoesNotContain(created.PlainToken, audit.Summary + audit.MetadataJson, StringComparison.Ordinal);
            Assert.DoesNotContain(ciphertext!, audit.Summary + audit.MetadataJson, StringComparison.Ordinal);
            Assert.DoesNotContain(hash, audit.Summary + audit.MetadataJson, StringComparison.Ordinal);
        }
        Assert.Equal(2, store.AuditLogs.Count(x => x.EntityId == created.TokenId.ToString() && x.EventType == "CustomerAccessTokenRevealed"));
    }

    [Fact]
    public async Task StaleEditAndInvalidExpiry_DoNotModifyMetadataOrCreateAudit()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        await AdminLoginAsync(client);
        var token = await CreateReminderAsync(client);
        var store = factory.Services.GetRequiredService<InMemoryDataStore>();
        var path = $"{TokensPath}/{token.TokenId}";
        var expiry = DateTimeOffset.UtcNow.AddHours(3);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync(path,
            new UpdateCustomerAccessTokenRequest(1, "Primera edición", expiry))).StatusCode);
        var auditCount = store.AuditLogs.Count;

        var stale = await client.PatchAsJsonAsync(path, new UpdateCustomerAccessTokenRequest(1, "Edición obsoleta", expiry.AddHours(1)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path,
            new UpdateCustomerAccessTokenRequest(2, "Sin fecha", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path,
            new UpdateCustomerAccessTokenRequest(2, "Fecha pasada", DateTimeOffset.UtcNow.AddHours(-1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path,
            new UpdateCustomerAccessTokenRequest(2, new string('x', 201), expiry))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path,
            new UpdateCustomerAccessTokenRequest(2, " ", expiry))).StatusCode);
        var persisted = store.CustomerAccessTokens.Single(x => x.Id == token.TokenId);
        Assert.Equal("Primera edición", persisted.DisplayName);
        Assert.Equal(expiry, persisted.ExpiresAt);
        Assert.Equal(2, persisted.MetadataVersion);
        Assert.Equal(auditCount, store.AuditLogs.Count);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("inactive-customer")]
    public async Task UnavailableAccess_IsHiddenAndCannotBeRevealedOrReactivatedByEditing(string reason)
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        await AdminLoginAsync(client);
        var created = await CreateReminderAsync(client);
        var store = factory.Services.GetRequiredService<InMemoryDataStore>();
        var token = store.CustomerAccessTokens.Single(x => x.Id == created.TokenId);
        if (reason == "expired") token.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        else if (reason == "revoked")
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"{TokensPath}/{token.Id}/revoke", new { })).StatusCode);
        else store.Customers.Single(x => x.Id == token.CustomerId).IsActive = false;
        var auditCount = store.AuditLogs.Count;

        var active = await client.GetFromJsonAsync<AdminCustomerAccessTokenResponse[]>(TokensPath + "?activeOnly=true");
        Assert.DoesNotContain(active!, x => x.TokenId == token.Id);
        var reveal = await client.PostAsJsonAsync($"{TokensPath}/{token.Id}/reveal", new { });
        Assert.Equal(HttpStatusCode.Conflict, reveal.StatusCode);
        AssertNoStore(reveal);
        Assert.DoesNotContain(created.PlainToken, await reveal.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var update = await client.PatchAsJsonAsync($"{TokensPath}/{token.Id}",
            new UpdateCustomerAccessTokenRequest(token.MetadataVersion, "No reactivar", DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(auditCount, store.AuditLogs.Count);
        var history = await client.GetFromJsonAsync<AdminCustomerAccessTokenResponse[]>(TokensPath);
        Assert.Contains(history!, x => x.TokenId == token.Id && !x.IsUsable && !x.CanReveal);
    }

    [Fact]
    public async Task RevealAndEdit_RejectAnonymousCustomerAndWrongOwnerWithoutRevealingSecret()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var admin = factory.CreateClient();
        await AdminLoginAsync(admin);
        var token = await CreateReminderAsync(admin);
        var revealPath = $"{TokensPath}/{token.TokenId}/reveal";
        using var caller = factory.CreateClient();
        var anonymous = await caller.PostAsJsonAsync(revealPath, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        AssertNoStore(anonymous);
        var customerLogin = await caller.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest(token.PlainToken));
        var session = await customerLogin.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        caller.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);
        var forbidden = await caller.PostAsJsonAsync(revealPath, new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        AssertNoStore(forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.PatchAsJsonAsync($"{TokensPath}/{token.TokenId}",
            new UpdateCustomerAccessTokenRequest(1, "No autorizado", token.ExpiresAt))).StatusCode);
        var foreignPath = $"/api/admin/customers/{DevelopmentSeedIds.DemoCustomer2Id}/access-tokens/{token.TokenId}";
        var wrongOwner = await admin.PostAsJsonAsync(foreignPath + "/reveal", new { });
        Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);
        AssertNoStore(wrongOwner);
        Assert.DoesNotContain(token.PlainToken, await wrongOwner.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync(foreignPath,
            new UpdateCustomerAccessTokenRequest(1, "Otro cliente", token.ExpiresAt))).StatusCode);
    }

    [Fact]
    public async Task CorruptedProtectedSecret_FailsWithoutReturningCiphertextOrAuditingSuccess()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        await AdminLoginAsync(client);
        var token = await CreateReminderAsync(client);
        var store = factory.Services.GetRequiredService<InMemoryDataStore>();
        var row = store.CustomerAccessTokens.Single(x => x.Id == token.TokenId);
        const string corrupted = "v1.not-a-valid-ciphertext";
        row.ProtectedToken = corrupted;
        var count = store.AuditLogs.Count;
        var response = await client.PostAsJsonAsync($"{TokensPath}/{token.TokenId}/reveal", new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(token.PlainToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(corrupted, body, StringComparison.Ordinal);
        Assert.DoesNotContain(row.TokenHash, body, StringComparison.Ordinal);
        Assert.Equal(count, store.AuditLogs.Count);
    }

    [Fact]
    public async Task WrongStoredHash_DeniesValidCiphertextWithoutAuditingRecoveryOrReturningPlaintext()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        await AdminLoginAsync(client);
        var created = await CreateReminderAsync(client);
        var store = factory.Services.GetRequiredService<InMemoryDataStore>();
        var token = store.CustomerAccessTokens.Single(x => x.Id == created.TokenId);
        token.TokenHash = new string('x', 44);
        var auditCount = store.AuditLogs.Count;
        var response = await client.PostAsJsonAsync($"{TokensPath}/{token.Id}/reveal", new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStore(response);
        Assert.DoesNotContain(created.PlainToken, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(auditCount, store.AuditLogs.Count);
    }

    [Fact]
    public async Task CreationWithoutUsableEncryptionKey_FailsBeforeStagingTokenOrAudit()
    {
        await using var baseFactory = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["CustomerAccessTokens:EncryptionKey"] = "invalid-key-for-test" })));
        using var client = factory.CreateClient();
        await AdminLoginAsync(client);
        (await client.PatchAsJsonAsync(CustomerPath + "/phone", new UpdateCustomerPhoneRequest("5512345678"))).EnsureSuccessStatusCode();
        var store = factory.Services.GetRequiredService<InMemoryDataStore>();
        var tokenCount = store.CustomerAccessTokens.Count;
        var auditCount = store.AuditLogs.Count;
        var response = await client.PostAsJsonAsync(TokensPath,
            new CreateCustomerAccessTokenRequest("Sin clave válida", DateTimeOffset.UtcNow.AddHours(1), true));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStore(response);
        Assert.Equal(tokenCount, store.CustomerAccessTokens.Count);
        Assert.Equal(auditCount, store.AuditLogs.Count);
        Assert.DoesNotContain("invalid-key-for-test", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginConcurrentWithMetadataChange_RequiresFreshAuthenticationInsteadOfIssuingStaleJwt()
    {
        await using var baseFactory = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICustomerAccessTokenRepository>();
            services.AddSingleton<ICustomerAccessTokenRepository>(provider => new ConflictingUsageRepository(
                new InMemoryCustomerAccessTokenRepository(provider.GetRequiredService<InMemoryDataStore>())));
        }));
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/customer-token",
            new CustomerTokenLoginRequest(DevelopmentAuthSeedValues.DefaultCustomerToken));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DevelopmentAuthSeedValues.DefaultCustomerToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredLink_BlocksAlreadyAuthenticatedCustomer_WithoutChangingProtectedCredential()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var admin = factory.CreateClient();
        await AdminLoginAsync(admin);
        var token = await CreateReminderAsync(admin);
        using var customer = factory.CreateClient();
        var login = await customer.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest(token.PlainToken));
        var session = await login.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        var store = factory.Services.GetRequiredService<InMemoryDataStore>();
        var row = store.CustomerAccessTokens.Single(x => x.Id == token.TokenId);
        // Advance only the authoritative access-link deadline; the issued JWT still has its original expiry.
        row.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync(
            $"/api/customer-orders/{DevelopmentSeedIds.GranTakitoCustomerId}/next")).StatusCode);
        Assert.Equal(token.PlainToken, new AesGcmCustomerAccessTokenProtector(factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>())
            .Unprotect(row.CustomerId, row.Id, row.ProtectedToken!));
    }

    [Fact]
    public async Task ExtendingLink_RequiresNewLoginToReceiveLongerSession_AndKeepsTheSameToken()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var admin = factory.CreateClient();
        await AdminLoginAsync(admin);
        (await admin.PatchAsJsonAsync(CustomerPath + "/phone", new UpdateCustomerPhoneRequest("5512345678"))).EnsureSuccessStatusCode();
        var initialDeadline = DateTimeOffset.UtcNow.AddMinutes(5);
        var create = await admin.PostAsJsonAsync(TokensPath, new CreateCustomerAccessTokenRequest("Prueba de vigencia", initialDeadline, true));
        create.EnsureSuccessStatusCode();
        var token = (await create.Content.ReadFromJsonAsync<CreatedCustomerAccessTokenResponse>())!;
        using var customer = factory.CreateClient();
        var firstLogin = await customer.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest(token.PlainToken));
        var firstSession = (await firstLogin.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        Assert.Equal(initialDeadline, firstSession.ExpiresAt);

        var newDeadline = DateTimeOffset.UtcNow.AddMinutes(20);
        var update = await admin.PatchAsJsonAsync($"{TokensPath}/{token.TokenId}",
            new UpdateCustomerAccessTokenRequest(1, "Vigencia ampliada", newDeadline));
        update.EnsureSuccessStatusCode();
        var anotherLogin = await customer.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest(token.PlainToken));
        var nextSession = (await anotherLogin.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        Assert.Equal(newDeadline, nextSession.ExpiresAt);
        Assert.Equal(newDeadline, nextSession.AccessLinkExpiresAt);
        Assert.Equal(initialDeadline, firstSession.ExpiresAt);
        Assert.NotEqual(firstSession.AccessToken, nextSession.AccessToken);
    }

    private static async Task<CreatedCustomerAccessTokenResponse> CreateReminderAsync(HttpClient client)
    {
        (await client.PatchAsJsonAsync(CustomerPath + "/phone", new UpdateCustomerPhoneRequest("5512345678"))).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync(TokensPath,
            new CreateCustomerAccessTokenRequest("Recuerda pedir", DateTimeOffset.UtcNow.AddHours(2), true));
        response.EnsureSuccessStatusCode();
        AssertNoStore(response);
        return (await response.Content.ReadFromJsonAsync<CreatedCustomerAccessTokenResponse>())!;
    }

    private static async Task AdminLoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/admin/login", new AdminLoginRequest(
            DevelopmentAuthSeedValues.DefaultAdminUserName, DevelopmentAuthSeedValues.DefaultAdminPassword));
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);
    }

    private static void AssertNoStore(HttpResponseMessage response)
        => Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);

    private sealed class ConflictingUsageRepository(ICustomerAccessTokenRepository inner) : ICustomerAccessTokenRepository
    {
        public Task<T> ExecuteMutationAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => inner.ExecuteMutationAsync(action, ct);
        public Task<CustomerAccessToken?> GetByTokenHashAsync(string hash, CancellationToken ct) => inner.GetByTokenHashAsync(hash, ct);
        public Task<CustomerAccessToken?> GetByIdAsync(Guid id, CancellationToken ct) => inner.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<CustomerAccessToken>> GetByCustomerIdAsync(Guid id, CancellationToken ct) => inner.GetByCustomerIdAsync(id, ct);
        public Task AddAsync(CustomerAccessToken token, CancellationToken ct) => inner.AddAsync(token, ct);
        public Task SaveChangesAsync(CancellationToken ct) => throw new CatalogConflictException("Conflicto simulado de metadatos.");
    }
}
