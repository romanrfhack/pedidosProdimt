using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemorySalesChannelRepository(InMemoryDataStore store) : ISalesChannelRepository
{
    public Task<SalesChannel> GetRequiredByTypeAsync(SalesChannelType type, CancellationToken cancellationToken)
    {
        var channel = store.SalesChannels.SingleOrDefault(x => x.Type == type);

        if (channel is null)
        {
            throw new InvalidOperationException($"Sales channel '{type}' is not configured.");
        }

        return Task.FromResult(channel);
    }

    public Task<SalesChannel?> GetByIdAsync(Guid salesChannelId, CancellationToken cancellationToken)
    {
        var channel = store.SalesChannels.SingleOrDefault(x => x.Id == salesChannelId);
        return Task.FromResult(channel);
    }
}
