using Atya.Foundation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Atya.Application.Idempotency.UnitTests;

public sealed class IdempotentExecutorTests
{
    private static readonly IdempotencyKey s_key = new("orders:create:42");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(IdempotencyCompletionPolicy.SuccessOnly)]
    [InlineData(IdempotencyCompletionPolicy.SuccessAndFailure)]
    public async Task Success_IsCompletedAndReplayed_UnderBothPolicies(IdempotencyCompletionPolicy policy)
    {
        (IdempotentExecutor executor, _) = Create(policy);
        int runs = 0;

        Result<string> first = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Success("v")), cancellationToken: TestContext.Current.CancellationToken);
        Result<string> second = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Success("other")), cancellationToken: TestContext.Current.CancellationToken);

        first.Value.Should().Be("v");
        second.Value.Should().Be("v");
        runs.Should().Be(1);
    }

    [Fact]
    public async Task FailureResult_SuccessOnly_IsAbandoned_SoTheCallerCanRetry()
    {
        (IdempotentExecutor executor, _) = Create(IdempotencyCompletionPolicy.SuccessOnly);
        int runs = 0;

        Result<string> first = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Failure<string>("x.fail", "no", ErrorKind.Conflict)), cancellationToken: TestContext.Current.CancellationToken);
        Result<string> second = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Success("retry")), cancellationToken: TestContext.Current.CancellationToken);

        first.IsFailure.Should().BeTrue();
        second.Value.Should().Be("retry");
        runs.Should().Be(2);
    }

    [Fact]
    public async Task FailureResult_SuccessAndFailure_IsCompletedAndReplayed()
    {
        (IdempotentExecutor executor, _) = Create(IdempotencyCompletionPolicy.SuccessAndFailure);
        int runs = 0;

        await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Failure<string>("x.fail", "no", ErrorKind.Conflict)), cancellationToken: TestContext.Current.CancellationToken);
        Result<string> second = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Success("retry")), cancellationToken: TestContext.Current.CancellationToken);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("x.fail");
        runs.Should().Be(1);
    }

    [Fact]
    public async Task PerCallPolicy_OverridesTheExecutorDefault()
    {
        (IdempotentExecutor executor, _) = Create(IdempotencyCompletionPolicy.SuccessOnly);
        int runs = 0;
        var call = new IdempotencyCallOptions { CompletionPolicy = IdempotencyCompletionPolicy.SuccessAndFailure };

        await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Failure<string>("x.fail", "no", ErrorKind.Conflict)), call, TestContext.Current.CancellationToken);
        Result<string> second = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Success("retry")), call, TestContext.Current.CancellationToken);

        second.IsFailure.Should().BeTrue();
        runs.Should().Be(1);
    }

    [Theory]
    [InlineData(IdempotencyCompletionPolicy.SuccessOnly)]
    [InlineData(IdempotencyCompletionPolicy.SuccessAndFailure)]
    public async Task Exception_AbandonsTheKey_AndIsRethrown(IdempotencyCompletionPolicy policy)
    {
        (IdempotentExecutor executor, InMemoryIdempotencyStore store) = Create(policy);

        Func<Task> action = async () => await executor.ExecuteAsync<string>(s_key, _ => throw new InvalidOperationException("boom"), cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        store.Count.Should().Be(0);
        Result<string> retry = await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("ok")), cancellationToken: TestContext.Current.CancellationToken);
        retry.Value.Should().Be("ok");
    }

    [Theory]
    [InlineData(IdempotencyCompletionPolicy.SuccessOnly)]
    [InlineData(IdempotencyCompletionPolicy.SuccessAndFailure)]
    public async Task CallerCancellation_AbandonsTheKey_AndIsRethrown(IdempotencyCompletionPolicy policy)
    {
        (IdempotentExecutor executor, InMemoryIdempotencyStore store) = Create(policy);
        using var cts = new CancellationTokenSource();

        Func<Task> action = async () => await executor.ExecuteAsync<string>(
            s_key,
            async token =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return Result.Success("never");
            },
            cancellationToken: cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task Completion_SurvivesACancelledCallerToken()
    {
        (IdempotentExecutor executor, _) = Create(IdempotencyCompletionPolicy.SuccessOnly);
        using var cts = new CancellationTokenSource();
        int runs = 0;

        Result<string> first = await executor.ExecuteAsync(
            s_key,
            async _ =>
            {
                runs++;
                await cts.CancelAsync();
                return Result.Success("done");
            },
            cancellationToken: cts.Token);
        Result<string> replay = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Success("again")), cancellationToken: TestContext.Current.CancellationToken);

        first.Value.Should().Be("done");
        replay.Value.Should().Be("done");
        runs.Should().Be(1);
    }

    [Fact]
    public async Task ReplayWindow_StartsAtCompletion_ForASlowOperation()
    {
        var options = new IdempotencyExecutorOptions { ExecutionLease = TimeSpan.FromSeconds(10), ReplayTimeToLive = TimeSpan.FromMinutes(5) };
        (IdempotentExecutor executor, _) = Create(options);
        int runs = 0;

        await executor.ExecuteAsync(
            s_key,
            _ =>
            {
                runs++;
                _time.Advance(TimeSpan.FromSeconds(30));
                return ValueTask.FromResult(Result.Success("slow"));
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Result<string> second = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Success("x")), cancellationToken: TestContext.Current.CancellationToken);
        Result<string> third = await executor.ExecuteAsync(s_key, _ => Run(ref runs, Result.Success("x")), cancellationToken: TestContext.Current.CancellationToken);

        second.Value.Should().Be("slow");
        third.Value.Should().Be("slow");
        runs.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentCaller_SeesInProgress()
    {
        (IdempotentExecutor executor, _) = Create(IdempotencyCompletionPolicy.SuccessOnly);
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        Task<Result<string>> first = executor.ExecuteAsync(
            s_key,
            async _ =>
            {
                started.SetResult();
                await release.Task;
                return Result.Success("v");
            },
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        await started.Task;

        Result<string> second = await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("x")), cancellationToken: TestContext.Current.CancellationToken);
        release.SetResult();
        await first;

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be(IdempotencyErrors.InProgressCode);
    }

    [Fact]
    public async Task FingerprintMismatch_FailsWithPayloadMismatch()
    {
        (IdempotentExecutor executor, _) = Create(IdempotencyCompletionPolicy.SuccessOnly);
        await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("v")), new IdempotencyCallOptions { RequestFingerprint = "a" }, TestContext.Current.CancellationToken);

        Result<string> other = await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("v")), new IdempotencyCallOptions { RequestFingerprint = "b" }, TestContext.Current.CancellationToken);

        other.Error.Code.Should().Be(IdempotencyErrors.PayloadMismatchCode);
    }

    [Fact]
    public async Task DifferentValueTypeOnReplay_FailsWithPayloadMismatch_InsteadOfThrowing()
    {
        (IdempotentExecutor executor, _) = Create(IdempotencyCompletionPolicy.SuccessOnly);
        await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("v")), cancellationToken: TestContext.Current.CancellationToken);

        Result<int> other = await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success(1)), cancellationToken: TestContext.Current.CancellationToken);

        other.Error.Code.Should().Be(IdempotencyErrors.PayloadMismatchCode);
    }

    [Fact]
    public async Task FullStore_FailsWithCapacityExceeded()
    {
        var store = new InMemoryIdempotencyStore(_time, 1);
        var executor = new IdempotentExecutor(store, _time);
        await executor.ExecuteAsync(new IdempotencyKey("a"), _ => ValueTask.FromResult(Result.Success("v")), cancellationToken: TestContext.Current.CancellationToken);

        Result<string> overflow = await executor.ExecuteAsync(new IdempotencyKey("b"), _ => ValueTask.FromResult(Result.Success("v")), cancellationToken: TestContext.Current.CancellationToken);

        overflow.Error.Code.Should().Be(IdempotencyErrors.CapacityExceededCode);
        overflow.Error.Kind.Should().Be(ErrorKind.Unavailable);
    }

    [Fact]
    public async Task SupersededCompletion_StillReturnsTheOperationResult_AndLogsAWarning()
    {
        var logger = new ListLogger();
        var store = new ForcedTransitionStore(new InMemoryIdempotencyStore(_time)) { Complete = IdempotencyTransition.Superseded };
        var executor = new IdempotentExecutor(store, _time, logger: logger);

        Result<string> result = await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("mine")), cancellationToken: TestContext.Current.CancellationToken);

        result.Value.Should().Be("mine");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == 9202);
    }

    [Fact]
    public async Task FinalizationFailure_IsSwallowed_Logged_AndReportedToTheObserver()
    {
        var logger = new ListLogger();
        var failures = new List<IdempotencyFinalizationFailure>();
        var store = new ForcedTransitionStore(new InMemoryIdempotencyStore(_time)) { CompleteThrows = new InvalidOperationException("store down") };
        var executor = new IdempotentExecutor(store, _time, new IdempotencyExecutorOptions { OnFinalizationFailure = failures.Add }, logger);

        Result<string> result = await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("mine")), cancellationToken: TestContext.Current.CancellationToken);

        result.Value.Should().Be("mine");
        failures.Should().ContainSingle().Which.Action.Should().Be("Complete");
        failures[0].Exception.Message.Should().Be("store down");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == 9201);
    }

    [Fact]
    public async Task ObserverThatThrows_NeverReplacesTheResult()
    {
        var store = new ForcedTransitionStore(new InMemoryIdempotencyStore(_time)) { CompleteThrows = new InvalidOperationException("store down") };
        var executor = new IdempotentExecutor(store, _time, new IdempotencyExecutorOptions { OnFinalizationFailure = _ => throw new InvalidOperationException("observer") });

        Result<string> result = await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("mine")), cancellationToken: TestContext.Current.CancellationToken);

        result.Value.Should().Be("mine");
    }

    [Fact]
    public async Task FinalizationTimeout_IsAppliedIndependentlyOfTheCallerToken()
    {
        var failures = new List<IdempotencyFinalizationFailure>();
        var store = new ForcedTransitionStore(new InMemoryIdempotencyStore(_time)) { BlockComplete = true };
        var options = new IdempotencyExecutorOptions { FinalizationTimeout = TimeSpan.FromSeconds(5), OnFinalizationFailure = failures.Add };
        var executor = new IdempotentExecutor(store, _time, options);

        Task<Result<string>> call = executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("mine")), cancellationToken: TestContext.Current.CancellationToken).AsTask();
        while (!call.IsCompleted)
        {
            _time.Advance(TimeSpan.FromSeconds(6));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        (await call).Value.Should().Be("mine");
        failures.Should().ContainSingle().Which.Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task AbandonFailure_AfterAnException_StillRethrowsTheOriginalException()
    {
        var failures = new List<IdempotencyFinalizationFailure>();
        var store = new ForcedTransitionStore(new InMemoryIdempotencyStore(_time)) { AbandonThrows = new InvalidOperationException("store down") };
        var executor = new IdempotentExecutor(store, _time, new IdempotencyExecutorOptions { OnFinalizationFailure = failures.Add });

        Func<Task> action = async () => await executor.ExecuteAsync<string>(s_key, _ => throw new ArgumentException("original"), cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("original*");
        failures.Should().ContainSingle().Which.Action.Should().Be("Abandon");
    }

    [Fact]
    public void Constructor_And_Options_AreValidated()
    {
        var store = new InMemoryIdempotencyStore(_time);

        Action noStore = () => _ = new IdempotentExecutor(null!, _time);
        Action noTime = () => _ = new IdempotentExecutor(store, null!);
        Action badLease = () => _ = new IdempotentExecutor(store, _time, new IdempotencyExecutorOptions { ExecutionLease = TimeSpan.Zero });
        Action badTtl = () => _ = new IdempotentExecutor(store, _time, new IdempotencyExecutorOptions { ReplayTimeToLive = TimeSpan.Zero });
        Action badTimeout = () => _ = new IdempotentExecutor(store, _time, new IdempotencyExecutorOptions { FinalizationTimeout = TimeSpan.Zero });

        noStore.Should().Throw<ArgumentNullException>();
        noTime.Should().Throw<ArgumentNullException>();
        badLease.Should().Throw<ArgumentOutOfRangeException>();
        badTtl.Should().Throw<ArgumentOutOfRangeException>();
        badTimeout.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExecuteAsync_ValidatesOperationAndPerCallOptions()
    {
        (IdempotentExecutor executor, _) = Create(IdempotencyCompletionPolicy.SuccessOnly);

        Func<Task> noOperation = async () => await executor.ExecuteAsync<string>(s_key, null!, cancellationToken: TestContext.Current.CancellationToken);
        Func<Task> badLease = async () => await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("v")), new IdempotencyCallOptions { ExecutionLease = TimeSpan.Zero }, TestContext.Current.CancellationToken);
        Func<Task> badTtl = async () => await executor.ExecuteAsync(s_key, _ => ValueTask.FromResult(Result.Success("v")), new IdempotencyCallOptions { ReplayTimeToLive = TimeSpan.FromSeconds(-1) }, TestContext.Current.CancellationToken);

        await noOperation.Should().ThrowAsync<ArgumentNullException>();
        await badLease.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await badTtl.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private static ValueTask<Result<string>> Run(ref int runs, Result<string> result)
    {
        runs++;
        return ValueTask.FromResult(result);
    }

    private (IdempotentExecutor Executor, InMemoryIdempotencyStore Store) Create(IdempotencyCompletionPolicy policy) =>
        Create(new IdempotencyExecutorOptions { CompletionPolicy = policy });

    private (IdempotentExecutor Executor, InMemoryIdempotencyStore Store) Create(IdempotencyExecutorOptions options)
    {
        var store = new InMemoryIdempotencyStore(_time);
        return (new IdempotentExecutor(store, _time, options), store);
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

    private sealed class ListLogger : ILogger<IdempotentExecutor>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
    }

    private sealed class ForcedTransitionStore : IIdempotencyStore
    {
        private readonly InMemoryIdempotencyStore _inner;

        public ForcedTransitionStore(InMemoryIdempotencyStore inner) => _inner = inner;

        public IdempotencyTransition? Complete { get; init; }

        public Exception? CompleteThrows { get; init; }

        public Exception? AbandonThrows { get; init; }

        public bool BlockComplete { get; init; }

        public bool IsEphemeral => true;

        public ValueTask<IdempotencyBeginResult<TValue>> TryBeginAsync<TValue>(IdempotencyKey key, TimeSpan executionLease, string? requestFingerprint, CancellationToken cancellationToken = default) =>
            _inner.TryBeginAsync<TValue>(key, executionLease, requestFingerprint, cancellationToken);

        public async ValueTask<IdempotencyTransition> CompleteAsync<TValue>(IdempotencyExecution execution, Result<TValue> result, TimeSpan replayTimeToLive, CancellationToken cancellationToken = default)
        {
            if (BlockComplete)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (CompleteThrows is not null)
            {
                throw CompleteThrows;
            }

            return Complete ?? await _inner.CompleteAsync(execution, result, replayTimeToLive, cancellationToken);
        }

        public ValueTask<IdempotencyTransition> RenewAsync(IdempotencyExecution execution, TimeSpan extension, CancellationToken cancellationToken = default) =>
            _inner.RenewAsync(execution, extension, cancellationToken);

        public ValueTask<IdempotencyTransition> AbandonAsync(IdempotencyExecution execution, CancellationToken cancellationToken = default) =>
            AbandonThrows is not null ? throw AbandonThrows : _inner.AbandonAsync(execution, cancellationToken);
    }
}
