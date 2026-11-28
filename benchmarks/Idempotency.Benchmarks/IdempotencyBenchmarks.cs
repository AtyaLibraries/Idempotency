using Atya.Foundation.Results;
using BenchmarkDotNet.Attributes;

namespace Atya.Application.Idempotency.Benchmarks;

/// <summary>
/// Benchmarks hot paths for idempotency primitives.
/// </summary>
[MemoryDiagnoser]
public class IdempotencyBenchmarks
{
    private static readonly IdempotencyKey s_replayKey = new("orders:create:replay");
    private IdempotentExecutor _executor = null!;
    private int _nextKey;

    /// <summary>
    /// Initializes benchmark fixtures.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [GlobalSetup]
    public async Task Setup()
    {
        _executor = new IdempotentExecutor(new InMemoryIdempotencyStore(TimeProvider.System, int.MaxValue), TimeProvider.System);
        await _executor.ExecuteAsync(s_replayKey, _ => ValueTask.FromResult(Result.Success("order-42")));
    }

    /// <summary>
    /// Measures successful key creation from caller input.
    /// </summary>
    /// <returns>The created key result.</returns>
    [Benchmark(Baseline = true)]
    public static Result<IdempotencyKey> CreateKey() => IdempotencyKey.Create("orders:create:42");

    /// <summary>
    /// Measures replaying a non-expired stored result.
    /// </summary>
    /// <returns>The replayed operation result.</returns>
    [Benchmark]
    public ValueTask<Result<string>> ReplayStoredResult() =>
        _executor.ExecuteAsync(s_replayKey, _ => ValueTask.FromResult(Result.Success("not-run")));

    /// <summary>
    /// Measures executing a new operation through the in-memory store.
    /// </summary>
    /// <returns>The operation result.</returns>
    [Benchmark]
    public ValueTask<Result<string>> ExecuteNewOperation() =>
        _executor.ExecuteAsync(
            new IdempotencyKey($"orders:create:{Interlocked.Increment(ref _nextKey)}"),
            _ => ValueTask.FromResult(Result.Success("order-42")));
}
