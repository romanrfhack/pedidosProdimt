using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IMachineRepository
{
    Task<IReadOnlyList<Machine>> GetAllAsync(CancellationToken cancellationToken);

    Task<Machine?> GetByIdAsync(Guid machineId, CancellationToken cancellationToken);

    Task<Machine?> GetByIdForUpdateAsync(Guid machineId, CancellationToken cancellationToken);

    Task<Machine?> GetByNumberAsync(int number, CancellationToken cancellationToken);

    Task<IReadOnlyList<Machine>> GetByIdsAsync(IEnumerable<Guid> machineIds, CancellationToken cancellationToken);

    Task AddAsync(Machine machine, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
