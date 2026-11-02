using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Api;

public static class CustomerProductSetupEndpoints
{
    public static void MapCustomerProductSetupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/customer-orders/{customerId:guid}/additional-products", async (Guid customerId,
            ClaimsPrincipal user, CustomerProductSetupService service, CancellationToken token) =>
        {
            if (!Guid.TryParse(user.FindFirstValue(ProdimtAuthClaims.CustomerId), out var own) || own != customerId)
                return Results.Forbid();
            return await ExecuteAsync(async () => Results.Ok(await service.GetAdditionalAsync(customerId, token)));
        }).RequireAuthorization("CustomerAccess");
        endpoints.MapGet("/api/admin/customers/{customerId:guid}/additional-products", async (Guid customerId,
            CustomerProductSetupService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.GetAdditionalAsync(customerId, token))))
            .RequireAuthorization("AdminAccess");

        // The pilot administrator performs General Administration. Keep the
        // existing actor policy; clients never receive commercial configuration.
        var admin = endpoints.MapGroup("/api/admin/product-setups").RequireAuthorization("AdminAccess");
        admin.MapGet("/count", async (CustomerProductSetupService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(new { pendingCount = await service.CountOpenAsync(token) })));
        admin.MapGet("/", async (CustomerProductSetupService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.GetOpenAsync(token))));
        admin.MapGet("/{id:guid}", async (Guid id, CustomerProductSetupService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.GetAsync(id, token))));
        admin.MapGet("/{id:guid}/resolution-preview", async (Guid id, CustomerProductSetupService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.PreviewAsync(id, token))));
        admin.MapPost("/{id:guid}/complete", async (Guid id, CompleteProductSetupRequest request, ClaimsPrincipal user,
            CustomerProductSetupService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.CompleteAsync(id, request,
                new(user.FindFirstValue(ProdimtAuthClaims.UserId), user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name), token))));
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException) { return Results.Problem("Esta operación requiere persistencia transaccional.", statusCode: 503); }
    }
}
