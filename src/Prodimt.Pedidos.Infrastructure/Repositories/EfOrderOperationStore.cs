using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfOrderOperationStore(PedidosDbContext dbContext) : IOrderOperationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OrderOperationResult<T>> ExecuteAsync<T>(string operationKey, string payloadHash,
        DateTimeOffset occurredAt, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var receipt = await dbContext.OrderOperations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
            if (receipt is not null)
            {
                if (receipt.PayloadHash != payloadHash)
                    throw new CustomerOrderConflictException("La clave de operacion ya se uso con otro contenido.");
                return new(JsonSerializer.Deserialize<T>(receipt.ResultJson, JsonOptions)!, true);
            }

            var result = await action(cancellationToken);
            dbContext.OrderOperations.Add(new OrderOperation
            {
                OperationKey = operationKey, PayloadHash = payloadHash, CreatedAt = occurredAt,
                ResultJson = JsonSerializer.Serialize(result, JsonOptions)
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(result, false);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackSafelyAsync(transaction);
            dbContext.ChangeTracker.Clear();
            throw new CustomerOrderConflictException("El pedido cambio durante la operacion. Recarga el detalle.");
        }
        catch (Exception ex) when (ex is SqlException { Number: 1205 or 2601 or 2627 } ||
            ex is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 } })
        {
            await RollbackSafelyAsync(transaction);
            dbContext.ChangeTracker.Clear();
            throw new CustomerOrderConflictException("Otra operacion se guardo al mismo tiempo. Reintenta con la misma clave y contenido.");
        }
        catch
        {
            await RollbackSafelyAsync(transaction);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }
    private static async Task RollbackSafelyAsync(IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch (InvalidOperationException) { /* SQL Server may already have rolled back a deadlock victim. */ }
        catch (SqlException ex) when (ex.Number is 3902 or 3903) { }
    }
}
