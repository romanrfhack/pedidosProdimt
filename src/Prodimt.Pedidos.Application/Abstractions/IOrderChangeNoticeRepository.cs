using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IOrderChangeNoticeRepository
{
    Task AddAsync(OrderChangeNotice notice, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderChangeNotice>> GetByDateAsync(DateOnly serviceDate, CancellationToken cancellationToken);
    Task<OrderChangeNotice?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
