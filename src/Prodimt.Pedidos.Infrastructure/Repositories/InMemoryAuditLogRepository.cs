using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryAuditLogRepository(InMemoryDataStore store) : IAuditLogRepository
{
    public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            store.AuditLogs.Add(auditLog);
        }

        return Task.CompletedTask;
    }
}
