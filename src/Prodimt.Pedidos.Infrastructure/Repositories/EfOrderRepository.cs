using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfOrderRepository(PedidosDbContext dbContext) : IOrderRepository
{
    private static readonly OrderStatus[] ActiveCustomerOrderStatuses =
    [
        OrderStatus.Submitted,
        OrderStatus.PendingAdminReview,
        OrderStatus.Accepted
    ];

    public Task<bool> HasActiveCustomerOrderAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken)
    {
        return dbContext.Orders.AnyAsync(
            x => x.CustomerId == customerId &&
                x.OrderDate == orderDate &&
                ActiveCustomerOrderStatuses.Contains(x.Status),
            cancellationToken);
    }

    public Task<int> CountCustomerOrdersAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken)
    {
        return dbContext.Orders.CountAsync(
            x => x.CustomerId == customerId && x.OrderDate == orderDate,
            cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        await dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByDateAsync(DateOnly orderDate, CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .AsNoTracking()
            .Where(x => x.OrderDate == orderDate)
            .OrderBy(x => x.SubmittedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetPendingReviewAsync(DateOnly orderDate, CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .AsNoTracking()
            .Where(x => x.OrderDate == orderDate && x.RequiresAdminReview)
            .OrderBy(x => x.SubmittedAt)
            .ToArrayAsync(cancellationToken);
    }

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return dbContext.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
