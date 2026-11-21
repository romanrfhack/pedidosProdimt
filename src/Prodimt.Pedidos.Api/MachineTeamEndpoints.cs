using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Api;

public static class MachineTeamEndpoints
{
    public static IEndpointRouteBuilder MapMachineTeamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/machine-workspace").RequireAuthorization("AdminAccess");
        group.MapGet("/{machineId:guid}/team", async (Guid machineId, DateOnly? date, ClaimsPrincipal user,
            MachineTeamService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.GetAsync(machineId, date, Actor(user), token))));
        group.MapPost("/{machineId:guid}/team/daily", async (Guid machineId, ChangeMachineTeamRequest request,
            ClaimsPrincipal user, MachineTeamService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.ChangeDailyAsync(machineId, request, Actor(user), token))));
        group.MapPost("/{machineId:guid}/team/regular", async (Guid machineId, SaveMachineTeamTemplateRequest request,
            ClaimsPrincipal user, MachineTeamService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.SaveTemplateAsync(machineId, request, Actor(user), token))));
        group.MapPost("/workers", async (RegisterMachineWorkerRequest request, ClaimsPrincipal user,
            MachineTeamService service, CancellationToken token) =>
            await ExecuteAsync(async () => Results.Ok(await service.RegisterWorkerAsync(request, Actor(user), token))));
        return endpoints;
    }

    private static AdminActorContext Actor(ClaimsPrincipal user) => new(user.FindFirstValue(ProdimtAuthClaims.UserId),
        user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message, retrySameOperation = ex.RetrySameOperation }); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message, retrySameOperation = true }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException) { return Results.Problem("Esta operación requiere SQL Server y persistencia transaccional.", statusCode: 503); }
    }
}
