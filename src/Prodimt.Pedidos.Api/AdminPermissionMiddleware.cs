using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.Security;

namespace Prodimt.Pedidos.Api;

/// <summary>One server-side gate covers existing and future administrative routes; unclassified routes fail closed.</summary>
public sealed class AdminPermissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SecurityService security)
    {
        var path = context.Request.Path.Value?.TrimEnd('/').ToLowerInvariant() ?? "";
        if (!path.StartsWith("/api/admin/", StringComparison.Ordinal) && path != "/api/admin")
        {
            await next(context); return;
        }
        context.Response.Headers.CacheControl = "no-store";
        // Authentication and AdminAccess policies run before this middleware.
        if (context.User.Identity?.IsAuthenticated != true)
        { context.Response.StatusCode = 401; return; }
        if (context.User.FindFirstValue(ProdimtAuthClaims.ActorType) != ProdimtActorTypes.Admin)
        { context.Response.StatusCode = 403; return; }
        if (!Guid.TryParse(context.User.FindFirstValue(ProdimtAuthClaims.UserId), out var userId))
        { context.Response.StatusCode = 401; return; }
        var key = RequiredPermission(context.Request.Method, path);
        if (key == "__unmapped__" || (key is not null && !(await security.GetPermissionsAsync(userId, context.RequestAborted))
            .Intersect(AcceptedPermissions(context.Request.Method, path), StringComparer.Ordinal).Any()))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "No tienes permiso para realizar esta acción.", permission = key }, context.RequestAborted);
            return;
        }
        await next(context);
    }

    public static string? RequiredPermission(string method, string normalizedPath)
    {
        var path = normalizedPath.TrimEnd('/').ToLowerInvariant();
        var get = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        if ((get && path == "/api/admin/security/me") || (HttpMethods.IsPost(method) && path == "/api/admin/account/change-password")) return null;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3) return "__unmapped__";
        return segments[2] switch
        {
            "delivery" => "delivery.manage",
            "collections" => CollectionPermission(method, segments),
            "ordering-schedules" => "customers.manage",
            "security" or "users" => "security.manage",
            "operations" => OperationPermission(method, path, segments),
            "machine-workspace" => MachineWorkspacePermission(method, segments),
            "product-setups" => get ? "customers.manage" : "pricing.manage",
            "daily-orders" or "import" => "imports.manage",
            "machines" => get ? "machines.read" : "machines.manage",
            "products" => segments.Contains("consolidation") ? "products.manage" : get ? "products.read" : "products.manage",
            "orders" => path.EndsWith("/next-delivery-date", StringComparison.Ordinal) ? "orders.capture" :
                path.EndsWith("/notification", StringComparison.Ordinal) || path.EndsWith("/review", StringComparison.Ordinal) || path.EndsWith("/pending-review", StringComparison.Ordinal) ? "orders.review" : get ? "orders.read" : "orders.capture",
            "customers" => CustomerPermission(segments, get),
            _ => "__unmapped__"
        };
    }
    public static IReadOnlyList<string> AcceptedPermissions(string method, string path)
    {
        var primary = RequiredPermission(method, path);
        if (primary is null) return [];
        if (primary == MachineWorkspaceAccess.WorkPermission) return [primary, MachineWorkspaceAccess.ManagePermission];
        if (primary == "operations.collections.read") return [primary, "operations.collections.receive"];
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)) return [primary];
        if (path.TrimEnd('/').EndsWith("/orders/next-delivery-date", StringComparison.OrdinalIgnoreCase))
            return ["orders.capture", "tokens.manage"];
        // These are the minimum reference reads needed to use a granted action in the existing editors.
        return primary switch
        {
            "customers.read" => ["customers.read", "customers.manage", "tokens.manage", "pricing.manage", "orders.capture"],
            "products.read" => ["products.read", "products.manage", "customers.manage", "pricing.manage", "orders.capture", "operations.produce"],
            "machines.read" => ["machines.read", "machines.manage", "customers.manage", "orders.capture", "operations.read", "operations.acknowledge", "operations.assign", "operations.produce", "operations.allocate"],
            "orders.read" => ["orders.read", "orders.review", "orders.capture"],
            "operations.read" => ["operations.read", "operations.acknowledge", "operations.assign", "operations.produce", "operations.allocate"],
            "customers.manage" when path.Contains("/product-setups", StringComparison.OrdinalIgnoreCase) => ["customers.manage", "pricing.manage"],
            _ => [primary]
        };
    }
    private static string CollectionPermission(string method, string[] segments)
    {
        var read = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        if (read && segments.Length == 4 && segments[3] == "cashier") return "operations.collections.read";
        if (read && segments.Length == 5 && segments[3] == "customers" && Guid.TryParse(segments[4], out _))
            return "operations.collections.read";
        if (read && segments.Length == 5 && segments[3] == "trips" && Guid.TryParse(segments[4], out _))
            return "operations.collections.read";
        if (HttpMethods.IsPost(method) && segments.Length == 4 && segments[3] == "receipts")
            return "operations.collections.receive";
        return "__unmapped__";
    }
    private static string MachineWorkspacePermission(string method, string[] segments)
    {
        var read = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        var post = HttpMethods.IsPost(method);
        if (segments.Length == 4 && segments[3] == "machines" && read) return MachineWorkspaceAccess.WorkPermission;
        if (segments.Length == 4 && segments[3] == "workers" && post) return MachineWorkspaceAccess.ManagePermission;
        if (segments.Length == 6 && segments[3] == "machines" && Guid.TryParse(segments[4], out _)
            && segments[5] == "operators" && (read || HttpMethods.IsPut(method))) return MachineWorkspaceAccess.ManagePermission;
        if (segments.Length < 4 || !Guid.TryParse(segments[3], out _)) return "__unmapped__";
        if (read && (segments.Length == 4 || (segments.Length == 5 && segments[4] == "team")))
            return MachineWorkspaceAccess.WorkPermission;
        if (post && segments.Length == 5 && segments[4] is "capture" or "lots" or "allocate" or "handoff" or "release")
            return MachineWorkspaceAccess.WorkPermission;
        if (post && segments.Length == 6 && segments[4] == "team" && segments[5] is "daily" or "regular")
            return MachineWorkspaceAccess.WorkPermission;
        return "__unmapped__";
    }

    private static string OperationPermission(string method, string path, string[] segments)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) return "operations.read";
        if (!HttpMethods.IsPost(method)) return "__unmapped__";
        if (segments.Length == 6 && segments[3] == "changes" && segments[5] == "acknowledge") return "operations.acknowledge";
        if (path == "/api/admin/operations/production/batches") return "operations.produce";
        if (path == "/api/admin/operations/production/allocations" ||
            (segments.Length == 7 && segments[3] == "production" && segments[4] == "allocations" && segments[6] == "reverse")) return "operations.allocate";
        if (segments.Length == 8 && segments[3] == "orders" && segments[5] == "lines" && segments[7] == "machine") return "operations.assign";
        return "__unmapped__";
    }
    private static string CustomerPermission(string[] segments, bool get)
    {
        if (segments.Length == 4 && segments[3] is "routes" or "route-assignments") return "customers.manage";
        if (segments.Contains("access-tokens") || segments.Last() == "phone") return "tokens.manage";
        if (segments.Contains("prices")) return "pricing.manage";
        if (segments.Contains("orders") || segments.Last() is "pending-orders" or "order-template") return "orders.capture";
        return get ? "customers.read" : "customers.manage";
    }
}
