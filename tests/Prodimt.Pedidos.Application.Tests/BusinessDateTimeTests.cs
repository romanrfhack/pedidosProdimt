using Prodimt.Pedidos.Infrastructure;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class BusinessDateTimeTests
{
    [Theory]
    [InlineData("2026-10-25T05:59:00Z", "2026-10-24", "23:59")]
    [InlineData("2026-10-25T06:00:00Z", "2026-10-25", "00:00")]
    [InlineData("2026-07-01T16:01:00Z", "2026-07-01", "10:01")]
    public void BusinessDateAndCutoffUseMexicoCityWhileEventsRemainUtc(string utc, string date, string time)
    {
        var now = DateTimeOffset.Parse(utc);
        var provider = new SystemDateTimeProvider(new FixedClock(now));
        Assert.Equal(now, provider.Now);
        Assert.Equal(TimeSpan.Zero, provider.Now.Offset);
        Assert.Equal(DateOnly.Parse(date), provider.Today);
        Assert.Equal(TimeOnly.Parse(time), provider.LocalTimeOfDay);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
