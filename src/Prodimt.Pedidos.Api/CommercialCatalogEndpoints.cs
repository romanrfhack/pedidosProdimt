using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;

namespace Prodimt.Pedidos.Api;

public static class CommercialCatalogEndpoints
{
    public static void MapCommercialCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin").RequireAuthorization("AdminAccess");
        group.MapGet("/customers/{customerId:guid}/prices", (Guid customerId, DateOnly? asOf,
            CustomerProductPricingService service, CancellationToken token) => Execute(() => service.GetAsync(customerId, asOf, token)));
        group.MapPost("/customers/{customerId:guid}/products/{productId:guid}/prices", (Guid customerId, Guid productId,
            ChangeCustomerProductPriceRequest request, ClaimsPrincipal user, CustomerProductPricingService service,
            CancellationToken token) => Execute(() => service.ChangeAsync(customerId, productId, request, Actor(user), token)));
        group.MapGet("/products/consolidation/candidates", (ProductConsolidationService service, CancellationToken token)
            => Execute(() => service.GetCandidatesAsync(token)));
        group.MapPost("/products/consolidation/preview", (PreviewProductConsolidationRequest request,
            ProductConsolidationService service, CancellationToken token) => Execute(() => service.PreviewAsync(request, token)));
        group.MapPost("/products/consolidation/apply", (ApplyProductConsolidationRequest request, ClaimsPrincipal user,
            ProductConsolidationService service, CancellationToken token) => Execute(() => service.ApplyAsync(request, Actor(user), token)));
    }

    private static AdminActorContext Actor(ClaimsPrincipal user) => new(user.FindFirstValue(ProdimtAuthClaims.UserId),
        user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message, code = "CatalogChanged" }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message, code = "ValidationError" }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    }
}
