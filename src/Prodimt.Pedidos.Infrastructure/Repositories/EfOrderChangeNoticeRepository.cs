using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfOrderChangeNoticeRepository(PedidosDbContext db) : IOrderChangeNoticeRepository
{
    public async Task AddAsync(OrderChangeNotice notice, CancellationToken cancellationToken)
        => await db.OrderChangeNotices.AddAsync(notice, cancellationToken);
    public async Task<IReadOnlyList<OrderChangeNotice>> GetByDateAsync(DateOnly serviceDate, CancellationToken cancellationToken)
        => await db.OrderChangeNotices.AsNoTracking().Where(x => x.ServiceDate == serviceDate).ToArrayAsync(cancellationToken);
    public Task<OrderChangeNotice?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => db.OrderChangeNotices.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
}
