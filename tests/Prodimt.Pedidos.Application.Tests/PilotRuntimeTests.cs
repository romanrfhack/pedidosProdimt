using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Api;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class PilotRuntimeTests
{
    [Theory]
    [InlineData("Authentication:Jwt:SigningKey", null)]
    [InlineData("Authentication:Jwt:SigningKey", "too-short")]
    [InlineData("Authentication:Jwt:SigningKey", JwtSettings.DevelopmentSigningKey)]
    [InlineData("Authentication:Jwt:SigningKey", "CHANGE_ME_to_a_secret_before_running_the_pilot")]
    [InlineData("Authentication:Jwt:AccessTokenMinutes", "0")]
    [InlineData("Authentication:Jwt:AccessTokenMinutes", "1441")]
    [InlineData("Persistence:Provider", "InMemory")]
    [InlineData("ConnectionStrings:Pedidos", null)]
    [InlineData("DevelopmentSeed:Enabled", "true")]
    [InlineData("DevelopmentSeed:ApplyMigrations", "true")]
    public void Production_RejectsUnsafeConfiguration(string setting, string? value)
    {
        var values = ProductionValues();
        values[setting] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Throws<InvalidOperationException>(() =>
            PilotRuntimeConfiguration.ValidateProduction(configuration, JwtSettings.FromConfiguration(configuration)));
    }

    [Fact]
    public void Production_AcceptsExplicitSqlAndOwnKeyWithoutDemoOrAutomaticMigrations()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ProductionValues()).Build();

        PilotRuntimeConfiguration.ValidateProduction(configuration, JwtSettings.FromConfiguration(configuration));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/cliente")]
    [InlineData("/admin/login")]
    [InlineData("/admin/importar-pedidos")]
    public async Task PackagedUi_LoadsKnownNavigationRoutesWithoutCachingHtml(string path)
    {
        await using var host = await StaticHost.StartAsync();
        using var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("pilot-shell", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/API/unknown")]
    [InlineData("/health/unknown")]
    [InlineData("/api/main.js")]
    [InlineData("/health/main.js")]
    [InlineData("/unknown")]
    [InlineData("/missing.js")]
    public async Task MissingApiHealthAndAssets_NeverReturnSpaHtml(string path)
    {
        await using var host = await StaticHost.StartAsync();
        using var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("pilot-shell", await response.Content.ReadAsStringAsync());
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PackagedUi_ServesAssetsAndPreservesHealthEndpoint()
    {
        await using var host = await StaticHost.StartAsync();

        Assert.Equal("window.pilot = true;", await host.Client.GetStringAsync("/main.js"));
        using var health = await host.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("application/json", health.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("pilot-shell", await health.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PackagedUi_PostDoesNotExecuteSpaFallbackAndHeadHasNoBody()
    {
        await using var host = await StaticHost.StartAsync();
        using var post = await host.Client.PostAsync("/admin/pedidos", new StringContent(""));
        using var head = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/admin/pedidos"));

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.True(head.Content.Headers.ContentLength > 0);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task MissingFrontendBuild_Returns404InsteadOfSuccessWithEmptyPage()
    {
        await using var host = await StaticHost.StartAsync(includeIndex: false);
        using var response = await host.Client.GetAsync("/admin/login");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Dictionary<string, string?> ProductionValues() => new()
    {
        ["Authentication:Jwt:SigningKey"] = "synthetic-test-only-signing-key-2026-pilot",
        ["Authentication:Jwt:AccessTokenMinutes"] = "120",
        ["Persistence:Provider"] = "SqlServer",
        ["ConnectionStrings:Pedidos"] = "Server=localhost;Database=synthetic_pilot;Integrated Security=true",
        ["DevelopmentSeed:Enabled"] = "false",
        ["DevelopmentSeed:ApplyMigrations"] = "false"
    };

    private sealed class StaticHost(WebApplication app, string root) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public static async Task<StaticHost> StartAsync(bool includeIndex = true)
        {
            var root = Path.Combine(Path.GetTempPath(), $"prodimt-web-test-{Guid.NewGuid():N}");
            var webRoot = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(webRoot);
            if (includeIndex) await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><div>pilot-shell</div>");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "main.js"), "window.pilot = true;");
            // Even a mistakenly packaged file cannot shadow reserved server routes.
            foreach (var reserved in new[] { "api", "health" })
            {
                Directory.CreateDirectory(Path.Combine(webRoot, reserved));
                await File.WriteAllTextAsync(Path.Combine(webRoot, reserved, "main.js"), "reserved");
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Production", ContentRootPath = root, WebRootPath = webRoot
            });
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            app.UsePilotStaticFiles();
            app.MapGet("/health", () => Microsoft.AspNetCore.Http.Results.Ok(new { status = "ok" }));
            app.MapPilotWeb();
            await app.StartAsync();
            return new StaticHost(app, root);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
