using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.CustomerOrders;
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

    public Task<IReadOnlySet<Guid>> GetCustomerIdsWithOrdersAsync(DateOnly orderDate, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            IReadOnlySet<Guid> customerIds = store.Orders
                .Where(x => x.CustomerId is not null && x.OrderDate == orderDate)
                .Select(x => x.CustomerId!.Value)
                .ToHashSet();

            return Task.FromResult(customerIds);
        }
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            if (order.CustomerId is not null && store.Orders.Any(x => x.CustomerId == order.CustomerId && x.OrderDate == order.OrderDate && x.SequenceNumber == order.SequenceNumber))
                throw new CustomerOrderConflictException("Se registro otro pedido al mismo tiempo. Recarga antes de volver a intentar.");
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

    public Task<Order?> GetBySourceAsync(string source, string externalOrderKey, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
            return Task.FromResult(store.Orders.SingleOrDefault(x => x.Source == source && x.ExternalOrderKey == externalOrderKey));
    }

    public Task ReplaceLinesAsync(Order order, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot) order.ReplaceLines(lines);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
