using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;

namespace Prodimt.Pedidos.Api;

public static class CustomerRouteAssignmentEndpoints
{
    public static void MapCustomerRouteAssignmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/customers").WithTags("Admin customers").RequireAuthorization("AdminAccess");
        group.MapGet("/routes", async (CustomerRouteAssignmentService service, CancellationToken token)
            => Results.Ok(await service.GetActiveRoutesAsync(token)));
        group.MapGet("/route-assignments", async (CustomerRouteAssignmentService service, CancellationToken token)
            => Results.Ok(await service.GetAssignmentsAsync(token)));
        group.MapPut("/{customerId:guid}/route", async (Guid customerId, UpdateCustomerRouteRequest request,
            ClaimsPrincipal user, CustomerRouteAssignmentService service, CancellationToken token) =>
        {
            var actor = new AdminActorContext(user.FindFirstValue(ProdimtAuthClaims.UserId),
                user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);
            try { return Results.Ok(await service.UpdateAsync(customerId, request, actor, token)); }
            catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message, code = "CatalogChanged" }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message, code = "ValidationError" }); }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        });
    }
}
