using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Api;

public static class OrderCaptureEndpoints
{
    public static void MapOrderCaptureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/customers/{customerId:guid}/orders").RequireAuthorization("AdminAccess");
        group.MapPost("/preview", async (Guid customerId, PreviewOrderCaptureRequest request, AdminOrderCaptureService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.PreviewAsync(customerId, request, token))));
        group.MapPost("/capture", async (Guid customerId, SubmitOrderCaptureRequest request, ClaimsPrincipal user, AdminOrderCaptureService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.SubmitAsync(customerId, request,
                new(user.FindFirstValue(ProdimtAuthClaims.UserId), user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name), token))));
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException ex) { return Results.Problem(ex.Message, statusCode: 503,
            extensions: new Dictionary<string, object?> { ["code"] = "PersistenceNotSupported" }); }
    }
}
