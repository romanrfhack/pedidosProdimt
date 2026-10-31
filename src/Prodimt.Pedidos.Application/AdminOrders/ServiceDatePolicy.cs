using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Services;

namespace Prodimt.Pedidos.Application.AdminOrders;

internal static class ServiceDatePolicy
{
    public static OrderSubmissionEvaluation Evaluate(DateOnly date, IDateTimeProvider clock, bool hasActiveOrder)
    {
        // Late means received after the cutoff of the service date, not merely today's clock time.
        var time = date < clock.Today ? TimeOnly.MaxValue : date > clock.Today ? TimeOnly.MinValue : clock.LocalTimeOfDay;
        return OrderSubmissionPolicy.Evaluate(time, hasActiveOrder);
    }
}
