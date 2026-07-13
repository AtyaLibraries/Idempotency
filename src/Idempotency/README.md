# Atya.Application.Idempotency

Idempotency primitives for safely handling repeated application operations.

## Installation

```bash
dotnet add package Atya.Application.Idempotency
```

## Quick Start

```csharp
using Atya.Application.Idempotency;
using Atya.Foundation.Results;
using Atya.Foundation.Time;

Result<IdempotencyKey> key = IdempotencyKey.Create("orders:create:42");
if (key.IsFailure)
{
    return key.Error.Message;
}

IClock clock = new SystemClock();
var store = new InMemoryIdempotencyStore();
var executor = new IdempotentExecutor(store, clock);

Result<string> replayed = await executor.ExecuteAsync(
    key.Value,
    TimeSpan.FromMinutes(10),
    _ => ValueTask.FromResult(Result.Success("order-42")),
    cancellationToken);

return replayed.Match(value => value, error => error.Message);
```

## Feature Tour

- `IdempotencyKey` validates caller-provided keys and offers a `Result`-returning `Create` method for request input.
- `IdempotencyLease` computes and evaluates UTC replay windows with `Atya.Foundation.Time.IClock`.
- `IIdempotencyStore` atomically begins keys and stores completed results.
- `InMemoryIdempotencyStore` provides a thread-safe proving implementation for tests, samples, and simple hosts.
- `IdempotentExecutor` runs the begin -> execute -> complete -> replay flow for `Result<T>` operations.
- `IdempotencyReplay<T>` replays a stored `Result<T>` until its lease expires.
- `IdempotencyErrors` exposes stable error codes for duplicate in-progress and expired idempotency states.

## Error Codes

| Code | Kind | When |
|---|---|---|
| `atya.application.idempotency.key_required` | Validation | Caller input did not contain a usable idempotency key. |
| `atya.application.idempotency.in_progress` | Conflict | A matching key already has an operation in progress. |
| `atya.application.idempotency.expired` | Conflict | A matching idempotency record exists but is no longer valid for replay. |

## Why These Dependencies

| Dependency | Purpose |
|---|---|
| `Atya.Foundation.Guards` | Programmer-error guards for public constructors and methods. |
| `Atya.Foundation.Results` | Expected validation, duplicate, and expired outcomes. |
| `Atya.Foundation.Time` | UTC-safe expiration checks through `IClock` and `TimeGuard`. |

## Links

- Repository: <https://github.com/AtyaLibraries/Idempotency>
- NuGet: <https://www.nuget.org/packages/Atya.Application.Idempotency>
- Samples: <https://github.com/AtyaLibraries/Idempotency/tree/development/samples>
- License: <https://github.com/AtyaLibraries/Idempotency/blob/development/LICENSE>
