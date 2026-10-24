namespace Prodimt.Pedidos.Application.Abstractions;

public sealed record OrderOperationResult<T>(T Result, bool Replayed);

public interface IOrderOperationStore
{
    Task<OrderOperationResult<T>> ExecuteAsync<T>(string operationKey, string payloadHash,
        DateTimeOffset occurredAt, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
