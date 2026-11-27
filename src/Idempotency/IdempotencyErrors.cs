using Atya.Foundation.Results;

namespace Atya.Application.Idempotency;

/// <summary>
/// Creates standard errors for idempotency decisions.
/// </summary>
public static class IdempotencyErrors
{
    /// <summary>
    /// Gets the stable error code used when an operation with the same key is already running.
    /// </summary>
    public const string InProgressCode = "atya.application.idempotency.in_progress";

    /// <summary>
    /// Gets the stable error code used when a stored idempotency record is no longer valid.
    /// </summary>
    public const string ExpiredCode = "atya.application.idempotency.expired";

    /// <summary>
    /// Gets the stable error code used when a key is reused with a different request fingerprint or result type.
    /// </summary>
    public const string PayloadMismatchCode = "atya.application.idempotency.payload_mismatch";

    /// <summary>
    /// Gets the stable error code used when the store cannot accept another key.
    /// </summary>
    public const string CapacityExceededCode = "atya.application.idempotency.capacity_exceeded";

    /// <summary>
    /// Creates a conflict error for a duplicate operation that is still in progress.
    /// </summary>
    /// <param name="key">The idempotency key that already has an active operation.</param>
    /// <returns>The conflict error.</returns>
    public static Error InProgress(IdempotencyKey key) =>
        new(InProgressCode, $"An operation with idempotency key '{key}' is already in progress.", ErrorKind.Conflict);

    /// <summary>
    /// Creates a conflict error for an idempotency record that has expired.
    /// </summary>
    /// <param name="key">The expired idempotency key.</param>
    /// <returns>The conflict error.</returns>
    public static Error Expired(IdempotencyKey key) =>
        new(ExpiredCode, $"The idempotency record for key '{key}' has expired.", ErrorKind.Conflict);

    /// <summary>
    /// Creates a failed result for a duplicate operation that is still in progress.
    /// </summary>
    /// <typeparam name="TValue">The expected success value type.</typeparam>
    /// <param name="key">The idempotency key that already has an active operation.</param>
    /// <returns>A failed result containing <see cref="InProgressCode"/>.</returns>
    public static Result<TValue> InProgressResult<TValue>(IdempotencyKey key) =>
        Result.Failure<TValue>(InProgress(key));

    /// <summary>
    /// Creates a failed result for an idempotency record that has expired.
    /// </summary>
    /// <typeparam name="TValue">The expected success value type.</typeparam>
    /// <param name="key">The expired idempotency key.</param>
    /// <returns>A failed result containing <see cref="ExpiredCode"/>.</returns>
    public static Result<TValue> ExpiredResult<TValue>(IdempotencyKey key) =>
        Result.Failure<TValue>(Expired(key));

    /// <summary>
    /// Creates the error for a key that was reused with a different request fingerprint or result type.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <returns>The conflict error.</returns>
    public static Error PayloadMismatch(IdempotencyKey key) =>
        new(PayloadMismatchCode, $"The idempotency key '{key}' was already used with a different request.", ErrorKind.Conflict);

    /// <summary>
    /// Creates the error for a store that cannot accept another key.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <returns>The unavailable error.</returns>
    public static Error CapacityExceeded(IdempotencyKey key) =>
        new(CapacityExceededCode, $"The idempotency store is full; key '{key}' was not accepted.", ErrorKind.Unavailable);

    /// <summary>
    /// Creates a failed result for a payload mismatch.
    /// </summary>
    /// <typeparam name="TValue">The result value type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <returns>The failed result.</returns>
    public static Result<TValue> PayloadMismatchResult<TValue>(IdempotencyKey key) =>
        Result.Failure<TValue>(PayloadMismatch(key));

    /// <summary>
    /// Creates a failed result for a full store.
    /// </summary>
    /// <typeparam name="TValue">The result value type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <returns>The failed result.</returns>
    public static Result<TValue> CapacityExceededResult<TValue>(IdempotencyKey key) =>
        Result.Failure<TValue>(CapacityExceeded(key));
}
