using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryCustomerRepository(InMemoryDataStore store) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = store.Customers.SingleOrDefault(x => x.Id == customerId);
        return Task.FromResult(customer);
    }

    public Task<IReadOnlyList<CustomerFrequentProduct>> GetFrequentProductsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomerFrequentProduct> frequentProducts = store.CustomerFrequentProducts
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.SortOrder)
            .ToArray();

        return Task.FromResult(frequentProducts);
    }
}
