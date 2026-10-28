using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public enum CustomerDayStatus { AwaitingOrder, HasOrder, NoOrderConfirmed, ScheduledRest, Inactive }

public sealed record AdminCustomerDayStatus(Guid CustomerId, CustomerDayStatus Status, int OrderCount, bool RequiresReview);

public sealed record AdminCustomerDayResponse(DateOnly Date, IReadOnlyList<AdminCustomerDayStatus> Customers);

public sealed class AdminCustomerDayService(
    ICustomerRepository customers,
    IOrderRepository orders,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<AdminCustomerDayResponse> GetTodayAsync(CancellationToken cancellationToken)
    {
        // Read the business date once; UTC/server/browser dates are not service dates.
        var date = dateTimeProvider.Today;
        var allCustomers = await customers.GetAllAsync(cancellationToken);
        var byCustomer = (await orders.GetByDateAsync(date, cancellationToken))
            .Where(order => order.CustomerId.HasValue && order.OrderDate == date)
            .ToLookup(order => order.CustomerId!.Value);
        var result = allCustomers.Select(customer =>
        {
            var dayOrders = byCustomer[customer.Id].ToArray();
            var active = dayOrders.Where(order => order.Status is OrderStatus.Submitted
                or OrderStatus.Accepted or OrderStatus.PendingAdminReview).ToArray();
            var latest = dayOrders.Where(order => order.Status is not (OrderStatus.Draft or OrderStatus.Superseded))
                .OrderByDescending(order => order.SequenceNumber).ThenByDescending(order => order.SubmittedAt).FirstOrDefault();
            var status = active.Length > 0 ? CustomerDayStatus.HasOrder
                : !customer.IsActive ? CustomerDayStatus.Inactive
                : latest?.Status == OrderStatus.NoOrder ? CustomerDayStatus.NoOrderConfirmed
                : customer.IsScheduledRestOn(date) ? CustomerDayStatus.ScheduledRest
                : CustomerDayStatus.AwaitingOrder;
            return new AdminCustomerDayStatus(customer.Id, status, active.Length,
                active.Any(order => order.RequiresAdminReview || order.Status == OrderStatus.PendingAdminReview));
        }).ToArray();
        // This projection never creates NoOrder records or changes orders/demand.
        return new AdminCustomerDayResponse(date, result);
    }
}
