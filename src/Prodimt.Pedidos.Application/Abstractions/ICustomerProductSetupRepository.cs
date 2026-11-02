using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface ICustomerProductSetupRepository
{
    Task<int> CountOpenAsync(CancellationToken token);
    Task<IReadOnlyList<CustomerProductSetupRequest>> GetOpenAsync(CancellationToken token);
    Task<CustomerProductSetupRequest?> GetAsync(Guid id, CancellationToken token);
    Task<CustomerProductSetupRequest?> GetByPairAsync(Guid customerId, Guid productId, CancellationToken token);
    Task<IReadOnlyList<Order>> GetOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken token);
    Task AddAsync(CustomerProductSetupRequest request, CancellationToken token);
    Task SaveChangesAsync(CancellationToken token);
}
