using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfMachineRepository(PedidosDbContext dbContext) : IMachineRepository
{
    public async Task<IReadOnlyList<Machine>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Machines
            .AsNoTracking()
            .OrderBy(x => x.Number)
            .ToArrayAsync(cancellationToken);
    }

    public Task<Machine?> GetByIdAsync(Guid machineId, CancellationToken cancellationToken)
    {
        return dbContext.Machines
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == machineId, cancellationToken);
    }

    public Task<Machine?> GetByIdForUpdateAsync(Guid machineId, CancellationToken cancellationToken)
    {
        return dbContext.Machines
            .SingleOrDefaultAsync(x => x.Id == machineId, cancellationToken);
    }

    public Task<Machine?> GetByNumberAsync(int number, CancellationToken cancellationToken)
    {
        return dbContext.Machines
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Number == number, cancellationToken);
    }

    public async Task<IReadOnlyList<Machine>> GetByIdsAsync(IEnumerable<Guid> machineIds, CancellationToken cancellationToken)
    {
        var ids = machineIds.Distinct().ToArray();

        return await dbContext.Machines
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async Task AddAsync(Machine machine, CancellationToken cancellationToken)
    {
        await dbContext.Machines.AddAsync(machine, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
