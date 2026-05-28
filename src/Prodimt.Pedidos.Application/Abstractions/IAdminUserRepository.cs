using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IAdminUserRepository
{
    Task<AdminUser?> GetByUserNameAsync(string userName, CancellationToken cancellationToken);
}
