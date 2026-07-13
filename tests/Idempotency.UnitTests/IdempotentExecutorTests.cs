using Atya.Foundation.Results;

namespace Atya.Application.Idempotency.UnitTests;

public sealed class IdempotentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_When_Key_Is_New_Runs_Operation_And_Completes_Result()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        var executor = new IdempotentExecutor(store, clock);
        var calls = 0;

        Result<string> result = await executor.ExecuteAsync(
            new IdempotencyKey("invoice:1"),
            TimeSpan.FromMinutes(5),
            _ =>
            {
                calls++;
                return ValueTask.FromResult(Result.Success("created"));
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("created");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_When_Key_Is_Completed_Replays_Result()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        var executor = new IdempotentExecutor(store, clock);
        IdempotencyKey key = new("invoice:2");
        await executor.ExecuteAsync(
            key,
            TimeSpan.FromMinutes(5),
            _ => ValueTask.FromResult(Result.Success("created")),
            TestContext.Current.CancellationToken);

        Result<string> result = await executor.ExecuteAsync(
            key,
            TimeSpan.FromMinutes(5),
            _ => ValueTask.FromResult(Result.Success("should-not-run")),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("created");
    }

    [Fact]
    public async Task ExecuteAsync_When_Key_Is_In_Progress_Returns_InProgress_Failure()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        var executor = new IdempotentExecutor(store, clock);
        IdempotencyKey key = new("invoice:3");
        await store.TryBeginAsync<string>(
            key,
            IdempotencyLease.From(clock, TimeSpan.FromMinutes(5)),
            clock,
            TestContext.Current.CancellationToken);

        Result<string> result = await executor.ExecuteAsync(
            key,
            TimeSpan.FromMinutes(5),
            _ => ValueTask.FromResult(Result.Success("should-not-run")),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(IdempotencyErrors.InProgressCode);
    }

    [Fact]
    public async Task ExecuteAsync_With_Same_Key_In_Parallel_Runs_Operation_Once()
    {
        var store = new InMemoryIdempotencyStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
        var executor = new IdempotentExecutor(store, clock);
        IdempotencyKey key = new("invoice:4");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;

        Task<Result<string>> first = Task.Run(async () => await executor.ExecuteAsync(
            key,
            TimeSpan.FromMinutes(5),
            _ =>
            {
                Interlocked.Increment(ref calls);
                entered.Set();
                release.Wait(CancellationToken.None);
                return ValueTask.FromResult(Result.Success("created"));
            },
            TestContext.Current.CancellationToken));

        entered.Wait(TestContext.Current.CancellationToken);

        Result<string> second = await executor.ExecuteAsync(
            key,
            TimeSpan.FromMinutes(5),
            _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(Result.Success("duplicate"));
            },
            TestContext.Current.CancellationToken);

        release.Set();
        Result<string> firstResult = await first;

        calls.Should().Be(1);
        firstResult.IsSuccess.Should().BeTrue();
        firstResult.Value.Should().Be("created");
        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be(IdempotencyErrors.InProgressCode);
    }

    [Fact]
    public async Task ExecuteAsync_With_Null_Operation_Throws()
    {
        var executor = new IdempotentExecutor(
            new InMemoryIdempotencyStore(),
            new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero)));

        var act = async () => await executor.ExecuteAsync<string>(
            new IdempotencyKey("invoice:5"),
            TimeSpan.FromMinutes(5),
            null!,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_With_Null_Store_Throws()
    {
        var act = () => new IdempotentExecutor(
            null!,
            new FakeClock(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero)));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_With_Null_Clock_Throws()
    {
        var act = () => new IdempotentExecutor(new InMemoryIdempotencyStore(), null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
