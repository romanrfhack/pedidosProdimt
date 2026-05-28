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

    public Task<IReadOnlyList<Customer>> GetActiveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Customer> customers = store.Customers
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToArray();

        return Task.FromResult(customers);
    }

    public Task<IReadOnlyList<Customer>> GetByIdsAsync(
        IEnumerable<Guid> customerIds,
        CancellationToken cancellationToken)
    {
        var ids = customerIds.ToHashSet();
        IReadOnlyList<Customer> customers = store.Customers
            .Where(x => ids.Contains(x.Id))
            .ToArray();

        return Task.FromResult(customers);
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

    public Task<IReadOnlyList<CustomerMachineAssignment>> GetMachineAssignmentsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomerMachineAssignment> assignments = store.CustomerMachineAssignments
            .Where(x => x.CustomerId == customerId && x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ToArray();

        return Task.FromResult(assignments);
    }
}
