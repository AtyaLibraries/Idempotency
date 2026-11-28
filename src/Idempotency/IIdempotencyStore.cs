using Atya.Foundation.Results;

namespace Atya.Application.Idempotency;

/// <summary>
/// Stores idempotency records. Implementations own their clock and compute absolute expiries themselves.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Gets a value indicating whether records are lost when the process ends. Informational only.
    /// </summary>
    public bool IsEphemeral { get; }

    /// <summary>
    /// Atomically begins a key, or reports why it cannot be begun.
    /// </summary>
    /// <typeparam name="TValue">The result value type of the operation.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="executionLease">How long the caller may execute before its ownership can be taken over.</param>
    /// <param name="requestFingerprint">An optional fingerprint of the request. Null means unchecked.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The begin result.</returns>
    public ValueTask<IdempotencyBeginResult<TValue>> TryBeginAsync<TValue>(
        IdempotencyKey key,
        TimeSpan executionLease,
        string? requestFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes an execution if its fence is still the current one. The replay window starts now, at completion.
    /// </summary>
    /// <typeparam name="TValue">The result value type of the operation.</typeparam>
    /// <param name="execution">The execution being completed.</param>
    /// <param name="result">The result to store.</param>
    /// <param name="replayTimeToLive">How long the stored result is replayed, counted from completion.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The transition outcome.</returns>
    public ValueTask<IdempotencyTransition> CompleteAsync<TValue>(
        IdempotencyExecution execution,
        Result<TValue> result,
        TimeSpan replayTimeToLive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends the lease of an execution if its fence is still the current one.
    /// </summary>
    /// <param name="execution">The execution being renewed.</param>
    /// <param name="extension">The new lease length, counted from now.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The transition outcome.</returns>
    public ValueTask<IdempotencyTransition> RenewAsync(
        IdempotencyExecution execution,
        TimeSpan extension,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases an execution so the key can be begun again, if its fence is still the current one.
    /// </summary>
    /// <param name="execution">The execution being abandoned.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The transition outcome.</returns>
    public ValueTask<IdempotencyTransition> AbandonAsync(
        IdempotencyExecution execution,
        CancellationToken cancellationToken = default);
}
