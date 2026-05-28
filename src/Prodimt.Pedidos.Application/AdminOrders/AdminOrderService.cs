using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed class AdminOrderService(
    IOrderRepository orders,
    IOrderAuditLogRepository auditLogs,
    ICustomerRepository customers,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<IReadOnlyList<AdminOrderSummaryResponse>> GetTodayAsync(CancellationToken cancellationToken)
    {
        var todayOrders = await orders.GetByDateAsync(dateTimeProvider.Today, cancellationToken);
        return await MapSummariesAsync(todayOrders, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminOrderSummaryResponse>> GetPendingReviewAsync(CancellationToken cancellationToken)
    {
        var pendingOrders = await orders.GetPendingReviewAsync(dateTimeProvider.Today, cancellationToken);
        return await MapSummariesAsync(pendingOrders, cancellationToken);
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

        if (request.Decision is not (AdminDecision.Accepted or AdminDecision.Rejected or AdminDecision.AcceptedWithChanges))
        {
            throw new ArgumentException("La decision administrativa debe ser Accepted, Rejected o AcceptedWithChanges.", nameof(request));
        }

        order.ApplyAdminDecision(request.Decision);
        order.InternalNotes = request.InternalNotes;
        await auditLogs.AddAsync(OrderAuditLog.Create(
            order,
            OrderAuditEventType.AdminDecisionRecorded,
            dateTimeProvider.Now,
            AuditActorType.Admin,
            $"Decision administrativa registrada: {request.Decision}."), cancellationToken);
        await orders.SaveChangesAsync(cancellationToken);

        var customerNames = await GetCustomerNamesAsync([order], cancellationToken);
        return MapSummary(order, customerNames);
    }

    public async Task<IReadOnlyList<OrderAuditLogResponse>> GetAuditAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException("Order was not found.");
        }

        var orderAuditLogs = await auditLogs.GetByOrderIdAsync(orderId, cancellationToken);
        return orderAuditLogs.Select(MapAuditLog).ToArray();
    }

    private async Task<IReadOnlyList<AdminOrderSummaryResponse>> MapSummariesAsync(
        IReadOnlyList<Order> sourceOrders,
        CancellationToken cancellationToken)
    {
        var customerNames = await GetCustomerNamesAsync(sourceOrders, cancellationToken);
        return sourceOrders.Select(order => MapSummary(order, customerNames)).ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetCustomerNamesAsync(
        IEnumerable<Order> sourceOrders,
        CancellationToken cancellationToken)
    {
        var customerIds = sourceOrders
            .Select(order => order.CustomerId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        var orderCustomers = await customers.GetByIdsAsync(customerIds, cancellationToken);
        return orderCustomers.ToDictionary(customer => customer.Id, customer => customer.Name);
    }

    private static AdminOrderSummaryResponse MapSummary(Order order, IReadOnlyDictionary<Guid, string> customerNames)
    {
        var customerName = order.CustomerId is { } customerId && customerNames.TryGetValue(customerId, out var name)
            ? name
            : "Mostrador";

        return new AdminOrderSummaryResponse(
            order.Id,
            order.CustomerId,
            customerName,
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
            order.DeliveryNotes,
            order.AdminDecision);
    }

    private static OrderAuditLogResponse MapAuditLog(OrderAuditLog auditLog)
    {
        return new OrderAuditLogResponse(
            auditLog.Id,
            auditLog.OrderId,
            auditLog.CustomerId,
            auditLog.EventType,
            auditLog.OccurredAt,
            auditLog.ActorType,
            auditLog.ActorId,
            auditLog.ActorDisplayName,
            auditLog.OrderStatus,
            auditLog.AdminReviewReason,
            auditLog.AdminDecision,
            auditLog.Summary,
            auditLog.MetadataJson);
    }
}
