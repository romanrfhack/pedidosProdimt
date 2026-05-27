using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerFrequentProduct>> GetFrequentProductsAsync(Guid customerId, CancellationToken cancellationToken);
}
