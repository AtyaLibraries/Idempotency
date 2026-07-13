namespace Atya.Application.Idempotency;

/// <summary>
/// Describes the result of attempting to begin an idempotent operation.
/// </summary>
public enum IdempotencyBeginStatus
{
    /// <summary>
    /// The caller acquired the key and should execute the operation.
    /// </summary>
    Begun = 0,

    /// <summary>
    /// Another caller is currently executing the operation for the same key.
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// A completed result is available for replay.
    /// </summary>
    Completed = 2,
}
