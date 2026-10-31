using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Prodimt.Pedidos.Application.CustomerOrders;
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

    public async Task<Order?> GetLatestCustomerOrderAsync(Guid customerId, DateOnly orderDate, CancellationToken cancellationToken)
    {
        var customerOrders = await dbContext.Orders
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.OrderDate == orderDate)
            .ToArrayAsync(cancellationToken);

        return customerOrders
            .OrderByDescending(x => x.SubmittedAt)
            .ThenByDescending(x => x.SequenceNumber)
            .FirstOrDefault();
    }

    public async Task<IReadOnlySet<Guid>> GetCustomerIdsWithOrdersAsync(DateOnly orderDate, CancellationToken cancellationToken)
    {
        var customerIds = await dbContext.Orders
            .AsNoTracking()
            .Where(x => x.CustomerId != null && x.OrderDate == orderDate)
            .Select(x => x.CustomerId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return customerIds.ToHashSet();
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        await dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByDateAsync(DateOnly orderDate, CancellationToken cancellationToken)
    {
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrderDate == orderDate)
            .ToArrayAsync(cancellationToken);

        return orders
            .OrderBy(x => x.SubmittedAt)
            .ToArray();
    }

    public async Task<IReadOnlyList<Order>> GetPendingReviewAsync(DateOnly orderDate, CancellationToken cancellationToken)
    {
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Where(x => x.OrderDate == orderDate && x.RequiresAdminReview)
            .ToArrayAsync(cancellationToken);

        return orders
            .OrderBy(x => x.SubmittedAt)
            .ToArray();
    }

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return dbContext.Orders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
    }

    public Task<Order?> GetBySourceAsync(string source, string externalOrderKey, CancellationToken cancellationToken)
        => dbContext.Orders.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Source == source && x.ExternalOrderKey == externalOrderKey, cancellationToken);

    public Task ReplaceLinesAsync(Order order, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken)
    {
        dbContext.OrderLines.RemoveRange(order.Lines);
        order.ReplaceLines(lines);
        dbContext.OrderLines.AddRange(lines);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (CatalogPersistenceConflicts.IsConcurrentChange(ex))
        {
            throw new CustomerOrderConflictException("El pedido cambio durante la operacion. Recarga el detalle.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new CustomerOrderConflictException("Se registro otro pedido al mismo tiempo. Recarga antes de volver a intentar.");
        }
    }
}
