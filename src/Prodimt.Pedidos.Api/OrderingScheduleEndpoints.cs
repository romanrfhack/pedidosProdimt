using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Auth;
namespace Prodimt.Pedidos.Api;

public static class OrderingScheduleEndpoints
{
    public static void MapOrderingScheduleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/orders/{id:guid}/notification", async (Guid id,
            Prodimt.Pedidos.Application.AdminOrders.OrderDecisionNotificationService service, CancellationToken token) =>
        {
            try { return Results.Ok(await service.GetAsync(id, token)); }
            catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        }).RequireAuthorization("AdminAccess");
        var group = endpoints.MapGroup("/api/admin/ordering-schedules").RequireAuthorization("AdminAccess");
        group.MapGet("/", async (OrderingScheduleService service, CancellationToken token) => Results.Ok(await service.GetAsync(token)));
        group.MapPut("/routes", async (RouteScheduleUpdate request, ClaimsPrincipal user, OrderingScheduleService service, CancellationToken token) =>
            await Run(async () => await service.SetRouteAsync(request.RouteCode, new(request.Cutoff, request.ExpectedVersion), Actor(user), token)));
        group.MapPut("/customers/{id:guid}", async (Guid id, ScheduleUpdate request, ClaimsPrincipal user, OrderingScheduleService service, CancellationToken token) =>
            await Run(async () => await service.SetCustomerAsync(id, request, Actor(user), token)));
    }
    private static Prodimt.Pedidos.Application.AdminOrders.AdminActorContext Actor(ClaimsPrincipal user) =>
        new(user.FindFirstValue(ProdimtAuthClaims.UserId), user.FindFirstValue(ProdimtAuthClaims.DisplayName));
    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.NoContent(); }
        catch (CatalogConflictException e) { return Results.Conflict(new { error = e.Message }); }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
        catch (NotSupportedException) { return Results.Problem("La configuración requiere persistencia transaccional.", statusCode: 503); }
    }
}
public sealed record RouteScheduleUpdate(string RouteCode, TimeOnly? Cutoff, int ExpectedVersion);
