using Prodimt.Pedidos.Application.Delivery;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Api;

internal sealed class OrderDispatchPreparation(IServiceScopeFactory scopes, ILogger<OrderDispatchPreparation> logger)
{
    public Task AfterSaveAsync(Guid orderId, OrderStatus status, CancellationToken token)
        => status is OrderStatus.Submitted or OrderStatus.Accepted
            ? AfterSaveAsync([orderId], token)
            : Task.CompletedTask;

    public async Task AfterSaveAsync(IEnumerable<Guid> orderIds, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        foreach (var orderId in orderIds.Distinct())
        {
            if (deadline.IsCancellationRequested) return;
            try
            {
                // The order and its receipt have already committed. Preparation owns a separate unit of work.
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DeliveryManagementService>()
                    .PrepareForOrderAsync(orderId, deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                if (!token.IsCancellationRequested)
                    logger.LogWarning("La preparación de reparto excedió el tiempo para el pedido {OrderId}.", orderId);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("La preparación de reparto quedó pendiente para el pedido {OrderId} ({ErrorType}).",
                    orderId, ex.GetType().Name);
            }
        }
    }
}
