using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Application.CustomerOrders;

// The delivery date is a business date, independent of host/browser timezone.
public static class DeliveryDatePolicy
{
    public static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
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

    internal static IDateTimeProvider CaptureExplicitClock(IDateTimeProvider clock)
        => new ReceivedClock(clock.Now, clock.Today, clock.LocalTimeOfDay);

    public static DateOnly GetDate(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, BusinessTimeZone).DateTime;
        var date = DateOnly.FromDateTime(local);
        var candidate = TimeOnly.FromDateTime(local) >= Cutoff ? date.AddDays(1) : date;
        while (candidate.DayOfWeek == DayOfWeek.Tuesday) candidate = candidate.AddDays(1);
        return candidate;
    }

    public static DateOnly NextBusinessDate(DateOnly date)
    {
        do { date = date.AddDays(1); } while (date.DayOfWeek == DayOfWeek.Tuesday);
        return date;
    }

    public static DateOnly LocalDate(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, BusinessTimeZone).DateTime);
    public static TimeOnly LocalTime(DateTimeOffset now) => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, BusinessTimeZone).DateTime);

    public static bool CanCaptureUpcomingDate(DateOnly date, IDateTimeProvider received)
        => date > received.Today && received.LocalTimeOfDay >= Cutoff && date == GetDate(received);

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
