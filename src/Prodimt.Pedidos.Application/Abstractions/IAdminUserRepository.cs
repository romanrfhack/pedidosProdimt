using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IAdminUserRepository
{
    Task<IReadOnlyList<AdminUser>> GetAllAsync(CancellationToken cancellationToken);

    Task<AdminUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<AdminUser?> GetByUserNameAsync(string userName, CancellationToken cancellationToken);

    Task AddAsync(AdminUser adminUser, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
