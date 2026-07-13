using Atya.Foundation.Guards;
using Atya.Foundation.Time;

namespace Atya.Application.Idempotency;

/// <summary>
/// Defines the UTC validity window for an idempotency record.
/// </summary>
public readonly record struct IdempotencyLease
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyLease"/> struct.
    /// </summary>
    /// <param name="expiresAtUtc">The UTC instant when the record expires.</param>
    /// <exception cref="ArgumentException"><paramref name="expiresAtUtc"/> does not use the UTC offset.</exception>
    public IdempotencyLease(DateTimeOffset expiresAtUtc)
    {
        ExpiresAtUtc = TimeGuard.AgainstNonUtc(expiresAtUtc);
    }

    /// <summary>
    /// Gets the UTC instant when the record expires.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>
    /// Creates a lease from a clock and positive time-to-live.
    /// </summary>
    /// <param name="clock">The clock used to compute the expiration instant.</param>
    /// <param name="timeToLive">The positive time-to-live.</param>
    /// <returns>The computed lease.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeToLive"/> is zero or negative.</exception>
    public static IdempotencyLease From(IClock clock, TimeSpan timeToLive)
    {
        Guard.AgainstNull(clock);

        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), timeToLive, "Time-to-live must be greater than zero.");
        }

        return new IdempotencyLease(clock.UtcNowOffset.Add(timeToLive));
    }

    /// <summary>
    /// Determines whether the lease has expired according to the supplied clock.
    /// </summary>
    /// <param name="clock">The clock used to evaluate expiration.</param>
    /// <returns><see langword="true"/> when the current UTC instant is at or after <see cref="ExpiresAtUtc"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    public bool IsExpired(IClock clock)
    {
        Guard.AgainstNull(clock);

        return clock.UtcNowOffset >= ExpiresAtUtc;
    }
}
