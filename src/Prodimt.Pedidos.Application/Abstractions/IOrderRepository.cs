using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IOrderRepository
{
    Task<bool> HasActiveCustomerOrderAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken);

    Task<int> CountCustomerOrdersAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken);

    Task<Order?> GetLatestCustomerOrderAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> GetCustomerIdsWithOrdersAsync(DateOnly orderDate, CancellationToken cancellationToken);

    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetByDateAsync(DateOnly orderDate, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetPendingReviewAsync(DateOnly orderDate, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
