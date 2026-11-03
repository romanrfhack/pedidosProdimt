using Prodimt.Pedidos.Domain.Entities;
namespace Prodimt.Pedidos.Application.Abstractions;

public interface IOrderingScheduleRepository
{
    Task<IReadOnlyList<RouteOrderSchedule>> GetAllAsync(CancellationToken token);
    Task<RouteOrderSchedule?> GetAsync(string routeCode, CancellationToken token);
    Task AddAsync(RouteOrderSchedule schedule, CancellationToken token);
    Task SaveChangesAsync(CancellationToken token);
}
