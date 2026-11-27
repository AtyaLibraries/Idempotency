using Atya.Foundation.Guards;
using Atya.Foundation.Results;

namespace Atya.Application.Idempotency;

/// <summary>
/// Represents a stored result that can be replayed until its replay window ends.
/// </summary>
/// <typeparam name="TValue">The operation result value type.</typeparam>
public sealed record class IdempotencyReplay<TValue>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyReplay{TValue}"/> class.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="result">The stored result.</param>
    /// <param name="completedAtUtc">The UTC instant at which the result was stored.</param>
    /// <param name="expiresAtUtc">The UTC instant at which the replay window ends.</param>
    public IdempotencyReplay(
        IdempotencyKey key,
        Result<TValue> result,
        DateTimeOffset completedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Key = key;
        Result = Guard.AgainstNull(result);
        CompletedAtUtc = completedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>
    /// Gets the idempotency key.
    /// </summary>
    public IdempotencyKey Key { get; }

    /// <summary>
    /// Gets the stored result.
    /// </summary>
    public Result<TValue> Result { get; }

    /// <summary>
    /// Gets the UTC instant at which the result was stored.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Gets the UTC instant at which the replay window ends.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; }
}
