namespace Atya.Application.Idempotency.UnitTests;

public sealed class IdempotencyLeaseTests
{
    [Fact]
    public void Constructor_With_Utc_Offset_Stores_Expiration()
    {
        var expiresAt = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

        var lease = new IdempotencyLease(expiresAt);

        lease.ExpiresAtUtc.Should().Be(expiresAt);
    }

    [Fact]
    public void Constructor_With_Non_Utc_Offset_Throws()
    {
        var expiresAt = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.FromHours(4));

        var act = () => new IdempotencyLease(expiresAt);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_With_Positive_Ttl_Computes_Expiration()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));

        IdempotencyLease lease = IdempotencyLease.From(clock, TimeSpan.FromMinutes(15));

        lease.ExpiresAtUtc.Should().Be(new DateTimeOffset(2026, 7, 13, 10, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void From_With_Null_Clock_Throws()
    {
        var act = () => IdempotencyLease.From(null!, TimeSpan.FromMinutes(1));

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void From_With_Non_Positive_Ttl_Throws(int seconds)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));

        var act = () => IdempotencyLease.From(clock, TimeSpan.FromSeconds(seconds));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IsExpired_Before_Expiration_Returns_False()
    {
        var lease = new IdempotencyLease(new DateTimeOffset(2026, 7, 13, 10, 15, 0, TimeSpan.Zero));
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 14, 59, TimeSpan.Zero));

        lease.IsExpired(clock).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_At_Expiration_Returns_True()
    {
        var expiresAt = new DateTimeOffset(2026, 7, 13, 10, 15, 0, TimeSpan.Zero);
        var lease = new IdempotencyLease(expiresAt);
        var clock = new FakeClock(expiresAt);

        lease.IsExpired(clock).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_With_Null_Clock_Throws()
    {
        var lease = new IdempotencyLease(new DateTimeOffset(2026, 7, 13, 10, 15, 0, TimeSpan.Zero));

        var act = () => lease.IsExpired(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
