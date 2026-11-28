namespace Atya.Application.Idempotency;

/// <summary>
/// Registration-level defaults for <see cref="IdempotentExecutor"/>.
/// </summary>
public sealed record class IdempotencyExecutorOptions
{
    /// <summary>
    /// Gets which outcomes are stored for replay. Defaults to <see cref="IdempotencyCompletionPolicy.SuccessOnly"/>.
    /// </summary>
    public IdempotencyCompletionPolicy CompletionPolicy { get; init; } = IdempotencyCompletionPolicy.SuccessOnly;

    /// <summary>
    /// Gets how long a caller may execute before its ownership can be taken over. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan ExecutionLease { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long a stored result is replayed, counted from completion. Defaults to 24 hours.
    /// </summary>
    public TimeSpan ReplayTimeToLive { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets how long completing or abandoning an execution may take. It is independent of the caller's cancellation token.
    /// Defaults to 10 seconds.
    /// </summary>
    public TimeSpan FinalizationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets an optional observer that is told when finalization fails or times out. Exceptions it throws are ignored.
    /// </summary>
    public Action<IdempotencyFinalizationFailure>? OnFinalizationFailure { get; init; }
}

/// <summary>
/// Per-call overrides of <see cref="IdempotencyExecutorOptions"/>. Null members use the executor defaults.
/// </summary>
public sealed record class IdempotencyCallOptions
{
    /// <summary>
    /// Gets the completion policy for this call.
    /// </summary>
    public IdempotencyCompletionPolicy? CompletionPolicy { get; init; }

    /// <summary>
    /// Gets the execution lease for this call.
    /// </summary>
    public TimeSpan? ExecutionLease { get; init; }

    /// <summary>
    /// Gets the replay time-to-live for this call.
    /// </summary>
    public TimeSpan? ReplayTimeToLive { get; init; }

    /// <summary>
    /// Gets an optional fingerprint of the request. Reusing a key with a different fingerprint fails with
    /// <see cref="IdempotencyErrors.PayloadMismatchCode"/>. Null means unchecked.
    /// </summary>
    public string? RequestFingerprint { get; init; }
}

/// <summary>
/// Describes a finalization (complete or abandon) that failed or timed out. The lease will simply expire.
/// </summary>
public sealed record class IdempotencyFinalizationFailure
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyFinalizationFailure"/> class.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="action">The finalization action, either <c>Complete</c> or <c>Abandon</c>.</param>
    /// <param name="exception">The exception that stopped finalization, including timeouts.</param>
    public IdempotencyFinalizationFailure(IdempotencyKey key, string action, Exception exception)
    {
        Key = key;
        Action = action;
        Exception = exception;
    }

    /// <summary>
    /// Gets the idempotency key.
    /// </summary>
    public IdempotencyKey Key { get; }

    /// <summary>
    /// Gets the finalization action, either <c>Complete</c> or <c>Abandon</c>.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Gets the exception that stopped finalization, including timeouts.
    /// </summary>
    public Exception Exception { get; }
}
