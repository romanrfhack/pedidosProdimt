using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfCustomerProductSetupRepository(PedidosDbContext db) : ICustomerProductSetupRepository
{
    public Task<int> CountOpenAsync(CancellationToken token)
        => db.CustomerProductSetupRequests.CountAsync(x => !x.IsResolved, token);
    public async Task<IReadOnlyList<CustomerProductSetupRequest>> GetOpenAsync(CancellationToken token)
        => (await db.CustomerProductSetupRequests.Include(x => x.Orders).Where(x => !x.IsResolved).ToArrayAsync(token))
            .OrderByDescending(x => x.LastRequestedAt).ToArray();
    public Task<CustomerProductSetupRequest?> GetAsync(Guid id, CancellationToken token)
        => db.CustomerProductSetupRequests.Include(x => x.Orders).SingleOrDefaultAsync(x => x.Id == id, token);
    public Task<CustomerProductSetupRequest?> GetByPairAsync(Guid customerId, Guid productId, CancellationToken token)
        => db.CustomerProductSetupRequests.Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.CustomerId == customerId && x.ProductId == productId, token);
    public async Task<IReadOnlyList<Order>> GetOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken token)
        => await db.Orders.Include(x => x.Lines).Where(x => orderIds.Contains(x.Id)).ToArrayAsync(token);
    public async Task AddAsync(CustomerProductSetupRequest request, CancellationToken token)
        => await db.CustomerProductSetupRequests.AddAsync(request, token);
    public Task SaveChangesAsync(CancellationToken token) => db.SaveChangesAsync(token);
}
