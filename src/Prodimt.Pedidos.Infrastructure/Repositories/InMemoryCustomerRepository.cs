using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryCustomerRepository(InMemoryDataStore store) : ICustomerRepository
{
    public Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Customer> customers = store.Customers
            .OrderBy(x => x.Name)
            .ToArray();

        return Task.FromResult(customers);
    }

    public Task<Customer?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = store.Customers.SingleOrDefault(x => x.Id == customerId);
        return Task.FromResult(customer);
    }

    public Task<Customer?> GetByIdForUpdateAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return GetByIdAsync(customerId, cancellationToken);
    }

    public Task<Customer?> GetActiveByExactNameAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        var customer = store.Customers.SingleOrDefault(x => x.IsActive && x.Name == normalizedName);
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
            .Where(x => x.CustomerId == customerId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToArray();

        return Task.FromResult(frequentProducts);
    }

    public Task<IReadOnlyList<CustomerFrequentProduct>> GetAllFrequentProductsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomerFrequentProduct> frequentProducts = store.CustomerFrequentProducts
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.SortOrder)
            .ToArray();

        return Task.FromResult(frequentProducts);
    }

    public Task ReplaceFrequentProductsAsync(
        Guid customerId,
        IReadOnlyList<CustomerFrequentProduct> frequentProducts,
        CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            store.CustomerFrequentProducts.RemoveAll(x => x.CustomerId == customerId);
            store.CustomerFrequentProducts.AddRange(frequentProducts);
        }

        return Task.CompletedTask;
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

    public Task<IReadOnlyList<CustomerMachineAssignment>> GetAllMachineAssignmentsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomerMachineAssignment> assignments = store.CustomerMachineAssignments
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.MachineId)
            .ToArray();

        return Task.FromResult(assignments);
    }

    public Task ReplaceMachineAssignmentsAsync(
        Guid customerId,
        IReadOnlyList<CustomerMachineAssignment> assignments,
        CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            store.CustomerMachineAssignments.RemoveAll(x => x.CustomerId == customerId);
            store.CustomerMachineAssignments.AddRange(assignments);
        }

        return Task.CompletedTask;
    }

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        lock (store.SyncRoot)
        {
            store.Customers.Add(customer);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
