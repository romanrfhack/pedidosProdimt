using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;

namespace Prodimt.Pedidos.Api;

public static class OrderCaptureEndpoints
{
    public static void MapOrderCaptureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/customers/{customerId:guid}/orders").RequireAuthorization("AdminAccess");
        group.MapPost("/preview", async (Guid customerId, PreviewOrderCaptureRequest request, ClaimsPrincipal user,
            AdminOrderCaptureService service, SecurityService security, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.PreviewAsync(customerId, request, token,
                await SecurityEndpoints.GetOrderActorAsync(user, security, token)))));
        group.MapPost("/capture", async (Guid customerId, SubmitOrderCaptureRequest request, ClaimsPrincipal user,
            AdminOrderCaptureService service, SecurityService security, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.SubmitAsync(customerId, request,
                await SecurityEndpoints.GetOrderActorAsync(user, security, token), token))));
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (DeliveryDateChangedException ex) { return Results.Conflict(new { error = ex.Message,
            code = "DeliveryDateChanged", ex.DeliveryDate, ex.ServerNow }); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException ex) { return Results.Problem(ex.Message, statusCode: 503,
            extensions: new Dictionary<string, object?> { ["code"] = "PersistenceNotSupported" }); }
    }
}
