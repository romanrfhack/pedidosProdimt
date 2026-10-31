using System.Security.Claims;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Api;

public static class CustomerOrderCaptureEndpoints
{
    public static void MapCustomerOrderCaptureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/orders/next-delivery-date", (IDateTimeProvider clock) =>
        {
            var now = clock.Now;
            return Results.Ok(new { deliveryDate = DeliveryDatePolicy.GetDate(now), serverNow = now });
        }).RequireAuthorization("AdminAccess");

        var group = endpoints.MapGroup("/api/customer-orders/{customerId:guid}").RequireAuthorization("CustomerAccess");
        group.MapGet("/next", async (Guid customerId, ClaimsPrincipal user, CustomerOrderCaptureService service, CancellationToken token) =>
            await ExecuteAsync(user, customerId, async () => Results.Ok(await service.GetNextAsync(customerId, token))));
        group.MapPost("/preview", async (Guid customerId, ClaimsPrincipal user, PreviewCustomerOrderCaptureRequest request,
            CustomerOrderCaptureService service, CancellationToken token) =>
            await ExecuteAsync(user, customerId, async () => Results.Ok(await service.PreviewAsync(customerId, request, token))));
        group.MapPost("/capture", async (Guid customerId, ClaimsPrincipal user, SubmitCustomerOrderCaptureRequest request,
            CustomerOrderCaptureService service, CancellationToken token) =>
            await ExecuteAsync(user, customerId, async () => Results.Ok(await service.SubmitAsync(customerId, request, token))));
        group.MapPost("/next/no-order", async (Guid customerId, ClaimsPrincipal user, MarkNextCustomerNoOrderRequest request,
            CustomerOrderCaptureService service, CancellationToken token) =>
            await ExecuteAsync(user, customerId, async () => Results.Ok(await service.MarkNoOrderAsync(customerId, request, token))));
    }

    private static async Task<IResult> ExecuteAsync(ClaimsPrincipal user, Guid customerId, Func<Task<IResult>> action)
    {
        if (!Guid.TryParse(user.FindFirstValue(ProdimtAuthClaims.CustomerId), out var authorized) || authorized != customerId)
            return Results.Forbid();
        try { return await action(); }
        catch (DeliveryDateChangedException ex) { return Results.Conflict(new { error = ex.Message,
            code = "DeliveryDateChanged", ex.DeliveryDate, ex.ServerNow }); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException) { return Results.Problem("La captura requiere persistencia transaccional.", statusCode: 503,
            extensions: new Dictionary<string, object?> { ["code"] = "PersistenceNotSupported" }); }
    }
}
