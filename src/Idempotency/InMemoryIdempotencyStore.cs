using Atya.Foundation.Guards;
using Atya.Foundation.Results;
using Atya.Foundation.Time;

namespace Atya.Application.Idempotency;

/// <summary>
/// Stores idempotency records in memory with thread-safe atomic begin decisions.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets a snapshot of stored idempotency keys.
    /// </summary>
    public IReadOnlyCollection<IdempotencyKey> Keys
    {
        get
        {
            lock (_sync)
            {
                return _entries.Keys.Select(key => new IdempotencyKey(key)).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<IdempotencyBeginResult<TValue>> TryBeginAsync<TValue>(
        IdempotencyKey key,
        IdempotencyLease lease,
        IClock clock,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guard.AgainstNull(clock);

        lock (_sync)
        {
            if (_entries.TryGetValue(key.Value, out Entry? entry))
            {
                if (!entry.Lease.IsExpired(clock))
                {
                    return ValueTask.FromResult(entry.ToBeginResult<TValue>(key));
                }

                _entries.Remove(key.Value);
            }

            _entries.Add(key.Value, Entry.Begin(lease));
            return ValueTask.FromResult(IdempotencyBeginResult.Begun<TValue>());
        }
    }

    /// <inheritdoc />
    public ValueTask CompleteAsync<TValue>(
        IdempotencyKey key,
        Result<TValue> result,
        IClock clock,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guard.AgainstNull(result);
        Guard.AgainstNull(clock);

        lock (_sync)
        {
            if (!_entries.TryGetValue(key.Value, out Entry? entry))
            {
                throw new KeyNotFoundException($"Idempotency key '{key}' has not begun.");
            }

            _entries[key.Value] = entry.Complete(result, clock.UtcNowOffset);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class Entry
    {
        private Entry(IdempotencyLease lease, object? result, DateTimeOffset? completedAtUtc)
        {
            Lease = lease;
            Result = result;
            CompletedAtUtc = completedAtUtc;
        }

        public IdempotencyLease Lease { get; }

        private object? Result { get; }

        private DateTimeOffset? CompletedAtUtc { get; }

        public static Entry Begin(IdempotencyLease lease) => new(lease, result: null, completedAtUtc: null);

        public Entry Complete<TValue>(Result<TValue> result, DateTimeOffset completedAtUtc) =>
            new(Lease, result, completedAtUtc);

        public IdempotencyBeginResult<TValue> ToBeginResult<TValue>(IdempotencyKey key)
        {
            if (Result is null)
            {
                return IdempotencyBeginResult.InProgress<TValue>();
            }

            if (Result is not Result<TValue> typedResult)
            {
                throw new InvalidOperationException($"Idempotency key '{key}' was completed with a different result type.");
            }

            var replay = new IdempotencyReplay<TValue>(key, typedResult, CompletedAtUtc!.Value, Lease);
            return IdempotencyBeginResult.Completed(replay);
        }
    }
}
