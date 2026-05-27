namespace Prodimt.Pedidos.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTimeOffset Now { get; }

    DateOnly Today { get; }

    TimeOnly LocalTimeOfDay { get; }
}
