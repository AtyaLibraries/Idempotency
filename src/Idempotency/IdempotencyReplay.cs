using Atya.Foundation.Guards;
using Atya.Foundation.Results;
using Atya.Foundation.Time;

namespace Atya.Application.Idempotency;

/// <summary>
/// Represents a stored operation result that can be replayed for a matching idempotency key.
/// </summary>
/// <typeparam name="TValue">The operation success value type.</typeparam>
public sealed record class IdempotencyReplay<TValue>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyReplay{TValue}"/> class.
    /// </summary>
    /// <param name="key">The idempotency key associated with the stored result.</param>
    /// <param name="result">The stored operation result.</param>
    /// <param name="completedAtUtc">The UTC instant when the operation completed.</param>
    /// <param name="lease">The replay validity window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="completedAtUtc"/> does not use the UTC offset.</exception>
    public IdempotencyReplay(
        IdempotencyKey key,
        Result<TValue> result,
        DateTimeOffset completedAtUtc,
        IdempotencyLease lease)
    {
        Key = key;
        Result = Guard.AgainstNull(result);
        CompletedAtUtc = TimeGuard.AgainstNonUtc(completedAtUtc);
        Lease = lease;
    }

    /// <summary>
    /// Gets the idempotency key associated with the stored result.
    /// </summary>
    public IdempotencyKey Key { get; }

    /// <summary>
    /// Gets the stored operation result.
    /// </summary>
    public Result<TValue> Result { get; }

    /// <summary>
    /// Gets the UTC instant when the operation completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Gets the replay validity window.
    /// </summary>
    public IdempotencyLease Lease { get; }

    /// <summary>
    /// Replays the stored result when it has not expired.
    /// </summary>
    /// <param name="clock">The clock used to evaluate expiration.</param>
    /// <returns>The stored result, or an expired idempotency failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    public Result<TValue> Replay(IClock clock) =>
        Lease.IsExpired(clock) ? IdempotencyErrors.ExpiredResult<TValue>(Key) : Result;
}
