using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.Collections;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;

namespace Prodimt.Pedidos.Api;

public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var own = endpoints.MapGroup("/api/delivery").RequireAuthorization("AdminAccess").WithTags("Cobranza de mi reparto");
        own.MapGet("/assignments/{id:guid}/collection-preview", (Guid id, decimal quantity, HttpContext context,
            CollectionService service, SecurityService security, CancellationToken ct) => OwnAsync(context, security,
                () => service.PreviewAsync(id, quantity, Actor(context.User), false, ct)));
        own.MapGet("/trips/{id:guid}/collections", (Guid id, HttpContext context, CollectionService service,
            SecurityService security, CancellationToken ct) => OwnAsync(context, security,
                () => service.TripAsync(id, Actor(context.User), false, ct)));

        var managed = endpoints.MapGroup("/api/admin/delivery").RequireAuthorization("AdminAccess").WithTags("Cobranza del reparto");
        managed.MapGet("/assignments/{id:guid}/collection-preview", (Guid id, decimal quantity, HttpContext context,
            CollectionService service, SecurityService security, CancellationToken ct) => OwnAsync(context, security,
                () => service.PreviewAsync(id, quantity, Actor(context.User), true, ct)));
        managed.MapGet("/trips/{id:guid}/collections", (Guid id, HttpContext context, CollectionService service,
            SecurityService security, CancellationToken ct) => OwnAsync(context, security,
                () => service.TripAsync(id, Actor(context.User), true, ct)));

        var admin = endpoints.MapGroup("/api/admin/collections").RequireAuthorization("AdminAccess").WithTags("Caja y adeudos");
        admin.MapGet("/cashier", (DateOnly? date, string? routeCode, ClaimsPrincipal user, CollectionService service, CancellationToken ct)
            => ExecuteAsync(() => service.CashierDashboardAsync(date, routeCode, Actor(user), ct)));
        admin.MapGet("/customers/{id:guid}", (Guid id, CollectionService service, CancellationToken ct)
            => ExecuteAsync(() => service.CustomerAccountAsync(id, ct)));
        admin.MapGet("/trips/{id:guid}", (Guid id, ClaimsPrincipal user, CollectionService service, CancellationToken ct)
            => ExecuteAsync(() => service.TripAsync(id, Actor(user), true, ct)));
        admin.MapPost("/receipts", (ReceiveCollectionCashRequest request, ClaimsPrincipal user,
            CollectionService service, CancellationToken ct) => ExecuteAsync(() => service.ReceiveCashAsync(request, Actor(user), ct)));
        return endpoints;
    }

    private static async Task<IResult> OwnAsync<T>(HttpContext context, SecurityService security, Func<Task<T>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(context.User.FindFirstValue(ProdimtAuthClaims.UserId), out var id)) return Results.Unauthorized();
        var permissions = await security.GetPermissionsAsync(id, context.RequestAborted);
        if (!permissions.Contains("operations.collections.work"))
            return Results.Json(new { error = "No tienes permiso para consultar la cobranza de reparto." }, statusCode: 403);
        return await ExecuteAsync(action);
    }

    private static AdminActorContext Actor(ClaimsPrincipal principal) => new(principal.FindFirstValue(ProdimtAuthClaims.UserId),
        principal.FindFirstValue(ProdimtAuthClaims.DisplayName));

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "No tienes acceso a esta cobranza." }, statusCode: 403); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message, retrySameOperation = ex.RetrySameOperation }); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message, retrySameOperation = false }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException) { return Results.Problem("La cobranza requiere persistencia transaccional.", statusCode: 503); }
    }
}
