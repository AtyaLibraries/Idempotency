using Atya.Foundation.Guards;
using Atya.Foundation.Results;
using Atya.Foundation.Time;

namespace Atya.Application.Idempotency;

/// <summary>
/// Executes result-returning operations through an idempotency store.
/// </summary>
public sealed class IdempotentExecutor
{
    private readonly IIdempotencyStore _store;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotentExecutor"/> class.
    /// </summary>
    /// <param name="store">The idempotency store.</param>
    /// <param name="clock">The clock used for lease and replay decisions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="clock"/> is null.</exception>
    public IdempotentExecutor(IIdempotencyStore store, IClock clock)
    {
        _store = Guard.AgainstNull(store);
        _clock = Guard.AgainstNull(clock);
    }

    /// <summary>
    /// Executes the operation only when the idempotency key can begin, otherwise returning an in-progress failure or
    /// replaying a completed result.
    /// </summary>
    /// <typeparam name="TValue">The operation success value type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="timeToLive">The positive replay time-to-live.</param>
    /// <param name="operation">The operation body to execute after the key begins.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The executed result, replayed result, or an in-progress conflict failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeToLive"/> is zero or negative.</exception>
    public async ValueTask<Result<TValue>> ExecuteAsync<TValue>(
        IdempotencyKey key,
        TimeSpan timeToLive,
        Func<CancellationToken, ValueTask<Result<TValue>>> operation,
        CancellationToken cancellationToken = default)
    {
        Guard.AgainstNull(operation);

        IdempotencyLease lease = IdempotencyLease.From(_clock, timeToLive);
        IdempotencyBeginResult<TValue> begin = await _store.TryBeginAsync<TValue>(
            key,
            lease,
            _clock,
            cancellationToken).ConfigureAwait(false);

        if (begin.IsCompleted)
        {
            return begin.Replay!.Replay(_clock);
        }

        if (begin.IsInProgress)
        {
            return IdempotencyErrors.InProgressResult<TValue>(key);
        }

        Result<TValue> result = await operation(cancellationToken).ConfigureAwait(false);
        await _store.CompleteAsync(key, result, _clock, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
