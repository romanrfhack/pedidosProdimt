using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfMachineTeamRepository(PedidosDbContext db) : IMachineTeamRepository
{
    public async Task<IReadOnlyList<MachineWorker>> GetWorkersAsync(CancellationToken token) =>
        await db.Set<MachineWorker>().AsNoTracking().ToArrayAsync(token);
    public async Task<IReadOnlyList<MachineTeamTemplate>> GetTemplatesAsync(CancellationToken token) =>
        await db.Set<MachineTeamTemplate>().ToArrayAsync(token);
    public async Task<IReadOnlyList<MachineTeamChange>> GetChangesAsync(DateOnly date, CancellationToken token) =>
        await db.Set<MachineTeamChange>().AsNoTracking().Where(x => x.Date == date).ToArrayAsync(token);
    public async Task AddWorkerAsync(MachineWorker worker, CancellationToken token) =>
        await db.Set<MachineWorker>().AddAsync(worker, token);
    public async Task AddTemplateAsync(MachineTeamTemplate template, CancellationToken token) =>
        await db.Set<MachineTeamTemplate>().AddAsync(template, token);
    public async Task AddChangeAsync(MachineTeamChange change, CancellationToken token) =>
        await db.Set<MachineTeamChange>().AddAsync(change, token);
}

public sealed class InMemoryMachineTeamRepository : IMachineTeamRepository
{
    public Task<IReadOnlyList<MachineWorker>> GetWorkersAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<MachineWorker>>([]);
    public Task<IReadOnlyList<MachineTeamTemplate>> GetTemplatesAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<MachineTeamTemplate>>([]);
    public Task<IReadOnlyList<MachineTeamChange>> GetChangesAsync(DateOnly date, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<MachineTeamChange>>([]);
    public Task AddWorkerAsync(MachineWorker worker, CancellationToken token) => throw PersistenceRequired();
    public Task AddTemplateAsync(MachineTeamTemplate template, CancellationToken token) => throw PersistenceRequired();
    public Task AddChangeAsync(MachineTeamChange change, CancellationToken token) => throw PersistenceRequired();

    private static NotSupportedException PersistenceRequired() => new("El equipo de máquina requiere SQL Server.");
}
