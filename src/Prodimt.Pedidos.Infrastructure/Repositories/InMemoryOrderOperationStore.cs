using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

// The demo list store cannot promise transactions or durable retries.
public sealed class InMemoryOrderOperationStore : IOrderOperationStore
{
    public Task<OrderOperationResult<T>> ExecuteAsync<T>(string operationKey, string payloadHash,
        DateTimeOffset occurredAt, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => throw new NotSupportedException("Configura SQL Server para importar o corregir pedidos con persistencia transaccional. La vista previa sigue disponible.");
}
