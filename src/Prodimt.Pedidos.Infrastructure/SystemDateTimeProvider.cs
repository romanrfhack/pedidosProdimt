using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    private readonly TimeProvider _clock;

    public SystemDateTimeProvider() : this(TimeProvider.System) { }

    public SystemDateTimeProvider(TimeProvider clock) => _clock = clock;

    // Event timestamps use UTC; business dates and cutoffs use Mexico City.
    public DateTimeOffset Now => _clock.GetUtcNow();
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, BusinessTimeZone).DateTime);
    public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, BusinessTimeZone).DateTime);
}
