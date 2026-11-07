using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;

namespace Prodimt.Pedidos.Application.AdminOrders;

internal static class ServiceDatePolicy
{
    public static void RequireWriteDate(DateOnly date, DateOnly today, AdminActorContext? actor)
    {
        if (actor?.IsSystemAdministrator != true && date != today)
            throw new UnauthorizedAccessException("Solo el Administrador del sistema puede registrar o modificar pedidos de otras fechas.");
    }

    public static void RequireCorrectionAccess(AdminActorContext? actor)
    {
        if (actor?.IsSystemAdministrator != true)
            throw new UnauthorizedAccessException("Solo el Administrador del sistema puede corregir pedidos guardados.");
    }

    public static OrderSubmissionEvaluation Evaluate(DateOnly date, IDateTimeProvider clock, bool hasActiveOrder)
        => EvaluateAtDate(date, clock.Today, clock.LocalTimeOfDay, hasActiveOrder);

    public static OrderSubmissionEvaluation EvaluateForAdminWrite(DateOnly date, IDateTimeProvider clock, bool hasActiveOrder,
        AdminActorContext? actor, TimeOnly? sameDayCutoff = null)
    {
        var today = clock.Today;
        RequireWriteDate(date, today, actor);
        // An authorized backfill records a completed service day.
        if (date < today) return new(false, false, null, OrderStatus.Accepted);
        if (date == today && sameDayCutoff is { } cutoff)
            return OrderSubmissionPolicy.Evaluate(isLate: clock.LocalTimeOfDay >= cutoff, hasExistingActiveOrderToday: hasActiveOrder);
        return EvaluateAtDate(date, today, clock.LocalTimeOfDay, hasActiveOrder);
    }

    private static OrderSubmissionEvaluation EvaluateAtDate(DateOnly date, DateOnly today, TimeOnly localTime, bool hasActiveOrder)
    {
        var time = date < today ? TimeOnly.MaxValue : date > today ? TimeOnly.MinValue : localTime;
        return OrderSubmissionPolicy.Evaluate(time, hasActiveOrder);
    }
}
