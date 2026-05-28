using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed class AdminMachineCatalogService(
    IMachineRepository machines,
    IAuditLogRepository auditLogs,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<IReadOnlyList<AdminMachineResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var allMachines = await machines.GetAllAsync(cancellationToken);
        return allMachines.Select(MapMachine).ToArray();
    }

    public async Task<AdminMachineResponse> GetByIdAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machine = await GetRequiredMachineAsync(machineId, cancellationToken);
        return MapMachine(machine);
    }

    public async Task<AdminMachineResponse> CreateAsync(
        UpsertAdminMachineRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureNumberIsAvailableAsync(request.Number, excludingMachineId: null, cancellationToken);

        var machine = Machine.Create(request.Number, request.Name);

        await machines.AddAsync(machine, cancellationToken);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Machine,
            machine.Id,
            CatalogAuditEventTypes.MachineCreated,
            dateTimeProvider.Now,
            actor,
            $"Maquina creada: #{machine.Number}."), cancellationToken);
        await machines.SaveChangesAsync(cancellationToken);

        return MapMachine(machine);
    }

    public async Task<AdminMachineResponse> UpdateAsync(
        Guid machineId,
        UpsertAdminMachineRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var machine = await GetRequiredMachineForUpdateAsync(machineId, cancellationToken);
        await EnsureNumberIsAvailableAsync(request.Number, machine.Id, cancellationToken);

        machine.Update(request.Number, request.Name);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Machine,
            machine.Id,
            CatalogAuditEventTypes.MachineUpdated,
            dateTimeProvider.Now,
            actor,
            $"Maquina actualizada: #{machine.Number}."), cancellationToken);
        await machines.SaveChangesAsync(cancellationToken);

        return MapMachine(machine);
    }

    public async Task<AdminMachineResponse> ActivateAsync(
        Guid machineId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var machine = await GetRequiredMachineForUpdateAsync(machineId, cancellationToken);
        machine.Activate();
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Machine,
            machine.Id,
            CatalogAuditEventTypes.MachineActivated,
            dateTimeProvider.Now,
            actor,
            $"Maquina activada: #{machine.Number}."), cancellationToken);
        await machines.SaveChangesAsync(cancellationToken);

        return MapMachine(machine);
    }

    public async Task<AdminMachineResponse> DeactivateAsync(
        Guid machineId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var machine = await GetRequiredMachineForUpdateAsync(machineId, cancellationToken);
        machine.Deactivate();
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Machine,
            machine.Id,
            CatalogAuditEventTypes.MachineDeactivated,
            dateTimeProvider.Now,
            actor,
            $"Maquina desactivada: #{machine.Number}."), cancellationToken);
        await machines.SaveChangesAsync(cancellationToken);

        return MapMachine(machine);
    }

    private async Task<Machine> GetRequiredMachineAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machine = await machines.GetByIdAsync(machineId, cancellationToken);

        if (machine is null)
        {
            throw new InvalidOperationException("Machine was not found.");
        }

        return machine;
    }

    private async Task<Machine> GetRequiredMachineForUpdateAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machine = await machines.GetByIdForUpdateAsync(machineId, cancellationToken);

        if (machine is null)
        {
            throw new InvalidOperationException("Machine was not found.");
        }

        return machine;
    }

    private async Task EnsureNumberIsAvailableAsync(
        int number,
        Guid? excludingMachineId,
        CancellationToken cancellationToken)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "El numero de maquina debe ser mayor a cero.");
        }

        var duplicate = await machines.GetByNumberAsync(number, cancellationToken);

        if (duplicate is not null && duplicate.Id != excludingMachineId)
        {
            throw new ArgumentException("Ya existe una maquina con ese numero.", nameof(number));
        }
    }

    private static AdminMachineResponse MapMachine(Machine machine)
    {
        return new AdminMachineResponse(machine.Id, machine.Number, machine.Name, machine.IsActive);
    }
}
