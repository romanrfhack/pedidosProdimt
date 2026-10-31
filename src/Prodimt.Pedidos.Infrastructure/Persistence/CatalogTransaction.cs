using System.Data;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminCatalogs;

namespace Prodimt.Pedidos.Infrastructure.Persistence;

internal static class CatalogTransaction
{
    public static async Task<T> ExecuteAsync<T>(PedidosDbContext dbContext,
        Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        // Validation, membership changes and invalidation must share the transaction.
        // Otherwise an approval can race with a product it does not reference yet.
        try
        {
            await using var transaction = dbContext.Database.CurrentTransaction is null
                ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            var result = await action(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex) when (CatalogPersistenceConflicts.IsConcurrentChange(ex))
        {
            throw new CatalogConflictException("El perfil o catalogo cambio durante la operacion. Recarga antes de reintentar.");
        }
    }
}
