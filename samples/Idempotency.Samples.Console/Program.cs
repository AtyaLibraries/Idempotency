using Atya.Application.Idempotency;
using Atya.Foundation.Results;
using Atya.Foundation.Time;

namespace Atya.Application.Idempotency.Samples.ConsoleApp;

/// <summary>
/// Runs the sample console application.
/// </summary>
public static class Program
{
    /// <summary>
    /// Demonstrates key validation, replay metadata, and expiration-aware replay.
    /// </summary>
    public static void Main()
    {
        Result<IdempotencyKey> keyResult = IdempotencyKey.Create("orders:create:42");
        if (keyResult.IsFailure)
        {
            Console.WriteLine(keyResult.Error.Message);
            return;
        }

        var clock = new SystemClock();
        IdempotencyLease lease = IdempotencyLease.From(clock, TimeSpan.FromMinutes(10));
        Result<string> created = Result.Success("order-42");

        var replay = new IdempotencyReplay<string>(keyResult.Value, created, clock.UtcNowOffset, lease);
        Result<string> replayed = replay.Replay(clock);

        Console.WriteLine(replayed.Match(value => $"Replayed result: {value}", error => error.Message));
    }
}
