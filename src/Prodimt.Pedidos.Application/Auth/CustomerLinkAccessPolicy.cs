using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
namespace Prodimt.Pedidos.Application.Auth;

public static class CustomerLinkAccessPolicy
{
    public const string RequestOnlyClaim = "prodimt_request_only";
    public static DateTimeOffset? MaximumAccessAt(CustomerAccessToken token)
    {
        if (token.ExpiresAt is not { } expiry) return null;
        var date = DeliveryDatePolicy.NextBusinessDate(DeliveryDatePolicy.LocalDate(expiry));
        var local = date.ToDateTime(DeliveryDatePolicy.Cutoff, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, DeliveryDatePolicy.BusinessTimeZone.GetUtcOffset(local));
    }
    public static bool CanIdentify(CustomerAccessToken token, DateTimeOffset now) => token.IsActive &&
        (MaximumAccessAt(token) is not { } maximum || now < maximum);
    public static bool IsRequestOnly(CustomerAccessToken token, DateTimeOffset now) => token.ExpiresAt is { } expiry && now >= expiry;
    public static bool IsAllowedRequest(string method, string path)
    {
        var parts = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || parts[0] != "api" || parts[1] != "customer-orders" || !Guid.TryParse(parts[2], out _)) return false;
        if (method == "GET") return parts.Length == 4 && parts[3] is "next" or "additional-products";
        return method == "POST" && parts.Length == 4 && parts[3] is "preview" or "capture";
    }
}
