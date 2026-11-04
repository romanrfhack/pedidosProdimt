using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Api;

public static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/operations/production").RequireAuthorization("AdminAccess");
        group.MapGet("/", async (DateOnly? date, string? routeCode, ProductionManagementService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.GetDashboardAsync(date, routeCode, token))));
        group.MapPost("/batches", async (RegisterProductionRequest request, ClaimsPrincipal user,
            ProductionManagementService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.RegisterAsync(request, Actor(user), token))));
        group.MapPost("/allocations", async (AllocateProductionRequest request, ClaimsPrincipal user,
            ProductionManagementService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.AllocateAsync(request, Actor(user), token))));
        group.MapPost("/allocations/{id:guid}/reverse", async (Guid id, ReverseProductionAllocationRequest request,
            ClaimsPrincipal user, ProductionManagementService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.ReverseAsync(id, request, Actor(user), token))));
        endpoints.MapPost("/api/admin/operations/orders/{orderId:guid}/lines/{lineId:guid}/machine", async (
            Guid orderId, Guid lineId, AssignProductionMachineRequest request, ClaimsPrincipal user,
            ProductionManagementService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.AssignMachineAsync(orderId, lineId, request, Actor(user), token))))
            .RequireAuthorization("AdminAccess");
        return endpoints;
    }
    private static AdminActorContext Actor(ClaimsPrincipal user) => new(user.FindFirstValue(ProdimtAuthClaims.UserId),
        user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);
    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message, retrySameOperation = ex.RetrySameOperation }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException) { return Results.Problem("Esta operación requiere SQL Server y persistencia transaccional.", statusCode: 503); }
    }
}
