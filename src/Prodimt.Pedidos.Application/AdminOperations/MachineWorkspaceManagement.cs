using System.Security.Cryptography;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed class MachineWorkspaceManagement(MachineWorkspaceAccess access, IMachineRepository machines,
    IMachineWorkspaceRepository workspace, ISecurityStore security, IOrderOperationStore operations,
    IAuditLogRepository audit, IDateTimeProvider clock)
{
    public async Task<IReadOnlyList<MachineWorkspaceMachine>> GetMachinesAsync(AdminActorContext actor, CancellationToken token)
    {
        var allowed = await access.GetMachineIdsAsync(actor, token);
        return (await machines.GetByIdsAsync(allowed, token)).Where(machine => machine.IsActive)
            .OrderBy(machine => machine.Number).Select(machine => new MachineWorkspaceMachine(machine.Id, machine.Number,
                machine.Name ?? $"Máquina {machine.Number}")).ToArray();
    }

    public async Task<MachineOperatorsResponse> GetOperatorsAsync(Guid machineId, AdminActorContext actor, CancellationToken token)
    {
        await access.RequireManageAsync(actor, token);
        await access.RequireAsync(machineId, actor, token);
        return await ReadOperatorsAsync(machineId, token);
    }

    public async Task<MachineOperatorsResponse> ReplaceOperatorsAsync(Guid machineId, ReplaceMachineOperatorsRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        await access.RequireManageAsync(actor, token);
        await access.RequireAsync(machineId, actor, token);
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExpectedVersion) || request.UserIds is null)
            throw new ArgumentException("Indica una clave de operación, la versión y los responsables seleccionados.");
        var desired = request.UserIds.Distinct().Order().ToArray();
        var hash = Hash(new { machineId, desired, request.ExpectedVersion, actor.ActorId });
        var receipt = await operations.ExecuteAsync($"MACHINE-OPERATORS:{request.OperationId:N}", hash, clock.Now, async ct =>
        {
            await access.RequireManageAsync(actor, ct);
            await access.RequireAsync(machineId, actor, ct);
            var current = await ReadOperatorsAsync(machineId, ct);
            if (current.Version != request.ExpectedVersion)
                throw new CustomerOrderConflictException("Los responsables cambiaron. Recarga la lista antes de guardar.");
            if (desired.Except(current.Users.Select(user => user.Id)).Any())
                throw new ArgumentException("Selecciona cuentas activas con acceso de Responsable de Máquina.");
            if (!desired.SequenceEqual(current.UserIds))
            {
                await workspace.ReplaceOperatorsAsync(machineId, desired, ct);
                await audit.AddAsync(AuditLog.Create("Machine", machineId.ToString(), "MachineOperatorsUpdated",
                    clock.Now, AuditActorType.Admin, "Responsables de máquina actualizados.", actor.ActorId,
                    actor.ActorDisplayName, JsonSerializer.Serialize(new { before = current.UserIds, after = desired })), ct);
            }
            return current with { UserIds = desired, Version = Hash(desired) };
        }, token);
        return receipt.Result;
    }

    private async Task<MachineOperatorsResponse> ReadOperatorsAsync(Guid machineId, CancellationToken token)
    {
        var data = await security.ReadAsync(token);
        var users = data.Users.Where(user => user.IsActive && !user.IsDeleted &&
                SecurityService.Resolve(data, user.Id).Any(permission => permission.Key == MachineWorkspaceAccess.WorkPermission))
            .OrderBy(user => user.DisplayName).Select(user => new MachineOperatorUser(user.Id, user.DisplayName)).ToArray();
        var ids = (await workspace.GetOperatorIdsAsync(machineId, token)).Order().ToArray();
        return new(users, ids, Hash(ids));
    }

    private static string Hash(object payload)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
}
