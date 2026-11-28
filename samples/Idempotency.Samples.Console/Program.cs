using Atya.Application.Idempotency;
using Atya.Foundation.Results;

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

        var store = new InMemoryIdempotencyStore(TimeProvider.System);
        var executor = new IdempotentExecutor(store, TimeProvider.System);

        Result<string> first = await executor.ExecuteAsync(
            keyResult.Value,
            _ => ValueTask.FromResult(Result.Success("order-42")));

        Result<string> replayed = await executor.ExecuteAsync(
            keyResult.Value,
            _ => ValueTask.FromResult(Result.Success("should-not-run")));

        Console.WriteLine(first.Match(value => $"Executed result: {value}", error => error.Message));
        Console.WriteLine(replayed.Match(value => $"Replayed result: {value}", error => error.Message));
    }
}
