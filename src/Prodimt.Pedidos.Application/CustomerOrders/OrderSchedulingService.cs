using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Services;
namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed record OrderingWindow(TimeOnly AutomaticConfirmationCutoff, string CutoffSource,
    bool RequiresConfirmation, bool RequiresDateAcceptance, DateOnly DeliveryDate, DateOnly Today, bool RequestOnly);

public sealed class OrderSchedulingService(IOrderingScheduleRepository schedules)
{
    public async Task<OrderingWindow> GetAsync(Customer customer, DateTimeOffset now, bool requestOnly, CancellationToken token)
    {
        var route = string.IsNullOrWhiteSpace(customer.RouteCode) ? null : await schedules.GetAsync(customer.RouteCode.Trim().ToUpperInvariant(), token);
        var cutoff = customer.AutomaticConfirmationCutoff ?? route?.AutomaticConfirmationCutoff ?? OrderSubmissionPolicy.DefaultCutoffTime;
        var today = DeliveryDatePolicy.LocalDate(now);
        var date = DeliveryDatePolicy.GetDate(now);
        return new(cutoff, customer.AutomaticConfirmationCutoff.HasValue ? "customer" : route?.AutomaticConfirmationCutoff.HasValue == true ? "route" : "default",
            requestOnly || date == today && DeliveryDatePolicy.LocalTime(now) >= cutoff,
            date != today, date, today, requestOnly);
    }
}
