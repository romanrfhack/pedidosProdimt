using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken);

    Task<Customer?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken);

    Task<Customer?> GetByIdForUpdateAsync(Guid customerId, CancellationToken cancellationToken);

    Task<Customer?> GetActiveByExactNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Customer>> GetActiveAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Customer>> GetByIdsAsync(IEnumerable<Guid> customerIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerFrequentProduct>> GetFrequentProductsAsync(Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerFrequentProduct>> GetAllFrequentProductsAsync(Guid customerId, CancellationToken cancellationToken);

    Task ReplaceFrequentProductsAsync(
        Guid customerId,
        IReadOnlyList<CustomerFrequentProduct> frequentProducts,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerMachineAssignment>> GetMachineAssignmentsAsync(Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerMachineAssignment>> GetAllMachineAssignmentsAsync(Guid customerId, CancellationToken cancellationToken);

    Task ReplaceMachineAssignmentsAsync(
        Guid customerId,
        IReadOnlyList<CustomerMachineAssignment> assignments,
        CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
