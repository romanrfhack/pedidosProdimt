using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Api;

public static class DailyOrderEndpoints
{
    public static IEndpointRouteBuilder MapDailyOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/daily-orders").RequireAuthorization("AdminAccess");
        group.MapPost("/validate", async (DailyOrderImportRequest request, AdminDailyOrderService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.ValidateAsync(request, token))));
        group.MapPost("/apply", async (DailyOrderImportRequest request, ClaimsPrincipal user, AdminDailyOrderService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.ApplyAsync(request, Actor(user), token))));
        endpoints.MapPost("/api/admin/orders/{orderId:guid}/corrections", async (Guid orderId, CorrectOrderRequest request,
            ClaimsPrincipal user, AdminDailyOrderService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.CorrectAsync(orderId, request, Actor(user), token))))
            .RequireAuthorization("AdminAccess");
        return endpoints;
    }

    private static AdminActorContext Actor(ClaimsPrincipal user) => new(
        user.FindFirstValue(ProdimtAuthClaims.UserId), user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (NotSupportedException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, extensions: new Dictionary<string, object?> { ["code"] = "PersistenceNotSupported" }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    }
}
