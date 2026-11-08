using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;

namespace Prodimt.Pedidos.Api;

public static class DailyOrderEndpoints
{
    public static IEndpointRouteBuilder MapDailyOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/daily-orders").RequireAuthorization("AdminAccess");
        group.MapPost("/validate", async (DailyOrderImportRequest request, ClaimsPrincipal user,
            AdminDailyOrderService service, SecurityService security, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.ValidateAsync(request, token,
                await SecurityEndpoints.GetOrderActorAsync(user, security, token)))));
        group.MapPost("/apply", async (DailyOrderImportRequest request, ClaimsPrincipal user,
            AdminDailyOrderService service, SecurityService security, OrderDispatchPreparation dispatch,
            CancellationToken token) =>
            await ExecuteAsync(async () =>
            {
                var result = await service.ApplyAsync(request,
                    await SecurityEndpoints.GetOrderActorAsync(user, security, token), token);
                if (result.Applied)
                    await dispatch.AfterSaveAsync(result.Orders.Where(row => row.Action == "order" && row.OrderId.HasValue)
                        .Select(row => row.OrderId!.Value), token);
                return Results.Ok(result);
            }));
        endpoints.MapPost("/api/admin/orders/{orderId:guid}/corrections", async (Guid orderId, CorrectOrderRequest request,
            ClaimsPrincipal user, AdminDailyOrderService service, SecurityService security, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.CorrectAsync(orderId, request,
                await SecurityEndpoints.GetOrderActorAsync(user, security, token), token))))
            .RequireAuthorization("AdminAccess");
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (NotSupportedException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, extensions: new Dictionary<string, object?> { ["code"] = "PersistenceNotSupported" }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    }
}
