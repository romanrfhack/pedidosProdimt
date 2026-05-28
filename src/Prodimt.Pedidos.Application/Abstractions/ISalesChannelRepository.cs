using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface ISalesChannelRepository
{
    Task<SalesChannel> GetRequiredByTypeAsync(SalesChannelType type, CancellationToken cancellationToken);

    Task<SalesChannel?> GetByIdAsync(Guid salesChannelId, CancellationToken cancellationToken);
}
