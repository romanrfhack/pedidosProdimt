using System.Security.Claims;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;

namespace Prodimt.Pedidos.Api;

public static class MachineWorkspaceEndpoints
{
    private const string Prefix = "/api/admin/machine-workspace";

    public static IEndpointRouteBuilder MapMachineWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Prefix).RequireAuthorization("AdminAccess");
        group.MapGet("/machines", (ClaimsPrincipal user, MachineWorkspaceManagement service, CancellationToken token) =>
            ExecuteAsync(() => service.GetMachinesAsync(Actor(user), token)));
        group.MapGet("/machines/{machineId:guid}/operators", (Guid machineId, ClaimsPrincipal user,
            MachineWorkspaceManagement service, CancellationToken token) =>
            ExecuteAsync(() => service.GetOperatorsAsync(machineId, Actor(user), token)));
        group.MapPut("/machines/{machineId:guid}/operators", (Guid machineId, ReplaceMachineOperatorsRequest request,
            ClaimsPrincipal user, MachineWorkspaceManagement service, CancellationToken token) =>
            ExecuteAsync(() => service.ReplaceOperatorsAsync(machineId, request, Actor(user), token)));
        var machine = group.MapGroup("/{machineId:guid}");
        machine.MapGet("/", (Guid machineId, ClaimsPrincipal user, ProductionManagementService service,
            MachineWorkspaceAccess access, IMachineWorkspaceRepository workspace, CancellationToken token) =>
            ExecuteAsync(() => service.GetMachineWorkspaceAsync(machineId, access, workspace, Actor(user), token)));
        machine.MapPost("/capture", (Guid machineId, MachineCaptureRequest request, ClaimsPrincipal user,
            ProductionManagementService service, MachineWorkspaceAccess access, CancellationToken token) =>
            ExecuteAsync(() => service.CaptureForMachineAsync(machineId, request, access, Actor(user), token)));
        machine.MapPost("/lots", (Guid machineId, MachineLotRequest request, ClaimsPrincipal user,
            ProductionManagementService service, MachineWorkspaceAccess access, CancellationToken token) =>
            ExecuteAsync(() => service.RegisterMachineLotAsync(machineId, request, access, Actor(user), token)));
        machine.MapPost("/allocate", (Guid machineId, AllocateProductionRequest request, ClaimsPrincipal user,
            ProductionManagementService service, MachineWorkspaceAccess access, CancellationToken token) =>
            ExecuteAsync(() => service.AllocateMachineLotAsync(machineId, request, access, Actor(user), token)));
        machine.MapPost("/handoff", (Guid machineId, MachineHandoffRequest request, ClaimsPrincipal user,
            ProductionManagementService service, MachineWorkspaceAccess access, CancellationToken token) =>
            ExecuteAsync(() => service.HandoffMachineLotAsync(machineId, request, access, Actor(user), token)));
        machine.MapPost("/release", (Guid machineId, MachineReleaseRequest request, ClaimsPrincipal user,
            ProductionManagementService service, MachineWorkspaceAccess access, IMachineWorkspaceRepository workspace,
            CancellationToken token) =>
            ExecuteAsync(() => service.ReleaseMachineProductionAsync(machineId, request, access, workspace, Actor(user), token)));
        return endpoints;
    }

    private static AdminActorContext Actor(ClaimsPrincipal user) => new(user.FindFirstValue(ProdimtAuthClaims.UserId),
        user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (CustomerOrderConflictException ex) { return Results.Conflict(new { error = ex.Message, retrySameOperation = ex.RetrySameOperation }); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message, retrySameOperation = true }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (NotSupportedException) { return Results.Problem("Esta operación requiere SQL Server y persistencia transaccional.", statusCode: 503); }
    }
}
