using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfOrderAuditLogRepository(PedidosDbContext dbContext) : IOrderAuditLogRepository
{
    public async Task AddAsync(OrderAuditLog auditLog, CancellationToken cancellationToken)
    {
        await dbContext.OrderAuditLogs.AddAsync(auditLog, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderAuditLog>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var auditLogs = await dbContext.OrderAuditLogs
            .AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .ToArrayAsync(cancellationToken);

        return auditLogs
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArray();
    }
}
