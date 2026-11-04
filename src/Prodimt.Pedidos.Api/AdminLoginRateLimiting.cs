using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Prodimt.Pedidos.Api;

public sealed class AdminLoginRateLimitOptions
{
    public const string SectionName = "Authentication:AdminLoginRateLimit";

    [Range(1, 1_000)]
    public int PerIpPermitLimit { get; set; } = 10;

    [Range(1, 10_000)]
    public int GlobalPermitLimit { get; set; } = 100;

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Limits anonymous password work without storing or locking an account.</summary>
public static class AdminLoginRateLimiting
{
    public const string PolicyName = "admin-login";
    private sealed record AdminLoginEndpoint;
    private static readonly EventId RejectedEvent = new(1801, "AdminLoginRateLimited");

    public static IServiceCollection AddAdminLoginRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AdminLoginRateLimitOptions>()
            .BindConfiguration(AdminLoginRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<AdminLoginRateLimitOptions>>((options, configured) =>
        {
            var settings = configured.Value;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                IsAdminLogin(context)
                    ? RateLimitPartition.GetFixedWindowLimiter(PolicyName,
                        _ => Window(settings.GlobalPermitLimit, settings.WindowSeconds))
                    : RateLimitPartition.GetNoLimiter("other-endpoints"));
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                // Only the already-validated connection address is trusted, never a raw request header.
                context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
                _ => Window(settings.PerIpPermitLimit, settings.WindowSeconds)));
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (!IsAdminLogin(context.HttpContext)) return;
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Prodimt.Pedidos.Api.AdminLoginRateLimiting")
                    .LogInformation(RejectedEvent, "Administrative login rate limit reached.");
                await response.WriteAsJsonAsync(new
                {
                    error = "Demasiados intentos de acceso. Espera y vuelve a intentar."
                }, cancellationToken);
            };
        });
        return services;
    }

    public static RouteHandlerBuilder RequireAdminLoginRateLimiting(this RouteHandlerBuilder endpoint) =>
        endpoint.WithMetadata(new AdminLoginEndpoint()).RequireRateLimiting(PolicyName);

    private static bool IsAdminLogin(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<AdminLoginEndpoint>() is not null;

    private static FixedWindowRateLimiterOptions Window(int permits, int seconds) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromSeconds(seconds),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
