using Atya.Foundation.Time;

namespace Atya.Application.Idempotency.UnitTests;

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTime UtcNow => utcNow.UtcDateTime;

    public DateTimeOffset UtcNowOffset => utcNow;

    public DateOnly TodayUtc => DateOnly.FromDateTime(utcNow.UtcDateTime);
}
