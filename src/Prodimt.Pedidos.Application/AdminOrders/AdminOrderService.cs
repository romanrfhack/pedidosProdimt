using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed class AdminOrderService(IOrderRepository orders, IDateTimeProvider dateTimeProvider)
{
    public async Task<IReadOnlyList<AdminOrderSummaryResponse>> GetTodayAsync(CancellationToken cancellationToken)
    {
        var todayOrders = await orders.GetByDateAsync(dateTimeProvider.Today, cancellationToken);
        return todayOrders.Select(MapSummary).ToArray();
    }

    public async Task<IReadOnlyList<AdminOrderSummaryResponse>> GetPendingReviewAsync(CancellationToken cancellationToken)
    {
        var pendingOrders = await orders.GetPendingReviewAsync(dateTimeProvider.Today, cancellationToken);
        return pendingOrders.Select(MapSummary).ToArray();
    }

    public async Task<AdminOrderSummaryResponse> ReviewAsync(
        Guid orderId,
        ReviewOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException("Order was not found.");
        }

        order.ApplyAdminDecision(request.Decision);
        order.InternalNotes = request.Notes;
        await orders.SaveChangesAsync(cancellationToken);

        return MapSummary(order);
    }

    private static AdminOrderSummaryResponse MapSummary(Order order)
    {
        return new AdminOrderSummaryResponse(
            order.Id,
            order.CustomerId,
            order.OrderDate,
            order.SubmittedAt,
            order.Status,
            order.SequenceNumber,
            order.IsLate,
            order.RequiresAdminReview,
            order.AdminReviewReason,
            order.RequestedDeliveryTime,
            order.RequestedDeliveryWindowStart,
            order.RequestedDeliveryWindowEnd,
            order.DeliveryNotes);
    }
}
