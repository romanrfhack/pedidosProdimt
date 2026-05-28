using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfMachineRepository(PedidosDbContext dbContext) : IMachineRepository
{
    public async Task<IReadOnlyList<Machine>> GetByIdsAsync(IEnumerable<Guid> machineIds, CancellationToken cancellationToken)
    {
        var ids = machineIds.Distinct().ToArray();

        return await dbContext.Machines
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
    }
}
