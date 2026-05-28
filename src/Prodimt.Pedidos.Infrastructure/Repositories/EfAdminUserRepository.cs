using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfAdminUserRepository(PedidosDbContext dbContext) : IAdminUserRepository
{
    public async Task<IReadOnlyList<AdminUser>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.AdminUsers
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .ToArrayAsync(cancellationToken);
    }

    public Task<AdminUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.AdminUsers
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }

    public Task<AdminUser?> GetByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var normalizedUserName = userName.Trim();

        return dbContext.AdminUsers.SingleOrDefaultAsync(x => x.UserName == normalizedUserName, cancellationToken);
    }

    public async Task AddAsync(AdminUser adminUser, CancellationToken cancellationToken)
    {
        await dbContext.AdminUsers.AddAsync(adminUser, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
