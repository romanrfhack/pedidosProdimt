using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;

namespace Prodimt.Pedidos.Api;

public static class CustomerReminderEndpoints
{
    public static void MapCustomerReminderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPatch("/api/admin/customers/{customerId:guid}/phone", async (
            Guid customerId, UpdateCustomerPhoneRequest request, ClaimsPrincipal user,
            AdminCustomerCatalogService service, CancellationToken cancellationToken) =>
        {
            try
            {
                await service.UpdatePhoneNumberAsync(customerId, request,
                    new AdminActorContext(user.FindFirstValue(ProdimtAuthClaims.UserId),
                        user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name), cancellationToken);
                return Results.NoContent();
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.NotFound(new { error = ex.Message }); }
        }).RequireAuthorization("AdminAccess").WithName("UpdateCustomerReminderPhone");
    }
}
