using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Security;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed class MachineWorkspaceAccess(ISecurityStore security, IMachineRepository machines, IMachineWorkspaceRepository workspace)
{
    public const string WorkPermission = "operations.machine.work";
    public const string ManagePermission = "operations.machine.manage";

    public async Task<IReadOnlyList<Guid>> GetMachineIdsAsync(AdminActorContext actor, CancellationToken token)
    {
        var userId = RequireActor(actor);
        var permissions = await security.GetPermissionsAsync(userId, token);
        if (permissions.Contains(ManagePermission))
            return (await machines.GetAllAsync(token)).Where(machine => machine.IsActive).Select(machine => machine.Id).ToArray();
        if (!permissions.Contains(WorkPermission)) throw new UnauthorizedAccessException();
        return await workspace.GetMachineIdsAsync(userId, token);
    }

    public async Task RequireAsync(Guid machineId, AdminActorContext actor, CancellationToken token)
    {
        if (!(await GetMachineIdsAsync(actor, token)).Contains(machineId)
            || await machines.GetByIdAsync(machineId, token) is not { IsActive: true })
            throw new UnauthorizedAccessException();
    }

    public async Task RequireManageAsync(AdminActorContext actor, CancellationToken token)
    {
        if (!(await security.GetPermissionsAsync(RequireActor(actor), token)).Contains(ManagePermission))
            throw new UnauthorizedAccessException();
    }

    private static Guid RequireActor(AdminActorContext actor) => Guid.TryParse(actor.ActorId, out var userId)
        ? userId : throw new UnauthorizedAccessException();
}
