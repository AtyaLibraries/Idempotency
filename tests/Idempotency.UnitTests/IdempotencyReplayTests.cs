using Atya.Foundation.Results;

namespace Atya.Application.Idempotency.UnitTests;

public sealed class IdempotencyReplayTests
{
    [Fact]
    public void Constructor_Stores_Replay_Metadata()
    {
        var key = new IdempotencyKey("invoice:123");
        Result<string> result = Result.Success("created");
        var completedAt = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
        var lease = new IdempotencyLease(completedAt.AddMinutes(30));

        var replay = new IdempotencyReplay<string>(key, result, completedAt, lease);

        replay.Key.Should().Be(key);
        replay.Result.Should().BeSameAs(result);
        replay.CompletedAtUtc.Should().Be(completedAt);
        replay.Lease.Should().Be(lease);
    }

    [Fact]
    public void Constructor_With_Null_Result_Throws()
    {
        var key = new IdempotencyKey("invoice:123");
        var completedAt = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
        var lease = new IdempotencyLease(completedAt.AddMinutes(30));

        var act = () => new IdempotencyReplay<string>(key, null!, completedAt, lease);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_With_Non_Utc_Completion_Throws()
    {
        var key = new IdempotencyKey("invoice:123");
        Result<string> result = Result.Success("created");
        var completedAt = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.FromHours(4));
        var lease = new IdempotencyLease(new DateTimeOffset(2026, 7, 13, 10, 30, 0, TimeSpan.Zero));

        var act = () => new IdempotencyReplay<string>(key, result, completedAt, lease);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Replay_Before_Expiration_Returns_Stored_Result()
    {
        var replay = CreateReplay();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 29, 59, TimeSpan.Zero));

        Result<string> result = replay.Replay(clock);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("created");
    }

    [Fact]
    public void Replay_After_Expiration_Returns_Expired_Failure()
    {
        var replay = CreateReplay();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 30, 0, TimeSpan.Zero));

        Result<string> result = replay.Replay(clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(IdempotencyErrors.ExpiredCode);
    }

    [Fact]
    public void Replay_With_Null_Clock_Throws()
    {
        var replay = CreateReplay();

        var act = () => replay.Replay(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static IdempotencyReplay<string> CreateReplay()
    {
        var completedAt = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

        return new IdempotencyReplay<string>(
            new IdempotencyKey("invoice:123"),
            Result.Success("created"),
            completedAt,
            new IdempotencyLease(completedAt.AddMinutes(30)));
    }
}
