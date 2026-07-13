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
}
