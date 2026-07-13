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
    /// Demonstrates key validation, idempotent execution, and result replay.
    /// </summary>
    public static async Task Main()
    {
        Result<IdempotencyKey> keyResult = IdempotencyKey.Create("orders:create:42");
        if (keyResult.IsFailure)
        {
            Console.WriteLine(keyResult.Error.Message);
            return;
        }

        var clock = new SystemClock();
        var store = new InMemoryIdempotencyStore();
        var executor = new IdempotentExecutor(store, clock);

        Result<string> first = await executor.ExecuteAsync(
            keyResult.Value,
            TimeSpan.FromMinutes(10),
            _ => ValueTask.FromResult(Result.Success("order-42")));

        Result<string> replayed = await executor.ExecuteAsync(
            keyResult.Value,
            TimeSpan.FromMinutes(10),
            _ => ValueTask.FromResult(Result.Success("should-not-run")));

        Console.WriteLine(first.Match(value => $"Executed result: {value}", error => error.Message));
        Console.WriteLine(replayed.Match(value => $"Replayed result: {value}", error => error.Message));
    }
}
