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

Result<IdempotencyKey> key = IdempotencyKey.Create("orders:create:42");
if (key.IsFailure)
{
    return key.Error.Message;
}

var store = new InMemoryIdempotencyStore(TimeProvider.System);
var executor = new IdempotentExecutor(store, TimeProvider.System);

Result<string> result = await executor.ExecuteAsync(
    key.Value,
    _ => ValueTask.FromResult(Result.Success("order-42")),
    cancellationToken: cancellationToken);

return result.Match(value => value, error => error.Message);
```

## Feature Tour

### Validate caller keys

Use `IdempotencyKey.Create` when the key comes from a request header, message property, or other caller input. Empty input returns a validation `Result` failure instead of throwing. Use the constructor when invalid input is a programmer error.

### Fenced leases

`IIdempotencyStore.TryBeginAsync` is the synchronization point: two concurrent callers with the same key cannot both receive `Begun`. A successful begin returns an `IdempotencyExecution` holding an opaque `IdempotencyFence`. `CompleteAsync`, `RenewAsync` and `AbandonAsync` are compare-and-set operations on that fence and return an `IdempotencyTransition` (`Applied`, `Superseded`, `NotFound`), so an expired slow owner can never overwrite a newer owner. Stores own their `TimeProvider` and compute absolute expiries themselves; callers pass `TimeSpan` values.

### Completion policy and abandon-on-exception

| Outcome | `SuccessOnly` (default) | `SuccessAndFailure` |
|---|---|---|
| Success `Result` | stored and replayed | stored and replayed |
| Failure `Result` | abandoned (the caller can retry) | stored and replayed |
| Exception | abandoned, rethrown | abandoned, rethrown |
| Caller cancellation | abandoned, rethrown | abandoned, rethrown |

Handlers with non-compensated partial side effects must opt in to `SuccessAndFailure` (`IdempotencyExecutorOptions.CompletionPolicy`, or per call through `IdempotencyCallOptions`).

### The replay window starts at completion

The replay time-to-live is counted from the moment the result is stored, never from the start of a slow operation.

### Finalization

Completing or abandoning uses its own timeout (`FinalizationTimeout`), not the caller's `CancellationToken`. If finalization fails or times out the lease simply expires; the operation's result is still returned, the failure is logged at Error and passed to `IdempotencyExecutorOptions.OnFinalizationFailure`.

### Request fingerprints

Pass `IdempotencyCallOptions.RequestFingerprint` to detect a key reused with a different request. A mismatch (or a replay requested with a different result type) returns `atya.application.idempotency.payload_mismatch`. A null fingerprint is unchecked.

### Bounded in-memory store

`InMemoryIdempotencyStore` is single-process only, keeps at most `MaxEntries` (default 10,000) entries, purges expired entries in expiry order on every mutating call, and returns `CapacityExceeded` (`atya.application.idempotency.capacity_exceeded`) when full. It is not shared between processes and is lost on restart (`IsEphemeral`). Durable stores are outside this package; `IdempotencyStoreConformanceTests` in the test project is the contract any store must pass.

### Execute through the built-in flow

```csharp
Result<OrderReceipt> result = await executor.ExecuteAsync(
    key,
    ProcessOrderAsync,
    new IdempotencyCallOptions { RequestFingerprint = requestHash },
    cancellationToken);
```

## Migrating from 1.x to 2.0

- `IIdempotencyStore` changed: `TryBeginAsync` takes an execution lease and optional fingerprint, `CompleteAsync` takes the `IdempotencyExecution` and a replay time-to-live, and `RenewAsync`, `AbandonAsync` and `IsEphemeral` were added. Stores no longer receive an `IClock`.
- `IdempotencyLease`, the `IClock` parameters and `IdempotencyReplay<T>.Lease` / `Replay(IClock)` are removed. `IdempotentExecutor` takes a `TimeProvider`, and `ExecuteAsync` takes `IdempotencyCallOptions` instead of a bare time-to-live. `InMemoryIdempotencyStore` takes a `TimeProvider`.
- Behavior: failure `Result`s are no longer replayed by default. Opt in with `SuccessAndFailure` if your handler has partial side effects that are not compensated.
- Behavior: an exception or cancellation releases the key immediately instead of leaving it in progress until the lease expires.
- The `Atya.Foundation.Time` dependency is gone; `Microsoft.Extensions.Logging.Abstractions` was added for the finalization error log.
- Last compatible version: 1.0.0.

## Error Codes

| Code | Kind | When |
|---|---|---|
| `atya.application.idempotency.key_required` | Validation | Caller input did not contain a usable idempotency key. |
| `atya.application.idempotency.in_progress` | Conflict | A matching key already has an operation in progress. |
| `atya.application.idempotency.expired` | Conflict | A matching idempotency record exists but is no longer valid for replay. |
| `atya.application.idempotency.payload_mismatch` | Conflict | The key was reused with a different request fingerprint or result type. |
| `atya.application.idempotency.capacity_exceeded` | Unavailable | The store is full and cannot accept a new key. |

## Why These Dependencies

| Dependency | Purpose |
|---|---|
| `Atya.Foundation.Guards` | Programmer-error guards for public constructors and methods. |
| `Atya.Foundation.Results` | Expected validation, duplicate, and expired outcomes. |
| `Microsoft.Extensions.Logging.Abstractions` | Error log when finalization fails (logging abstractions only, no provider). |

## Links

- Repository: <https://github.com/AtyaLibraries/Idempotency>
- NuGet: <https://www.nuget.org/packages/Atya.Application.Idempotency>
- Samples: <https://github.com/AtyaLibraries/Idempotency/tree/development/samples>
- License: <https://github.com/AtyaLibraries/Idempotency/blob/development/LICENSE>
