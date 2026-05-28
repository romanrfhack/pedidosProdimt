using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryMachineRepository(InMemoryDataStore store) : IMachineRepository
{
    public Task<IReadOnlyList<Machine>> GetByIdsAsync(IEnumerable<Guid> machineIds, CancellationToken cancellationToken)
    {
        var ids = machineIds.ToHashSet();
        IReadOnlyList<Machine> machines = store.Machines
            .Where(x => ids.Contains(x.Id))
            .ToArray();

        return Task.FromResult(machines);
    }
}
