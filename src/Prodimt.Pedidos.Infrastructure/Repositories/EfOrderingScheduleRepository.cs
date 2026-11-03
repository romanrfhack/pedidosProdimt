using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;
namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfOrderingScheduleRepository(PedidosDbContext db) : IOrderingScheduleRepository
{
    public async Task<IReadOnlyList<RouteOrderSchedule>> GetAllAsync(CancellationToken token) =>
        await db.RouteOrderSchedules.AsNoTracking().ToArrayAsync(token);
    public Task<RouteOrderSchedule?> GetAsync(string routeCode, CancellationToken token) =>
        db.RouteOrderSchedules.SingleOrDefaultAsync(x => x.RouteCode == routeCode, token);
    public async Task AddAsync(RouteOrderSchedule schedule, CancellationToken token) => await db.RouteOrderSchedules.AddAsync(schedule, token);
    public async Task SaveChangesAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException) { throw new CatalogConflictException("El horario cambió. Actualiza la página antes de volver a guardar."); }
    }
}
