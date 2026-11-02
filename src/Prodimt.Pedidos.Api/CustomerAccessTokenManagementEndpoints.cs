using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;

namespace Prodimt.Pedidos.Api;

public static class CustomerAccessTokenManagementEndpoints
{
    public static void MapCustomerAccessTokenManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/customers/{customerId:guid}/access-tokens")
            .RequireAuthorization("AdminAccess");
        group.MapPatch("/{tokenId:guid}", async (Guid customerId, Guid tokenId,
            UpdateCustomerAccessTokenRequest request, ClaimsPrincipal user,
            AdminCustomerAccessTokenService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Results.Ok(await service.UpdateAsync(customerId, tokenId, request, Actor(user), ct))))
            .WithName("UpdateCustomerAccessTokenMetadata");
        group.MapPost("/{tokenId:guid}/reveal", async (Guid customerId, Guid tokenId,
            ClaimsPrincipal user, AdminCustomerAccessTokenService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Results.Ok(await service.RevealAsync(customerId, tokenId, Actor(user), ct))))
            .WithName("RevealCustomerAccessToken");
    }

    private static AdminActorContext Actor(ClaimsPrincipal user)
        => new(user.FindFirstValue(ProdimtAuthClaims.UserId),
            user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (CustomerAccessTokenProtectionException ex) { return Results.Problem(ex.Message, statusCode: 503); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.NotFound(new { error = ex.Message }); }
    }
}
