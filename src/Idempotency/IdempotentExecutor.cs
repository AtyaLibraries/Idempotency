using Atya.Foundation.Guards;
using Atya.Foundation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atya.Application.Idempotency;

/// <summary>
/// Executes operations at most once per key and replays stored results.
/// </summary>
public sealed class IdempotentExecutor
{
    private static readonly Action<ILogger, string, string, Exception?> s_finalizationFailed = LoggerMessage.Define<string, string>(
        LogLevel.Error,
        new EventId(9201, "IdempotencyFinalizationFailed"),
        "Idempotency finalization ({Action}) failed for key '{Key}'; the lease will expire.");

    private static readonly Action<ILogger, string, string, Exception?> s_superseded = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(9202, "IdempotencyFinalizationSuperseded"),
        "Idempotency finalization ({Action}) for key '{Key}' was not applied because the execution was superseded.");

    private readonly IIdempotencyStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IdempotencyExecutorOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotentExecutor"/> class.
    /// </summary>
    /// <param name="store">The idempotency store.</param>
    /// <param name="timeProvider">The time provider used for the finalization timeout.</param>
    /// <param name="options">The executor defaults. Null uses <see cref="IdempotencyExecutorOptions"/> defaults.</param>
    /// <param name="logger">An optional logger for finalization failures.</param>
    public IdempotentExecutor(
        IIdempotencyStore store,
        TimeProvider timeProvider,
        IdempotencyExecutorOptions? options = null,
        ILogger<IdempotentExecutor>? logger = null)
    {
        _store = Guard.AgainstNull(store);
        _timeProvider = Guard.AgainstNull(timeProvider);
        _options = options ?? new IdempotencyExecutorOptions();
        _logger = logger ?? NullLogger<IdempotentExecutor>.Instance;
        ValidatePositive(_options.ExecutionLease, nameof(IdempotencyExecutorOptions.ExecutionLease));
        ValidatePositive(_options.ReplayTimeToLive, nameof(IdempotencyExecutorOptions.ReplayTimeToLive));
        ValidatePositive(_options.FinalizationTimeout, nameof(IdempotencyExecutorOptions.FinalizationTimeout));
    }

    /// <summary>
    /// Executes <paramref name="operation"/> once for <paramref name="key"/>, or replays or rejects as the store dictates.
    /// </summary>
    /// <typeparam name="TValue">The operation result value type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="operation">The operation to run.</param>
    /// <param name="call">Optional per-call overrides.</param>
    /// <param name="cancellationToken">A token that cancels the operation. Finalization does not use it.</param>
    /// <returns>The operation result, a stored replay, or a conflict failure.</returns>
    /// <remarks>
    /// An exception or cancellation abandons the key before it is rethrown. A failed result is abandoned unless the
    /// completion policy is <see cref="IdempotencyCompletionPolicy.SuccessAndFailure"/>. The result of the operation is
    /// always returned to the caller, even if finalization fails, times out or is superseded.
    /// </remarks>
    public async ValueTask<Result<TValue>> ExecuteAsync<TValue>(
        IdempotencyKey key,
        Func<CancellationToken, ValueTask<Result<TValue>>> operation,
        IdempotencyCallOptions? call = null,
        CancellationToken cancellationToken = default)
    {
        Guard.AgainstNull(operation);

        TimeSpan executionLease = call?.ExecutionLease ?? _options.ExecutionLease;
        TimeSpan replayTimeToLive = call?.ReplayTimeToLive ?? _options.ReplayTimeToLive;
        IdempotencyCompletionPolicy policy = call?.CompletionPolicy ?? _options.CompletionPolicy;
        ValidatePositive(executionLease, nameof(IdempotencyCallOptions.ExecutionLease));
        ValidatePositive(replayTimeToLive, nameof(IdempotencyCallOptions.ReplayTimeToLive));

        IdempotencyBeginResult<TValue> begin = await _store
            .TryBeginAsync<TValue>(key, executionLease, call?.RequestFingerprint, cancellationToken)
            .ConfigureAwait(false);

        switch (begin.Status)
        {
            case IdempotencyBeginStatus.Completed:
                return begin.Replay!.Result;
            case IdempotencyBeginStatus.InProgress:
                return IdempotencyErrors.InProgressResult<TValue>(key);
            case IdempotencyBeginStatus.PayloadMismatch:
                return IdempotencyErrors.PayloadMismatchResult<TValue>(key);
            case IdempotencyBeginStatus.CapacityExceeded:
                return IdempotencyErrors.CapacityExceededResult<TValue>(key);
        }

        IdempotencyExecution execution = begin.Execution!;

        Result<TValue> result;
        try
        {
            result = await operation(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await FinalizeAsync(key, "Abandon", token => _store.AbandonAsync(execution, token)).ConfigureAwait(false);
            throw;
        }

        if (result.IsSuccess || policy == IdempotencyCompletionPolicy.SuccessAndFailure)
        {
            await FinalizeAsync(key, "Complete", token => _store.CompleteAsync(execution, result, replayTimeToLive, token)).ConfigureAwait(false);
        }
        else
        {
            await FinalizeAsync(key, "Abandon", token => _store.AbandonAsync(execution, token)).ConfigureAwait(false);
        }

        return result;
    }

    private static void ValidatePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be greater than zero.");
        }
    }

    private async ValueTask FinalizeAsync(IdempotencyKey key, string action, Func<CancellationToken, ValueTask<IdempotencyTransition>> finalize)
    {
        try
        {
            using var timeout = new CancellationTokenSource(_options.FinalizationTimeout, _timeProvider);
            IdempotencyTransition transition = await finalize(timeout.Token).ConfigureAwait(false);
            if (transition == IdempotencyTransition.Superseded)
            {
                s_superseded(_logger, action, key.Value, null);
            }
        }
        catch (Exception exception)
        {
            s_finalizationFailed(_logger, action, key.Value, exception);
            NotifyObserver(new IdempotencyFinalizationFailure(key, action, exception));
        }
    }

    private void NotifyObserver(IdempotencyFinalizationFailure failure)
    {
        try
        {
            _options.OnFinalizationFailure?.Invoke(failure);
        }
        catch (Exception)
        {
            // The observer must never replace the operation's result.
        }
    }
}
