using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Delivery;
using Prodimt.Pedidos.Application.Security;

namespace Prodimt.Pedidos.Api;

public static class DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var own = endpoints.MapGroup("/api/delivery").RequireAuthorization("AdminAccess").WithTags("Mi reparto");
        own.MapGet("", (DateOnly? date, HttpContext context, DeliveryManagementService service, SecurityService security, CancellationToken ct)
            => OwnAsync(context, security, false, async _ => Results.Ok(await service.DashboardAsync(date, null, Actor(context.User), false, ct))));
        own.MapGet("/trips/{id:guid}", (Guid id, HttpContext context, DeliveryManagementService service, SecurityService security, CancellationToken ct)
            => OwnAsync(context, security, false, async _ => Results.Ok(await service.TripAsync(id, Actor(context.User), false, ct))));
        own.MapPost("/prepare", (PrepareDeliveryRequest request, HttpContext context, DeliveryManagementService service, SecurityService security, CancellationToken ct)
            => OwnAsync(context, security, true, async _ => Results.Ok(await service.PrepareAsync(request, Actor(context.User), false, ct))));
        own.MapPost("/trips/{id:guid}/planned-count", (Guid id, CountPlannedDeliveryLineRequest request, HttpContext context,
            DeliveryManagementService service, SecurityService security, CancellationToken ct)
            => OwnAsync(context, security, true, async _ => Results.Ok(await service.CountPlannedAsync(id, request, Actor(context.User), false, ct))));
        own.MapPost("/trips/{id:guid}/depart", (Guid id, DepartDeliveryTripRequest request, HttpContext context,
            DeliveryManagementService service, SecurityService security, CancellationToken ct)
            => OwnAsync(context, security, true, async _ => Results.Ok(await service.DepartAsync(id, request, Actor(context.User), false, ct))));
        foreach (var stage in new[] { "count", "load", "deliver", "return" })
            own.MapPost("/assignments/{id:guid}/" + stage, (Guid id, RecordDeliveryStageRequest request, HttpContext context,
                DeliveryManagementService service, SecurityService security, CancellationToken ct)
                => OwnAsync(context, security, true, async _ => Results.Ok(await service.RecordAsync(id, stage, request, Actor(context.User), false, ct))));
        own.MapPost("/trips/{id:guid}/close", (Guid id, CloseDeliveryTripRequest request, HttpContext context,
            DeliveryManagementService service, SecurityService security, CancellationToken ct)
            => OwnAsync(context, security, true, async _ => Results.Ok(await service.CloseAsync(id, request, Actor(context.User), false, ct))));

        var admin = endpoints.MapGroup("/api/admin/delivery").RequireAuthorization("AdminAccess").WithTags("Gestión del reparto");
        admin.MapGet("", (DateOnly? date, string? routeCode, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.DashboardAsync(date, routeCode, Actor(user), true, ct))));
        admin.MapGet("/trips/{id:guid}", (Guid id, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.TripAsync(id, Actor(user), true, ct))));
        admin.MapPost("/prepare", (PrepareDeliveryRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.PrepareAsync(request, Actor(user), true, ct))));
        admin.MapPost("/routes", (SaveDeliveryRouteRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.SaveRouteAsync(request, Actor(user), ct))));
        admin.MapPost("/routes/day-driver", (SaveDeliveryDayDriverRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.SaveDayDriverAsync(request, Actor(user), ct))));
        admin.MapPut("/customers/{customerId:guid}/location", (Guid customerId, SaveDeliveryLocationRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.SaveLocationAsync(customerId, request, Actor(user), ct))));
        admin.MapPost("/trips", (CreateDeliveryTripRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.CreateTripAsync(request, Actor(user), ct))));
        admin.MapPost("/trips/{id:guid}/assignments", (Guid id, AssignDeliveryLineRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.AssignAsync(id, request, Actor(user), ct))));
        admin.MapPost("/trips/{id:guid}/planned-count", (Guid id, CountPlannedDeliveryLineRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.CountPlannedAsync(id, request, Actor(user), true, ct))));
        admin.MapPost("/trips/{id:guid}/depart", (Guid id, DepartDeliveryTripRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.DepartAsync(id, request, Actor(user), true, ct))));
        foreach (var stage in new[] { "count", "load", "deliver", "return" })
            admin.MapPost("/assignments/{id:guid}/" + stage, (Guid id, RecordDeliveryStageRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
                => ExecuteAsync(async () => Results.Ok(await service.RecordAsync(id, stage, request, Actor(user), true, ct))));
        admin.MapPost("/trips/{id:guid}/close", (Guid id, CloseDeliveryTripRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.CloseAsync(id, request, Actor(user), true, ct))));
        admin.MapPost("/assignments/{id:guid}/release", (Guid id, ReleaseDeliveryAssignmentRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.ReleaseAsync(id, request, Actor(user), ct))));
        admin.MapPost("/assignments/{id:guid}/instructions", (Guid id, ReleaseDeliveryAssignmentRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.RefreshInstructionsAsync(id, request, Actor(user), ct))));
        admin.MapPost("/assignments/{id:guid}/transfer", (Guid id, TransferDeliveryRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.TransferAsync(id, request, Actor(user), ct))));
        admin.MapPost("/assignments/{id:guid}/adjust", (Guid id, AdjustDeliveryRequest request, ClaimsPrincipal user, DeliveryManagementService service, CancellationToken ct)
            => ExecuteAsync(async () => Results.Ok(await service.AdjustAsync(id, request, Actor(user), ct))));
        return endpoints;
    }
    private static async Task<IResult> OwnAsync(HttpContext context, SecurityService security, bool write, Func<bool, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(context.User.FindFirstValue(ProdimtAuthClaims.UserId), out var id)) return Results.Unauthorized();
        var permissions = await security.GetPermissionsAsync(id, context.RequestAborted);
        var manage = permissions.Contains("delivery.manage");
        if (!manage && !permissions.Contains("delivery.operate.own") && (write || !permissions.Contains("delivery.read.own")))
            return Results.Json(new { error = "No tienes permiso para consultar u operar tu reparto." }, statusCode: 403);
        return await ExecuteAsync(() => action(manage));
    }
    private static AdminActorContext Actor(ClaimsPrincipal principal) => new(principal.FindFirstValue(ProdimtAuthClaims.UserId),
        principal.FindFirstValue(ProdimtAuthClaims.DisplayName));
    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "Esta vuelta pertenece a otro repartidor." }, statusCode: 403); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException) { return Results.Problem("El reparto requiere SQL Server con persistencia transaccional.", statusCode: 503); }
    }
}
