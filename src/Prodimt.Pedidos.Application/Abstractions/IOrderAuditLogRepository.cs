using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IOrderAuditLogRepository
{
    Task AddAsync(OrderAuditLog auditLog, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderAuditLog>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
}
