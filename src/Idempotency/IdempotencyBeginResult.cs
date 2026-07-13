using Atya.Foundation.Guards;

namespace Atya.Application.Idempotency;

/// <summary>
/// Represents the atomic decision returned when beginning an idempotent operation.
/// </summary>
/// <typeparam name="TValue">The operation success value type.</typeparam>
public sealed record class IdempotencyBeginResult<TValue>
{
    internal IdempotencyBeginResult(IdempotencyBeginStatus status, IdempotencyReplay<TValue>? replay)
    {
        Status = status;
        Replay = replay;
    }

    /// <summary>
    /// Gets the begin decision status.
    /// </summary>
    public IdempotencyBeginStatus Status { get; }

    /// <summary>
    /// Gets the completed replay value when <see cref="Status"/> is <see cref="IdempotencyBeginStatus.Completed"/>.
    /// </summary>
    public IdempotencyReplay<TValue>? Replay { get; }

    /// <summary>
    /// Gets a value indicating whether the caller should execute the operation.
    /// </summary>
    public bool IsBegun => Status == IdempotencyBeginStatus.Begun;

    /// <summary>
    /// Gets a value indicating whether another caller is currently executing the operation.
    /// </summary>
    public bool IsInProgress => Status == IdempotencyBeginStatus.InProgress;

    /// <summary>
    /// Gets a value indicating whether a completed result is available for replay.
    /// </summary>
    public bool IsCompleted => Status == IdempotencyBeginStatus.Completed;

}

/// <summary>
/// Creates idempotency begin decisions.
/// </summary>
public static class IdempotencyBeginResult
{
    /// <summary>
    /// Creates a begin decision for a caller that acquired the key.
    /// </summary>
    /// <typeparam name="TValue">The operation success value type.</typeparam>
    /// <returns>The begun decision.</returns>
    public static IdempotencyBeginResult<TValue> Begun<TValue>() =>
        new(IdempotencyBeginStatus.Begun, replay: null);

    /// <summary>
    /// Creates a begin decision for a key that is already in progress.
    /// </summary>
    /// <typeparam name="TValue">The operation success value type.</typeparam>
    /// <returns>The in-progress decision.</returns>
    public static IdempotencyBeginResult<TValue> InProgress<TValue>() =>
        new(IdempotencyBeginStatus.InProgress, replay: null);

    /// <summary>
    /// Creates a begin decision for a stored completed result.
    /// </summary>
    /// <typeparam name="TValue">The operation success value type.</typeparam>
    /// <param name="replay">The completed replay value.</param>
    /// <returns>The completed decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="replay"/> is null.</exception>
    public static IdempotencyBeginResult<TValue> Completed<TValue>(IdempotencyReplay<TValue> replay) =>
        new(IdempotencyBeginStatus.Completed, Guard.AgainstNull(replay));
}
