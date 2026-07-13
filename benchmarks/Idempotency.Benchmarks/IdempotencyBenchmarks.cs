using Atya.Foundation.Results;
using Atya.Foundation.Time;
using BenchmarkDotNet.Attributes;

namespace Atya.Application.Idempotency.Benchmarks;

/// <summary>
/// Benchmarks hot paths for idempotency primitives.
/// </summary>
[MemoryDiagnoser]
public class IdempotencyBenchmarks
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
    private IdempotencyReplay<string> _replay = null!;

    /// <summary>
    /// Initializes benchmark fixtures.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _replay = new IdempotencyReplay<string>(
            new IdempotencyKey("orders:create:42"),
            Result.Success("order-42"),
            _clock.UtcNowOffset,
            new IdempotencyLease(_clock.UtcNowOffset.AddMinutes(10)));
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
    public Result<string> ReplayStoredResult() => _replay.Replay(_clock);

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTime UtcNow => utcNow.UtcDateTime;

        public DateTimeOffset UtcNowOffset => utcNow;

        public DateOnly TodayUtc => DateOnly.FromDateTime(utcNow.UtcDateTime);
    }
}
