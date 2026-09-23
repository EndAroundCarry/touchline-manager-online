namespace TouchlineManager.Domain.Ops;

/// <summary>
/// Exponential backoff with jitter for durable job retries.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic for a given jitter sample, so the retry schedule is unit-testable
/// without a clock or a random source. The caller supplies the jitter sample.
/// </para>
/// <para>
/// Jitter is applied as a factor in <c>[0.5, 1.0]</c> rather than full jitter in
/// <c>[0, delay]</c>. This still spreads a batch of simultaneously failed jobs so they do not
/// stampede the database together, while keeping retries ordered by attempt number, which
/// makes incident timelines readable. See ADR-0003.
/// </para>
/// </remarks>
public static class JobRetryPolicy
{
    /// <summary>Delay before the first retry.</summary>
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound on any computed delay.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(30);

    /// <summary>Attempts allowed before a job is dead-lettered.</summary>
    public const int DefaultMaxAttempts = 8;

    /// <summary>Exponent cap, so the computation cannot overflow on a pathological attempt count.</summary>
    private const int MaxExponent = 20;

    /// <summary>
    /// Computes the delay before the next attempt.
    /// </summary>
    /// <param name="attemptCount">Attempts already made. Must be at least 1.</param>
    /// <param name="jitterSample">A value in <c>[0, 1]</c> supplied by the caller.</param>
    /// <returns>A delay in <c>[0.5 x exponential, exponential]</c>, capped at <see cref="MaxDelay"/>.</returns>
    public static TimeSpan NextDelay(int attemptCount, double jitterSample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptCount, 1);
        if (jitterSample is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterSample), jitterSample, "Jitter sample must be between 0 and 1 inclusive.");
        }

        var exponent = Math.Min(attemptCount - 1, MaxExponent);
        var exponentialSeconds = Math.Min(
            BaseDelay.TotalSeconds * Math.Pow(2, exponent),
            MaxDelay.TotalSeconds);

        var jitterFactor = 0.5 + (0.5 * jitterSample);

        return TimeSpan.FromSeconds(exponentialSeconds * jitterFactor);
    }

    /// <summary>
    /// Reports whether the job has exhausted its attempts and must be dead-lettered rather
    /// than retried.
    /// </summary>
    /// <param name="attemptCount">Attempts already made.</param>
    /// <param name="maxAttempts">Configured attempt ceiling for the job.</param>
    public static bool IsPermanentFailure(int attemptCount, int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        return attemptCount >= maxAttempts;
    }
}
