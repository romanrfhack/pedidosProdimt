using Prodimt.Pedidos.Application.AdminOperations;
using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Api;

public static class AdminOperationsEndpoints
{
    public static IEndpointRouteBuilder MapAdminOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/operations/plan", async (
            DateOnly? date, string? routeCode, AdminOperationsService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetPlanAsync(date, routeCode, cancellationToken)))
            .RequireAuthorization("AdminAccess")
            .WithName("GetOperationsPlan");
        endpoints.MapGet("/api/admin/operations/changes", async (
            DateOnly? date, string? routeCode, Guid? machineId, bool? pendingOnly, OrderChangeNoticeService service,
            CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(date, routeCode, machineId,
                pendingOnly ?? true, cancellationToken)))
            .RequireAuthorization("AdminAccess").WithName("GetOrderChangeNotices");
        endpoints.MapPost("/api/admin/operations/changes/{id:guid}/acknowledge", async (
            Guid id, AcknowledgeOrderChangeRequest request, ClaimsPrincipal user, OrderChangeNoticeService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var actor = new AdminActorContext(user.FindFirstValue(ProdimtAuthClaims.UserId),
                    user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);
                return Results.Ok(await service.AcknowledgeAsync(id, request, actor, cancellationToken));
            }
            catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
            catch (NotSupportedException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
        }).RequireAuthorization("AdminAccess").WithName("AcknowledgeOrderChangeNotice");
        return endpoints;
    }
}
