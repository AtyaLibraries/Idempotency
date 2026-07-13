using Atya.Foundation.Results;

namespace Atya.Application.Idempotency.UnitTests;

public sealed class InMemoryIdempotencyStoreTests
{
    [Fact]
    public async Task TryBeginAsync_When_Key_Is_New_Returns_Begun()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        IdempotencyKey key = new("order:1");
        IdempotencyLease lease = IdempotencyLease.From(clock, TimeSpan.FromMinutes(5));

        IdempotencyBeginResult<string> result = await store.TryBeginAsync<string>(
            key,
            lease,
            clock,
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(IdempotencyBeginStatus.Begun);
        result.IsBegun.Should().BeTrue();
        result.IsInProgress.Should().BeFalse();
        result.IsCompleted.Should().BeFalse();
        store.Keys.Should().ContainSingle().Which.Should().Be(key);
    }

    [Fact]
    public async Task TryBeginAsync_When_Key_Is_In_Progress_Returns_InProgress()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        IdempotencyKey key = new("order:2");
        IdempotencyLease lease = IdempotencyLease.From(clock, TimeSpan.FromMinutes(5));
        await store.TryBeginAsync<string>(key, lease, clock, TestContext.Current.CancellationToken);

        IdempotencyBeginResult<string> result = await store.TryBeginAsync<string>(
            key,
            lease,
            clock,
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(IdempotencyBeginStatus.InProgress);
        result.IsInProgress.Should().BeTrue();
        result.Replay.Should().BeNull();
    }

    [Fact]
    public async Task TryBeginAsync_When_Key_Is_Completed_Returns_Replay()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        IdempotencyKey key = new("order:3");
        IdempotencyLease lease = IdempotencyLease.From(clock, TimeSpan.FromMinutes(5));
        await store.TryBeginAsync<string>(key, lease, clock, TestContext.Current.CancellationToken);
        await store.CompleteAsync(key, Result.Success("created"), clock, TestContext.Current.CancellationToken);

        IdempotencyBeginResult<string> result = await store.TryBeginAsync<string>(
            key,
            lease,
            clock,
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(IdempotencyBeginStatus.Completed);
        result.IsCompleted.Should().BeTrue();
        result.Replay.Should().NotBeNull();
        result.Replay!.Result.Value.Should().Be("created");
        result.Replay.CompletedAtUtc.Should().Be(clock.UtcNowOffset);
    }

    [Fact]
    public async Task TryBeginAsync_When_Record_Is_Expired_Replaces_Record_And_Returns_Begun()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        IdempotencyKey key = new("order:4");
        IdempotencyLease lease = IdempotencyLease.From(clock, TimeSpan.FromSeconds(1));
        await store.TryBeginAsync<string>(key, lease, clock, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        IdempotencyBeginResult<string> result = await store.TryBeginAsync<string>(
            key,
            IdempotencyLease.From(clock, TimeSpan.FromMinutes(5)),
            clock,
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(IdempotencyBeginStatus.Begun);
    }

    [Fact]
    public async Task TryBeginAsync_When_Cancelled_Throws()
    {
        var store = new InMemoryIdempotencyStore();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = async () => await store.TryBeginAsync<string>(
            new IdempotencyKey("order:5"),
            new IdempotencyLease(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero)),
            new FakeClock(new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero)),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TryBeginAsync_With_Null_Clock_Throws()
    {
        var store = new InMemoryIdempotencyStore();

        var act = async () => await store.TryBeginAsync<string>(
            new IdempotencyKey("order:6"),
            new IdempotencyLease(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero)),
            null!,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CompleteAsync_With_Null_Result_Throws()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));

        var act = async () => await store.CompleteAsync<string>(
            new IdempotencyKey("order:7"),
            null!,
            clock,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CompleteAsync_With_Null_Clock_Throws()
    {
        var store = new InMemoryIdempotencyStore();

        var act = async () => await store.CompleteAsync(
            new IdempotencyKey("order:8"),
            Result.Success("created"),
            null!,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CompleteAsync_When_Key_Has_Not_Begun_Throws()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));

        var act = async () => await store.CompleteAsync(
            new IdempotencyKey("order:9"),
            Result.Success("created"),
            clock,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task TryBeginAsync_When_Result_Type_Differs_Throws()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        IdempotencyKey key = new("order:10");
        IdempotencyLease lease = IdempotencyLease.From(clock, TimeSpan.FromMinutes(5));
        await store.TryBeginAsync<string>(key, lease, clock, TestContext.Current.CancellationToken);
        await store.CompleteAsync(key, Result.Success("created"), clock, TestContext.Current.CancellationToken);

        var act = async () => await store.TryBeginAsync<int>(key, lease, clock, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
