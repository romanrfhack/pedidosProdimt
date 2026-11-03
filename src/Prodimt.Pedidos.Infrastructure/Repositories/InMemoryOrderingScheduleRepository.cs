using System.Collections.Concurrent;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryOrderingScheduleRepository : IOrderingScheduleRepository
{
    private readonly ConcurrentDictionary<string, RouteOrderSchedule> items = new(StringComparer.OrdinalIgnoreCase);
    public Task<IReadOnlyList<RouteOrderSchedule>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<RouteOrderSchedule>>(items.Values.ToArray());
    public Task<RouteOrderSchedule?> GetAsync(string routeCode, CancellationToken token) => Task.FromResult(items.GetValueOrDefault(routeCode));
    public Task AddAsync(RouteOrderSchedule schedule, CancellationToken token) { items[schedule.RouteCode] = schedule; return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken token) => Task.CompletedTask;
}
