using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
}
