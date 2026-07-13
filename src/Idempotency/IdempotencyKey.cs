using Atya.Foundation.Guards;
using Atya.Foundation.Results;

namespace Atya.Application.Idempotency;

/// <summary>
/// Represents the stable caller-provided key used to identify a repeatable operation.
/// </summary>
public readonly record struct IdempotencyKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyKey"/> struct.
    /// </summary>
    /// <param name="value">The non-empty key value.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty, or whitespace.</exception>
    public IdempotencyKey(string value)
    {
        Value = Guard.AgainstNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Gets the normalized key value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates an idempotency key from caller input.
    /// </summary>
    /// <param name="value">The caller-provided key value.</param>
    /// <returns>A successful key result, or a validation failure when the value is empty.</returns>
    public static Result<IdempotencyKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<IdempotencyKey>(
                "atya.application.idempotency.key_required",
                "An idempotency key is required.",
                ErrorKind.Validation);
        }

        return Result.Success(new IdempotencyKey(value));
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
