using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryMachineRepository(InMemoryDataStore store) : IMachineRepository
{
    public Task<IReadOnlyList<Machine>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Machine> machines = store.Machines
            .OrderBy(x => x.Number)
            .ToArray();

        return Task.FromResult(machines);
    }

    public Task<Machine?> GetByIdAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machine = store.Machines.SingleOrDefault(x => x.Id == machineId);
        return Task.FromResult(machine);
    }

    public Task<Machine?> GetByIdForUpdateAsync(Guid machineId, CancellationToken cancellationToken)
    {
        return GetByIdAsync(machineId, cancellationToken);
    }

    public Task<Machine?> GetByNumberAsync(int number, CancellationToken cancellationToken)
    {
        var machine = store.Machines.SingleOrDefault(x => x.Number == number);
        return Task.FromResult(machine);
    }

    public Task<IReadOnlyList<Machine>> GetByIdsAsync(IEnumerable<Guid> machineIds, CancellationToken cancellationToken)
    {
        var ids = machineIds.ToHashSet();
        IReadOnlyList<Machine> machines = store.Machines
            .Where(x => ids.Contains(x.Id))
            .ToArray();

        return Task.FromResult(machines);
    }

    public Task AddAsync(Machine machine, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            store.Machines.Add(machine);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
