using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfAdminUserRepository(PedidosDbContext dbContext) : IAdminUserRepository
{
    public Task<AdminUser?> GetByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var normalizedUserName = userName.Trim();

        return dbContext.AdminUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserName == normalizedUserName, cancellationToken);
    }
}
