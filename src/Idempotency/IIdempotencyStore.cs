using Atya.Foundation.Results;
using Atya.Foundation.Time;

namespace Atya.Application.Idempotency;

/// <summary>
/// Stores idempotency records and performs atomic begin decisions for operation keys.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to begin an operation for the specified key.
    /// </summary>
    /// <typeparam name="TValue">The operation success value type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="lease">The lease to use when the operation begins.</param>
    /// <param name="clock">The clock used to evaluate existing record expiration.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// A begin decision. Implementations must make this operation atomic for a key: concurrent callers with the same
    /// key must not both receive <see cref="IdempotencyBeginStatus.Begun"/>.
    /// </returns>
    public ValueTask<IdempotencyBeginResult<TValue>> TryBeginAsync<TValue>(
        IdempotencyKey key,
        IdempotencyLease lease,
        IClock clock,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the completed result for an operation that previously began.
    /// </summary>
    /// <typeparam name="TValue">The operation success value type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="result">The operation result to store for replay.</param>
    /// <param name="clock">The clock used to record completion time.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the result has been stored.</returns>
    public ValueTask CompleteAsync<TValue>(
        IdempotencyKey key,
        Result<TValue> result,
        IClock clock,
        CancellationToken cancellationToken = default);
}
