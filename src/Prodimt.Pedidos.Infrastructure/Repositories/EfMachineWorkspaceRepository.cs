using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfMachineWorkspaceRepository(PedidosDbContext db) : IMachineWorkspaceRepository
{
    public async Task<IReadOnlyList<Guid>> GetMachineIdsAsync(Guid userId, CancellationToken token)
        => await db.Set<MachineOperator>().Where(x => x.UserId == userId).Select(x => x.MachineId).ToArrayAsync(token);

    public async Task<IReadOnlyList<Guid>> GetOperatorIdsAsync(Guid machineId, CancellationToken token)
        => await db.Set<MachineOperator>().Where(x => x.MachineId == machineId).Select(x => x.UserId).ToArrayAsync(token);

    public async Task ReplaceOperatorsAsync(Guid machineId, IReadOnlyList<Guid> userIds, CancellationToken token)
    {
        var assignments = db.Set<MachineOperator>();
        var current = await assignments.Where(x => x.MachineId == machineId).ToArrayAsync(token);
        var desired = userIds.ToHashSet();
        assignments.RemoveRange(current.Where(x => !desired.Contains(x.UserId)));
        var existing = current.Select(x => x.UserId).ToHashSet();
        assignments.AddRange(desired.Except(existing).Select(userId => new MachineOperator { MachineId = machineId, UserId = userId }));
    }

    public async Task<IReadOnlyList<MachineProductionNotice>> GetNoticesAsync(Guid machineId, DateOnly date, CancellationToken token)
    {
        // SQLite cannot sort DateTimeOffset in SQL.
        var day = await db.Set<MachineProductionNotice>().AsNoTracking()
            .Where(x => x.MachineId == machineId && x.ServiceDate == date).ToArrayAsync(token);
        return day.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(30).ToArray();
    }

    public async Task<IReadOnlyList<MachineProductionNotice>> GetDayNoticesAsync(DateOnly date, CancellationToken token)
    {
        var day = await db.Set<MachineProductionNotice>().AsNoTracking()
            .Where(x => x.ServiceDate == date).ToArrayAsync(token);
        return day.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToArray();
    }

    public async Task AddNoticeAsync(MachineProductionNotice notice, CancellationToken token)
        => await db.Set<MachineProductionNotice>().AddAsync(notice, token);
}
