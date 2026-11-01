using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface ICustomerAccessTokenRepository
{
    Task<T> ExecuteMutationAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);

    Task<CustomerAccessToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<CustomerAccessToken?> GetByIdAsync(Guid tokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerAccessToken>> GetByCustomerIdAsync(Guid customerId, CancellationToken cancellationToken);

    Task AddAsync(CustomerAccessToken accessToken, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
