using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfSalesChannelRepository(PedidosDbContext dbContext) : ISalesChannelRepository
{
    public async Task<SalesChannel> GetRequiredByTypeAsync(SalesChannelType type, CancellationToken cancellationToken)
    {
        var channel = await dbContext.SalesChannels
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Type == type, cancellationToken);

        if (channel is null)
        {
            throw new InvalidOperationException($"Sales channel '{type}' is not configured.");
        }

        return channel;
    }
}
