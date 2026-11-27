using Atya.Foundation.Guards;

namespace Atya.Application.Idempotency;

/// <summary>
/// An opaque token that identifies one execution of a key. It is compared by ordinal equality only.
/// </summary>
public readonly record struct IdempotencyFence
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyFence"/> struct.
    /// </summary>
    /// <param name="value">The opaque token value.</param>
    public IdempotencyFence(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the opaque token value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// The ownership a caller holds after it has begun a key: the key, the fence that proves ownership and the lease expiry.
/// </summary>
public sealed record class IdempotencyExecution
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyExecution"/> class.
    /// </summary>
    /// <param name="key">The key that was begun.</param>
    /// <param name="fence">The fence that proves ownership of the key.</param>
    /// <param name="leaseExpiresAtUtc">The UTC instant at which the execution lease expires.</param>
    public IdempotencyExecution(IdempotencyKey key, IdempotencyFence fence, DateTimeOffset leaseExpiresAtUtc)
    {
        Key = key;
        Guard.AgainstNullOrWhiteSpace(fence.Value);
        Fence = fence;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    /// <summary>
    /// Gets the key that was begun.
    /// </summary>
    public IdempotencyKey Key { get; }

    /// <summary>
    /// Gets the fence that proves ownership of the key.
    /// </summary>
    public IdempotencyFence Fence { get; }

    /// <summary>
    /// Gets the UTC instant at which the execution lease expires.
    /// </summary>
    public DateTimeOffset LeaseExpiresAtUtc { get; }
}

/// <summary>
/// The outcome of a compare-and-set transition (complete, renew or abandon) against the fence of an execution.
/// </summary>
public enum IdempotencyTransition
{
    /// <summary>The fence matched the current in-progress entry and the transition was applied.</summary>
    Applied = 0,

    /// <summary>The entry exists but belongs to a different execution, or is no longer in progress.</summary>
    Superseded = 1,

    /// <summary>No entry exists for the key.</summary>
    NotFound = 2,
}

/// <summary>
/// Selects which operation outcomes are stored for replay.
/// </summary>
public enum IdempotencyCompletionPolicy
{
    /// <summary>
    /// Only successful results are stored. A failed result releases the key so the caller can retry. This is the default.
    /// </summary>
    SuccessOnly = 0,

    /// <summary>
    /// Both successful and failed results are stored and replayed.
    /// </summary>
    SuccessAndFailure = 1,
}
