using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryOrderChangeNoticeRepository : IOrderChangeNoticeRepository
{
    private readonly List<OrderChangeNotice> notices = [];
    private readonly object sync = new();
    public Task AddAsync(OrderChangeNotice notice, CancellationToken cancellationToken)
    {
        lock (sync) notices.Add(notice);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<OrderChangeNotice>> GetByDateAsync(DateOnly serviceDate, CancellationToken cancellationToken)
    {
        lock (sync) return Task.FromResult<IReadOnlyList<OrderChangeNotice>>(notices.Where(x => x.ServiceDate == serviceDate).ToArray());
    }
    public Task<OrderChangeNotice?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (sync) return Task.FromResult(notices.SingleOrDefault(x => x.Id == id));
    }
}
