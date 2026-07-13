using Atya.Foundation.Time;

namespace Atya.Application.Idempotency.UnitTests;

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    private DateTimeOffset _utcNow = utcNow;

    public DateTime UtcNow => _utcNow.UtcDateTime;

    public DateTimeOffset UtcNowOffset => _utcNow;

    public DateOnly TodayUtc => DateOnly.FromDateTime(_utcNow.UtcDateTime);

    public void Advance(TimeSpan timeSpan) => _utcNow = _utcNow.Add(timeSpan);
}
