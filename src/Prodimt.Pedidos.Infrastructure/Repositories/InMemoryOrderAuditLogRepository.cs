using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryOrderAuditLogRepository(InMemoryDataStore store) : IOrderAuditLogRepository
{
    public Task AddAsync(OrderAuditLog auditLog, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            store.OrderAuditLogs.Add(auditLog);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OrderAuditLog>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            IReadOnlyList<OrderAuditLog> auditLogs = store.OrderAuditLogs
                .Where(x => x.OrderId == orderId)
                .OrderBy(x => x.OccurredAt)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .ToArray();

            return Task.FromResult(auditLogs);
        }
    }
}
