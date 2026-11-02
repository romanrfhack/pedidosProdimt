using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

// Read-only demo follows the existing nontransactional capture limitation.
public sealed class InMemoryCustomerProductSetupRepository(InMemoryDataStore store) : ICustomerProductSetupRepository
{
    public Task<int> CountOpenAsync(CancellationToken token) => Task.FromResult(0);
    public Task<IReadOnlyList<CustomerProductSetupRequest>> GetOpenAsync(CancellationToken token)
        => Task.FromResult<IReadOnlyList<CustomerProductSetupRequest>>([]);
    public Task<CustomerProductSetupRequest?> GetAsync(Guid id, CancellationToken token)
        => Task.FromResult<CustomerProductSetupRequest?>(null);
    public Task<CustomerProductSetupRequest?> GetByPairAsync(Guid customerId, Guid productId, CancellationToken token)
        => Task.FromResult<CustomerProductSetupRequest?>(null);
    public Task<IReadOnlyList<Order>> GetOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken token)
        => Task.FromResult<IReadOnlyList<Order>>(store.Orders.Where(x => orderIds.Contains(x.Id)).ToArray());
    public Task AddAsync(CustomerProductSetupRequest request, CancellationToken token)
        => throw new NotSupportedException("La configuración pendiente requiere persistencia transaccional.");
    public Task SaveChangesAsync(CancellationToken token)
        => throw new NotSupportedException("La configuración pendiente requiere persistencia transaccional.");
}
