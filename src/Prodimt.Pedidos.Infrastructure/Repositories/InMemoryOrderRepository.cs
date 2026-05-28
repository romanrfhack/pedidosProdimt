using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryOrderRepository(InMemoryDataStore store) : IOrderRepository
{
    private static readonly OrderStatus[] ActiveCustomerOrderStatuses =
    [
        OrderStatus.Submitted,
        OrderStatus.PendingAdminReview,
        OrderStatus.Accepted
    ];

    public Task<bool> HasActiveCustomerOrderAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            return Task.FromResult(store.Orders.Any(x =>
                x.CustomerId == customerId &&
                x.OrderDate == orderDate &&
                ActiveCustomerOrderStatuses.Contains(x.Status)));
        }
    }

    public Task<int> CountCustomerOrdersAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            return Task.FromResult(store.Orders.Count(x => x.CustomerId == customerId && x.OrderDate == orderDate));
        }
    }

    public Task<Order?> GetLatestCustomerOrderAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            var order = store.Orders
                .Where(x => x.CustomerId == customerId && x.OrderDate == orderDate)
                .OrderByDescending(x => x.SubmittedAt)
                .ThenByDescending(x => x.SequenceNumber)
                .FirstOrDefault();

            return Task.FromResult(order);
        }
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            store.Orders.Add(order);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Order>> GetByDateAsync(DateOnly orderDate, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            IReadOnlyList<Order> orders = store.Orders
                .Where(x => x.OrderDate == orderDate)
                .OrderBy(x => x.SubmittedAt)
                .ToArray();

            return Task.FromResult(orders);
        }
    }

    public Task<IReadOnlyList<Order>> GetPendingReviewAsync(DateOnly orderDate, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            IReadOnlyList<Order> orders = store.Orders
                .Where(x => x.OrderDate == orderDate && x.RequiresAdminReview)
                .OrderBy(x => x.SubmittedAt)
                .ToArray();

            return Task.FromResult(orders);
        }
    }

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            return Task.FromResult(store.Orders.SingleOrDefault(x => x.Id == orderId));
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
