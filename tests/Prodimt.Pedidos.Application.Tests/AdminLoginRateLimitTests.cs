using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Prodimt.Pedidos.Api;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class AdminLoginRateLimitTests
{
    private const string LoginPath = "/api/auth/admin/login";
    private static readonly AdminLoginRequest Valid = new(
        DevelopmentAuthSeedValues.DefaultAdminUserName, DevelopmentAuthSeedValues.DefaultAdminPassword);
    private static readonly AdminLoginRequest Invalid = new("fictional-user", "fictional-password-not-for-logs");

    [Fact]
    public async Task PerIpLimit_ReturnsSanitized429AndRetryAfter_ThenAllowsLoginAfterWindow()
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = Configure(parent, perIp: 1, global: 100, seconds: 1);
        using var client = Client(factory);
        // Initialize the host/store before the measured rate-limit window.
        _ = factory.Services.GetRequiredService<InMemoryDataStore>();
        using var first = await client.PostAsJsonAsync(LoginPath, Invalid);
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        using var limited = await client.PostAsJsonAsync(LoginPath, Valid);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter?.Delta);
        Assert.InRange(limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 1);
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
        var body = await limited.Content.ReadAsStringAsync();
        Assert.Equal("Demasiados intentos de acceso. Espera y vuelve a intentar.",
            JsonDocument.Parse(body).RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain(Invalid.UserName, body);
        Assert.DoesNotContain(Invalid.Password, body);
        Assert.DoesNotContain(Valid.Password, body);

        await Task.Delay(TimeSpan.FromMilliseconds(1_100));
        using var recovered = await client.PostAsJsonAsync(LoginPath, Valid);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var login = (await recovered.Content.ReadFromJsonAsync<AdminLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/security/me")).StatusCode);
    }

    [Fact]
    public async Task ForwardedAddress_FromUntrustedPeerCannotBypassPerIpLimit()
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = Configure(parent, perIp: 1, global: 100);
        var first = await SendFromAsync(factory, "198.51.100.10", "203.0.113.1");
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        var spoofed = await SendFromAsync(factory, "198.51.100.10", "203.0.113.2");
        Assert.Equal(HttpStatusCode.TooManyRequests, spoofed.StatusCode);
        var differentPeer = await SendFromAsync(factory, "198.51.100.11", "203.0.113.2");
        Assert.Equal(HttpStatusCode.Unauthorized, differentPeer.StatusCode);
    }

    [Fact]
    public async Task LocalProxy_UsesEffectiveClientIp_AndGlobalLimitCoversDifferentIpsOnlyOnAdminLogin()
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = Configure(parent, perIp: 1, global: 2);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendFromAsync(factory, "127.0.0.1", "203.0.113.10")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendFromAsync(factory, "127.0.0.1", "203.0.113.11")).StatusCode);
        var limited = await SendFromAsync(factory, "127.0.0.1", "203.0.113.12");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null);
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        using var customerLogin = await client.PostAsJsonAsync("/api/auth/customer-token",
            new CustomerTokenLoginRequest(DevelopmentAuthSeedValues.DefaultCustomerToken));
        Assert.Equal(HttpStatusCode.OK, customerLogin.StatusCode);
        var customer = (await customerLogin.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/security/me")).StatusCode);
    }

    [Theory]
    [InlineData("PerIpPermitLimit", "0")]
    [InlineData("GlobalPermitLimit", "0")]
    [InlineData("WindowSeconds", "0")]
    public async Task InvalidConfigurationFailsStartupInsteadOfDisablingProtection(string key, string value)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Authentication:AdminLoginRateLimit:{key}"] = value
        });
        builder.Services.AddAdminLoginRateLimiting();
        using var host = builder.Build();
        // Exercise the real ValidateOnStart registration without the test web host's
        // asynchronous startup/disposal race masking the validation exception.
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData(121, 16)]
    [InlineData(5, 1025)]
    public async Task OversizedCredentialsAreRejectedBeforeRepositoryOrPasswordHash(int userNameLength, int passwordLength)
    {
        var users = new CountingUsers();
        var passwords = new CountingPasswords();
        var service = new PilotAuthenticationService(users, null!, null!, null!, passwords, null!, null!);
        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => service.LoginAdminAsync(
            new AdminLoginRequest(new string('u', userNameLength), new string('p', passwordLength)), CancellationToken.None));
        Assert.Equal("Credenciales administrativas invalidas.", error.Message);
        Assert.Equal(0, users.ReadCount);
        Assert.Equal(0, passwords.VerifyCount);
    }

    [Fact]
    public async Task ExistingCredentialsAtSupportedLengthLimitsStillAuthenticate()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = Client(factory);
        var user = factory.Services.GetRequiredService<InMemoryDataStore>().AdminUsers.Single();
        var password = new string('p', 1024);
        using var scope = factory.Services.CreateScope();
        user.UserName = new string('u', 120);
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHashService>().HashPassword(password);
        using var response = await client.PostAsJsonAsync(LoginPath, new AdminLoginRequest(user.UserName, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static WebApplicationFactory<Program> Configure(AuthApiAuthorizationTests.AuthApiFactory parent,
        int perIp, int global, int seconds = 60) => parent.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Authentication:AdminLoginRateLimit:PerIpPermitLimit"] = perIp.ToString(),
                    ["Authentication:AdminLoginRateLimit:GlobalPermitLimit"] = global.ToString(),
                    ["Authentication:AdminLoginRateLimit:WindowSeconds"] = seconds.ToString()
                }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestPeerStartupFilter>());
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> SendFromAsync(WebApplicationFactory<Program> factory, string peer, string forwarded)
    {
        using var client = Client(factory);
        client.DefaultRequestHeaders.Add("X-Test-Peer", peer);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwarded);
        return await client.PostAsJsonAsync(LoginPath, Invalid);
    }

    // Test-host transport only. Runs before the real ForwardedHeaders middleware;
    // production contains no header or hook that can replace the connection peer.
    private sealed class TestPeerStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers["X-Test-Peer"], out var peer))
                    context.Connection.RemoteIpAddress = peer;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class CountingPasswords : IPasswordHashService
    {
        public int VerifyCount { get; private set; }
        public string HashPassword(string password) => throw new NotSupportedException();
        public bool VerifyPassword(string passwordHash, string password) { VerifyCount++; return false; }
    }

    private sealed class CountingUsers : IAdminUserRepository
    {
        public int ReadCount { get; private set; }
        public Task<AdminUser?> GetByUserNameAsync(string userName, CancellationToken cancellationToken)
        { ReadCount++; return Task.FromResult<AdminUser?>(null); }
        public Task<IReadOnlyList<AdminUser>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdminUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(AdminUser adminUser, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryChangePasswordAsync(Guid userId, string expectedHash, string newHash, AuditLog audit,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
