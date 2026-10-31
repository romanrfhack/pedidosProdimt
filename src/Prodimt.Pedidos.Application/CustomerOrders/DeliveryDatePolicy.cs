using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Application.CustomerOrders;

// The delivery date is a business date, independent of host/browser timezone.
public static class DeliveryDatePolicy
{
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    public static readonly TimeOnly Cutoff = new(18, 0);

    public static DateOnly GetDate(IDateTimeProvider clock) => GetDate(clock.Now);

    // Capture once when a confirmation arrives. Database waits cannot change the
    // received date while leaving the order timestamp on the other side of the cutoff.
    public static IDateTimeProvider Capture(IDateTimeProvider clock)
    {
        var now = clock.Now;
        var local = TimeZoneInfo.ConvertTime(now, BusinessTimeZone).DateTime;
        return new ReceivedClock(now, DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    public static DateOnly GetDate(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, BusinessTimeZone).DateTime;
        var date = DateOnly.FromDateTime(local);
        return TimeOnly.FromDateTime(local) >= Cutoff ? date.AddDays(1) : date;
    }

    public static DateOnly RequireExpectedDate(DateOnly? expected, IDateTimeProvider clock)
    {
        if (expected is null || expected == DateOnly.MinValue)
            throw new ArgumentException("Confirma la fecha de entrega mostrada antes de guardar.");
        var now = clock.Now;
        var date = GetDate(now);
        if (date != expected)
            throw new DeliveryDateChangedException(date, now);
        return date;
    }

    private sealed record ReceivedClock(DateTimeOffset Now, DateOnly Today, TimeOnly LocalTimeOfDay) : IDateTimeProvider;
}

public sealed class DeliveryDateChangedException(DateOnly deliveryDate, DateTimeOffset serverNow)
    : Exception("La fecha de entrega cambió por el horario de corte. Conserva tu captura y confirma la nueva fecha antes de continuar.")
{
    public DateOnly DeliveryDate { get; } = deliveryDate;
    public DateTimeOffset ServerNow { get; } = serverNow;
}
