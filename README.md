<h1 align="center">Atya.Application.Idempotency</h1>

<p align="center"><i>Idempotency primitives for safely handling repeated application operations.</i></p>

<p align="center">
  <a href="https://www.nuget.org/packages/Atya.Application.Idempotency"><img src="https://img.shields.io/nuget/v/Atya.Application.Idempotency?style=for-the-badge&logo=nuget&logoColor=white&label=NuGet&color=512BD4" alt="NuGet Version"></a>
  <a href="https://www.nuget.org/packages/Atya.Application.Idempotency"><img src="https://img.shields.io/nuget/dt/Atya.Application.Idempotency?style=for-the-badge&logo=nuget&logoColor=white&label=Downloads&color=512BD4" alt="NuGet Downloads"></a>
  <img src="https://img.shields.io/badge/.NET_10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt="Target Framework">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/AtyaLibraries/Idempotency?style=for-the-badge&color=512BD4" alt="License"></a>
  <a href="https://github.com/AtyaLibraries/Idempotency/actions"><img src="https://img.shields.io/github/actions/workflow/status/AtyaLibraries/Idempotency/ci.yml?branch=development&style=for-the-badge&logo=githubactions&logoColor=white&label=Build" alt="Build"></a>
</p>

---

## Overview

`Atya.Application.Idempotency` provides storage-neutral primitives and an execution path for application operations that may be retried by clients, queues, schedulers, or workflow engines. It validates caller-provided idempotency keys, atomically begins operations through a store, records replay metadata, evaluates UTC expiration windows through `Atya.Foundation.Time`, and returns standard `Atya.Foundation.Results` failures for duplicate or expired operations.

The package does not prescribe persistence, locking, HTTP middleware, or a workflow framework. Applications keep those choices and use these primitives at their command or request boundary.

## Features

- `IdempotencyKey` for validated, stable operation keys.
- `IdempotencyLease` for UTC expiration windows based on an injected `IClock`.
- `IIdempotencyStore` for atomic begin/complete state transitions.
- `InMemoryIdempotencyStore` for tests, samples, and simple hosts.
- `IdempotentExecutor` for the begin -> execute -> complete -> replay flow.
- `IdempotencyReplay<T>` for replaying a stored `Result<T>` until its lease expires.
- `IdempotencyErrors` for stable conflict error codes.

## Installation

```bash
dotnet add package Atya.Application.Idempotency
```

```powershell
Install-Package Atya.Application.Idempotency
```

```xml
<PackageReference Include="Atya.Application.Idempotency" Version="<latest-stable>" />
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

### Validate caller keys

Use `IdempotencyKey.Create` when the key comes from a request header, message property, or other caller input. Empty input returns a validation `Result` failure instead of throwing.

```csharp
Result<IdempotencyKey> key = IdempotencyKey.Create(headerValue);
```

Use the constructor when invalid input is a programmer error inside your own code.

```csharp
var key = new IdempotencyKey("payments:capture:abc123");
```

### Compute UTC replay windows

`IdempotencyLease` uses `IClock` so tests and workers do not depend on wall-clock time.

```csharp
IdempotencyLease lease = IdempotencyLease.From(clock, TimeSpan.FromMinutes(15));
bool expired = lease.IsExpired(clock);
```

### Begin atomically

`IIdempotencyStore.TryBeginAsync` is the synchronization point. Store implementations must ensure two concurrent callers with the same key cannot both receive `Begun`.

```csharp
IdempotencyBeginResult<OrderReceipt> begin = await store.TryBeginAsync<OrderReceipt>(
    key,
    lease,
    clock,
    cancellationToken);
```

`InMemoryIdempotencyStore` provides a thread-safe implementation for tests, samples, and simple single-process hosts. Durable stores such as EF Core or Redis are intentionally outside this package.

### Replay stored results

Store your own operation result and replay it for matching keys until the lease expires.

```csharp
var replay = new IdempotencyReplay<OrderReceipt>(key, result, completedAtUtc, lease);
Result<OrderReceipt> response = replay.Replay(clock);
```

### Execute through the built-in flow

Use `IdempotentExecutor` when the operation body already returns `Result<T>`.

```csharp
Result<OrderReceipt> result = await executor.ExecuteAsync(
    key,
    TimeSpan.FromMinutes(10),
    ProcessOrderAsync,
    cancellationToken);
```

The executor flow is:

1. `TryBeginAsync`
2. `Completed` -> replay the stored result
3. `InProgress` -> return `atya.application.idempotency.in_progress`
4. `Begun` -> run the operation, call `CompleteAsync`, return the operation result

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

## Compatibility

Targets `net10.0`.

## Testing

```bash
dotnet test
```

## Benchmarks

```bash
dotnet run -c Release --project benchmarks/Idempotency.Benchmarks/Idempotency.Benchmarks.csproj
```

## Links

- Repository: <https://github.com/AtyaLibraries/Idempotency>
- NuGet: <https://www.nuget.org/packages/Atya.Application.Idempotency>
- Samples: <https://github.com/AtyaLibraries/Idempotency/tree/development/samples>
- License: <https://github.com/AtyaLibraries/Idempotency/blob/development/LICENSE>

## License

Released under the MIT license. See [LICENSE](LICENSE) for details.
