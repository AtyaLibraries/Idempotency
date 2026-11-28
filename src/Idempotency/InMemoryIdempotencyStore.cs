using Atya.Foundation.Guards;
using Atya.Foundation.Results;

namespace Atya.Application.Idempotency;

/// <summary>
/// A thread-safe, bounded, single-process idempotency store for tests, samples and simple hosts. Records are lost when the
/// process ends and are not shared between processes.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    /// <summary>
    /// The default maximum number of entries.
    /// </summary>
    public const int DefaultMaxEntries = 10_000;

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly PriorityQueue<(string Key, long Version), DateTimeOffset> _expiries = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _maxEntries;
    private long _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryIdempotencyStore"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider that defines now.</param>
    /// <param name="maxEntries">The maximum number of live entries. Must be positive.</param>
    public InMemoryIdempotencyStore(TimeProvider timeProvider, int maxEntries = DefaultMaxEntries)
    {
        _timeProvider = Guard.AgainstNull(timeProvider);
        _maxEntries = Guard.AgainstZeroOrNegative(maxEntries);
    }

    /// <inheritdoc />
    public bool IsEphemeral => true;

    /// <summary>
    /// Gets the keys of all live entries, after purging expired ones.
    /// </summary>
    public IReadOnlyCollection<IdempotencyKey> Keys
    {
        get
        {
            lock (_sync)
            {
                Purge(_timeProvider.GetUtcNow());
                return _entries.Keys.Select(key => new IdempotencyKey(key)).ToArray();
            }
        }
    }

    /// <summary>
    /// Gets the number of live entries, after purging expired ones.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                Purge(_timeProvider.GetUtcNow());
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<IdempotencyBeginResult<TValue>> TryBeginAsync<TValue>(
        IdempotencyKey key,
        TimeSpan executionLease,
        string? requestFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePositive(executionLease, nameof(executionLease));

        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            Purge(now);

            if (_entries.TryGetValue(key.Value, out Entry? entry))
            {
                return ValueTask.FromResult(entry.ToBeginResult<TValue>(key, requestFingerprint));
            }

            if (_entries.Count >= _maxEntries)
            {
                return ValueTask.FromResult(IdempotencyBeginResult.CapacityExceeded<TValue>());
            }

            DateTimeOffset expiresAt = now + executionLease;
            var fence = new IdempotencyFence(Guid.NewGuid().ToString("N"));
            Store(key.Value, Entry.Begin(fence, requestFingerprint, expiresAt, NextVersion()));

            return ValueTask.FromResult(IdempotencyBeginResult.Begun<TValue>(new IdempotencyExecution(key, fence, expiresAt)));
        }
    }

    /// <inheritdoc />
    public ValueTask<IdempotencyTransition> CompleteAsync<TValue>(
        IdempotencyExecution execution,
        Result<TValue> result,
        TimeSpan replayTimeToLive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guard.AgainstNull(execution);
        Guard.AgainstNull(result);
        ValidatePositive(replayTimeToLive, nameof(replayTimeToLive));

        lock (_sync)
        {
            // The rule is fence equality, not clock comparison: check ownership before purging so that a late
            // completion by an owner that nobody superseded is accepted.
            IdempotencyTransition transition = Check(execution, out Entry? entry);
            if (transition != IdempotencyTransition.Applied)
            {
                Purge(_timeProvider.GetUtcNow());
                return ValueTask.FromResult(transition);
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            Store(execution.Key.Value, entry!.Complete(typeof(TValue), result, now, now + replayTimeToLive, NextVersion()));
            Purge(now);
            return ValueTask.FromResult(IdempotencyTransition.Applied);
        }
    }

    /// <inheritdoc />
    public ValueTask<IdempotencyTransition> RenewAsync(
        IdempotencyExecution execution,
        TimeSpan extension,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guard.AgainstNull(execution);
        ValidatePositive(extension, nameof(extension));

        lock (_sync)
        {
            IdempotencyTransition transition = Check(execution, out Entry? entry);
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (transition == IdempotencyTransition.Applied)
            {
                Store(execution.Key.Value, entry!.Renew(now + extension, NextVersion()));
            }

            Purge(now);
            return ValueTask.FromResult(transition);
        }
    }

    /// <inheritdoc />
    public ValueTask<IdempotencyTransition> AbandonAsync(
        IdempotencyExecution execution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guard.AgainstNull(execution);

        lock (_sync)
        {
            IdempotencyTransition transition = Check(execution, out _);
            if (transition == IdempotencyTransition.Applied)
            {
                _entries.Remove(execution.Key.Value);
            }

            Purge(_timeProvider.GetUtcNow());
            return ValueTask.FromResult(transition);
        }
    }

    private static void ValidatePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be greater than zero.");
        }
    }

    private long NextVersion() => ++_version;

    private IdempotencyTransition Check(IdempotencyExecution execution, out Entry? entry)
    {
        if (!_entries.TryGetValue(execution.Key.Value, out entry))
        {
            return IdempotencyTransition.NotFound;
        }

        return entry.IsInProgress && string.Equals(entry.Fence.Value, execution.Fence.Value, StringComparison.Ordinal)
            ? IdempotencyTransition.Applied
            : IdempotencyTransition.Superseded;
    }

    private void Store(string key, Entry entry)
    {
        _entries[key] = entry;
        _expiries.Enqueue((key, entry.Version), entry.ExpiresAtUtc);

        // Superseded queue items stay until their time; compact when they outnumber live entries.
        if (_expiries.Count > (_entries.Count * 4) + 1024)
        {
            RebuildQueue();
        }
    }

    private void RebuildQueue()
    {
        _expiries.Clear();
        foreach (KeyValuePair<string, Entry> pair in _entries)
        {
            _expiries.Enqueue((pair.Key, pair.Value.Version), pair.Value.ExpiresAtUtc);
        }
    }

    private void Purge(DateTimeOffset now)
    {
        while (_expiries.TryPeek(out (string Key, long Version) item, out DateTimeOffset expiresAt) && expiresAt <= now)
        {
            _expiries.Dequeue();
            if (_entries.TryGetValue(item.Key, out Entry? entry) && entry.Version == item.Version)
            {
                _entries.Remove(item.Key);
            }
        }
    }

    private sealed class Entry
    {
        private Entry(
            IdempotencyFence fence,
            string? fingerprint,
            DateTimeOffset expiresAtUtc,
            long version,
            Type? resultType,
            object? result,
            DateTimeOffset? completedAtUtc)
        {
            Fence = fence;
            Fingerprint = fingerprint;
            ExpiresAtUtc = expiresAtUtc;
            Version = version;
            ResultType = resultType;
            Result = result;
            CompletedAtUtc = completedAtUtc;
        }

        public IdempotencyFence Fence { get; }

        public DateTimeOffset ExpiresAtUtc { get; }

        public long Version { get; }

        public bool IsInProgress => Result is null;

        private string? Fingerprint { get; }

        private Type? ResultType { get; }

        private object? Result { get; }

        private DateTimeOffset? CompletedAtUtc { get; }

        public static Entry Begin(IdempotencyFence fence, string? fingerprint, DateTimeOffset expiresAtUtc, long version) =>
            new(fence, fingerprint, expiresAtUtc, version, resultType: null, result: null, completedAtUtc: null);

        public Entry Complete<TValue>(Type valueType, Result<TValue> result, DateTimeOffset completedAtUtc, DateTimeOffset expiresAtUtc, long version) =>
            new(Fence, Fingerprint, expiresAtUtc, version, valueType, result, completedAtUtc);

        public Entry Renew(DateTimeOffset expiresAtUtc, long version) =>
            new(Fence, Fingerprint, expiresAtUtc, version, ResultType, Result, CompletedAtUtc);

        public IdempotencyBeginResult<TValue> ToBeginResult<TValue>(IdempotencyKey key, string? requestFingerprint)
        {
            if (Fingerprint is not null
                && requestFingerprint is not null
                && !string.Equals(Fingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                return IdempotencyBeginResult.PayloadMismatch<TValue>();
            }

            if (Result is null)
            {
                return IdempotencyBeginResult.InProgress<TValue>();
            }

            if (Result is not Result<TValue> typedResult)
            {
                return IdempotencyBeginResult.PayloadMismatch<TValue>();
            }

            var replay = new IdempotencyReplay<TValue>(key, typedResult, CompletedAtUtc!.Value, ExpiresAtUtc);
            return IdempotencyBeginResult.Completed(replay);
        }
    }
}
