using Atya.Foundation.Guards;

namespace Atya.Application.Idempotency;

/// <summary>
/// Represents the outcome of beginning an idempotent operation.
/// </summary>
/// <typeparam name="TValue">The operation result value type.</typeparam>
public sealed record class IdempotencyBeginResult<TValue>
{
    internal IdempotencyBeginResult(
        IdempotencyBeginStatus status,
        IdempotencyReplay<TValue>? replay,
        IdempotencyExecution? execution)
    {
        Status = status;
        Replay = replay;
        Execution = execution;
    }

    /// <summary>
    /// Gets the begin status.
    /// </summary>
    public IdempotencyBeginStatus Status { get; }

    /// <summary>
    /// Gets the replay available when <see cref="Status"/> is <see cref="IdempotencyBeginStatus.Completed"/>.
    /// </summary>
    public IdempotencyReplay<TValue>? Replay { get; }

    /// <summary>
    /// Gets the execution ownership when <see cref="Status"/> is <see cref="IdempotencyBeginStatus.Begun"/>.
    /// </summary>
    public IdempotencyExecution? Execution { get; }

    /// <summary>
    /// Gets a value indicating whether the caller acquired the key.
    /// </summary>
    public bool IsBegun => Status == IdempotencyBeginStatus.Begun;

    /// <summary>
    /// Gets a value indicating whether another execution is in progress.
    /// </summary>
    public bool IsInProgress => Status == IdempotencyBeginStatus.InProgress;

    /// <summary>
    /// Gets a value indicating whether a completed result is available.
    /// </summary>
    public bool IsCompleted => Status == IdempotencyBeginStatus.Completed;
}

/// <summary>
/// Creates <see cref="IdempotencyBeginResult{TValue}"/> instances.
/// </summary>
public static class IdempotencyBeginResult
{
    /// <summary>
    /// Creates a result indicating that the caller acquired the key.
    /// </summary>
    /// <typeparam name="TValue">The operation result value type.</typeparam>
    /// <param name="execution">The ownership the caller holds.</param>
    /// <returns>The begin result.</returns>
    public static IdempotencyBeginResult<TValue> Begun<TValue>(IdempotencyExecution execution) =>
        new(IdempotencyBeginStatus.Begun, replay: null, Guard.AgainstNull(execution));

    /// <summary>
    /// Creates a result indicating that another execution is in progress.
    /// </summary>
    /// <typeparam name="TValue">The operation result value type.</typeparam>
    /// <returns>The begin result.</returns>
    public static IdempotencyBeginResult<TValue> InProgress<TValue>() =>
        new(IdempotencyBeginStatus.InProgress, replay: null, execution: null);

    /// <summary>
    /// Creates a result indicating that a completed result is available.
    /// </summary>
    /// <typeparam name="TValue">The operation result value type.</typeparam>
    /// <param name="replay">The stored result to replay.</param>
    /// <returns>The begin result.</returns>
    public static IdempotencyBeginResult<TValue> Completed<TValue>(IdempotencyReplay<TValue> replay) =>
        new(IdempotencyBeginStatus.Completed, Guard.AgainstNull(replay), execution: null);

    /// <summary>
    /// Creates a result indicating that the key exists with a different request fingerprint or result type.
    /// </summary>
    /// <typeparam name="TValue">The operation result value type.</typeparam>
    /// <returns>The begin result.</returns>
    public static IdempotencyBeginResult<TValue> PayloadMismatch<TValue>() =>
        new(IdempotencyBeginStatus.PayloadMismatch, replay: null, execution: null);

    /// <summary>
    /// Creates a result indicating that the store cannot accept a new key.
    /// </summary>
    /// <typeparam name="TValue">The operation result value type.</typeparam>
    /// <returns>The begin result.</returns>
    public static IdempotencyBeginResult<TValue> CapacityExceeded<TValue>() =>
        new(IdempotencyBeginStatus.CapacityExceeded, replay: null, execution: null);
}
