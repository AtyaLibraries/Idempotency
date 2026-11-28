using Atya.Foundation.Results;
using Microsoft.Extensions.Time.Testing;

namespace Atya.Application.Idempotency.UnitTests;

/// <summary>
/// The store contract. Every <see cref="IIdempotencyStore"/> implementation must pass this suite by deriving from it.
/// </summary>
public abstract class IdempotencyStoreConformanceTests
{
    private static readonly IdempotencyKey s_key = new("orders:create:42");

    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));

    protected abstract IIdempotencyStore CreateStore(TimeProvider timeProvider);

    [Fact]
    public async Task Begin_Parallel_YieldsExactlyOneOwner()
    {
        for (int round = 0; round < 1000; round++)
        {
            IIdempotencyStore store = CreateStore(Time);
            var key = new IdempotencyKey("race:" + round);
            using var barrier = new Barrier(8);

            Task<IdempotencyBeginResult<string>>[] tasks = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await store.TryBeginAsync<string>(key, TimeSpan.FromMinutes(1), null, CancellationToken.None);
                }, TestContext.Current.CancellationToken))
                .ToArray();
            IdempotencyBeginResult<string>[] results = await Task.WhenAll(tasks);

            results.Count(r => r.IsBegun).Should().Be(1);
            results.Count(r => r.IsInProgress).Should().Be(7);
        }
    }

    [Fact]
    public async Task Begin_ReturnsExecutionWithFutureLease()
    {
        IIdempotencyStore store = CreateStore(Time);

        var begin = await Begin<string>(store, TimeSpan.FromSeconds(30));

        begin.IsBegun.Should().BeTrue();
        begin.Execution!.Key.Should().Be(s_key);
        begin.Execution.LeaseExpiresAtUtc.Should().Be(Time.GetUtcNow().AddSeconds(30));
        begin.Execution.Fence.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Fencing_ASupersededOwnerCannotComplete_RenewOrAbandon()
    {
        IIdempotencyStore store = CreateStore(Time);
        var a = (await Begin<string>(store, TimeSpan.FromSeconds(10))).Execution!;
        Time.Advance(TimeSpan.FromSeconds(11));
        var b = (await Begin<string>(store, TimeSpan.FromSeconds(10))).Execution!;

        (await store.CompleteAsync(a, Result.Success("from-a"), TimeSpan.FromMinutes(1), CancellationToken.None))
            .Should().Be(IdempotencyTransition.Superseded);
        (await store.RenewAsync(a, TimeSpan.FromMinutes(1), CancellationToken.None))
            .Should().Be(IdempotencyTransition.Superseded);
        (await store.AbandonAsync(a, CancellationToken.None))
            .Should().Be(IdempotencyTransition.Superseded);
        (await store.CompleteAsync(b, Result.Success("from-b"), TimeSpan.FromMinutes(1), CancellationToken.None))
            .Should().Be(IdempotencyTransition.Applied);

        var replay = await Begin<string>(store, TimeSpan.FromSeconds(10));
        replay.IsCompleted.Should().BeTrue();
        replay.Replay!.Result.Value.Should().Be("from-b");
    }

    [Fact]
    public async Task Transitions_OnAnUnknownExecution_ReportNotFound()
    {
        IIdempotencyStore store = CreateStore(Time);
        var ghost = new IdempotencyExecution(s_key, new IdempotencyFence("x"), Time.GetUtcNow());

        (await store.CompleteAsync(ghost, Result.Success("v"), TimeSpan.FromMinutes(1), CancellationToken.None)).Should().Be(IdempotencyTransition.NotFound);
        (await store.RenewAsync(ghost, TimeSpan.FromMinutes(1), CancellationToken.None)).Should().Be(IdempotencyTransition.NotFound);
        (await store.AbandonAsync(ghost, CancellationToken.None)).Should().Be(IdempotencyTransition.NotFound);
    }

    [Fact]
    public async Task Completing_AnAlreadyCompletedExecution_IsSuperseded()
    {
        IIdempotencyStore store = CreateStore(Time);
        var execution = (await Begin<string>(store, TimeSpan.FromSeconds(10))).Execution!;
        await store.CompleteAsync(execution, Result.Success("first"), TimeSpan.FromMinutes(1), CancellationToken.None);

        (await store.CompleteAsync(execution, Result.Success("second"), TimeSpan.FromMinutes(1), CancellationToken.None))
            .Should().Be(IdempotencyTransition.Superseded);
        (await Begin<string>(store, TimeSpan.FromSeconds(10))).Replay!.Result.Value.Should().Be("first");
    }

    [Fact]
    public async Task ReplayWindow_StartsAtCompletion_NotAtBegin()
    {
        IIdempotencyStore store = CreateStore(Time);
        var execution = (await Begin<string>(store, TimeSpan.FromSeconds(10))).Execution!;
        await store.RenewAsync(execution, TimeSpan.FromMinutes(5), CancellationToken.None);
        Time.Advance(TimeSpan.FromSeconds(30));

        await store.CompleteAsync(execution, Result.Success("v"), TimeSpan.FromSeconds(10), CancellationToken.None);
        Time.Advance(TimeSpan.FromSeconds(9));
        var within = await Begin<string>(store, TimeSpan.FromSeconds(10));
        Time.Advance(TimeSpan.FromSeconds(2));
        var after = await Begin<string>(store, TimeSpan.FromSeconds(10));

        within.IsCompleted.Should().BeTrue();
        within.Replay!.CompletedAtUtc.Should().Be(new DateTimeOffset(2026, 11, 27, 10, 0, 30, TimeSpan.Zero));
        within.Replay.ExpiresAtUtc.Should().Be(within.Replay.CompletedAtUtc.AddSeconds(10));
        after.IsBegun.Should().BeTrue();
    }

    [Fact]
    public async Task Abandon_ReleasesTheKeyForTheNextCaller()
    {
        IIdempotencyStore store = CreateStore(Time);
        var execution = (await Begin<string>(store, TimeSpan.FromMinutes(1))).Execution!;

        (await store.AbandonAsync(execution, CancellationToken.None)).Should().Be(IdempotencyTransition.Applied);

        (await Begin<string>(store, TimeSpan.FromMinutes(1))).IsBegun.Should().BeTrue();
    }

    [Fact]
    public async Task Renew_ExtendsTheLeaseFromNow()
    {
        IIdempotencyStore store = CreateStore(Time);
        var execution = (await Begin<string>(store, TimeSpan.FromSeconds(10))).Execution!;
        Time.Advance(TimeSpan.FromSeconds(5));

        (await store.RenewAsync(execution, TimeSpan.FromSeconds(30), CancellationToken.None)).Should().Be(IdempotencyTransition.Applied);
        Time.Advance(TimeSpan.FromSeconds(20));
        (await Begin<string>(store, TimeSpan.FromSeconds(10))).IsInProgress.Should().BeTrue();
        Time.Advance(TimeSpan.FromSeconds(11));

        (await Begin<string>(store, TimeSpan.FromSeconds(10))).IsBegun.Should().BeTrue();
    }

    [Fact]
    public async Task LateCompletion_ByAnExpiredOwnerNobodySuperseded_IsAccepted()
    {
        IIdempotencyStore store = CreateStore(Time);
        var execution = (await Begin<string>(store, TimeSpan.FromSeconds(10))).Execution!;
        Time.Advance(TimeSpan.FromSeconds(60));

        (await store.CompleteAsync(execution, Result.Success("late"), TimeSpan.FromMinutes(1), CancellationToken.None))
            .Should().Be(IdempotencyTransition.Applied);

        (await Begin<string>(store, TimeSpan.FromSeconds(10))).Replay!.Result.Value.Should().Be("late");
    }

    [Fact]
    public async Task SameKeyDifferentFingerprint_IsAPayloadMismatch_AndNullIsUnchecked()
    {
        IIdempotencyStore store = CreateStore(Time);
        var first = await store.TryBeginAsync<string>(s_key, TimeSpan.FromMinutes(1), "fp-1", CancellationToken.None);

        (await store.TryBeginAsync<string>(s_key, TimeSpan.FromMinutes(1), "fp-2", CancellationToken.None)).Status
            .Should().Be(IdempotencyBeginStatus.PayloadMismatch);
        (await store.TryBeginAsync<string>(s_key, TimeSpan.FromMinutes(1), "fp-1", CancellationToken.None)).Status
            .Should().Be(IdempotencyBeginStatus.InProgress);
        (await store.TryBeginAsync<string>(s_key, TimeSpan.FromMinutes(1), null, CancellationToken.None)).Status
            .Should().Be(IdempotencyBeginStatus.InProgress);
        first.IsBegun.Should().BeTrue();
    }

    [Fact]
    public async Task ReplayWithADifferentValueType_IsAPayloadMismatch_NotAnException()
    {
        IIdempotencyStore store = CreateStore(Time);
        var execution = (await Begin<string>(store, TimeSpan.FromMinutes(1))).Execution!;
        await store.CompleteAsync(execution, Result.Success("text"), TimeSpan.FromMinutes(1), CancellationToken.None);

        var wrongType = await store.TryBeginAsync<int>(s_key, TimeSpan.FromMinutes(1), null, CancellationToken.None);

        wrongType.Status.Should().Be(IdempotencyBeginStatus.PayloadMismatch);
    }

    [Fact]
    public async Task StoredFailureResults_AreReplayedWhenCompleted()
    {
        IIdempotencyStore store = CreateStore(Time);
        var execution = (await Begin<string>(store, TimeSpan.FromMinutes(1))).Execution!;
        await store.CompleteAsync(execution, Result.Failure<string>("x.failed", "failed", ErrorKind.Conflict), TimeSpan.FromMinutes(1), CancellationToken.None);

        var replay = await Begin<string>(store, TimeSpan.FromMinutes(1));

        replay.Replay!.Result.IsFailure.Should().BeTrue();
        replay.Replay.Result.Error.Code.Should().Be("x.failed");
    }

    [Fact]
    public async Task CancelledToken_ThrowsBeforeTouchingState()
    {
        IIdempotencyStore store = CreateStore(Time);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> action = async () => await store.TryBeginAsync<string>(s_key, TimeSpan.FromMinutes(1), null, cancelled.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        (await Begin<string>(store, TimeSpan.FromMinutes(1))).IsBegun.Should().BeTrue();
    }

    protected static ValueTask<IdempotencyBeginResult<TValue>> Begin<TValue>(IIdempotencyStore store, TimeSpan lease) =>
        store.TryBeginAsync<TValue>(s_key, lease, null, CancellationToken.None);
}

public sealed class InMemoryIdempotencyStoreConformanceTests : IdempotencyStoreConformanceTests
{
    protected override IIdempotencyStore CreateStore(TimeProvider timeProvider) => new InMemoryIdempotencyStore(timeProvider);
}
