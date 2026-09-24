namespace TouchlineManager.Domain.Auth;

/// <summary>
/// Progressive lockout after repeated failed logins (ADR-0002, master plan §12.1).
/// </summary>
/// <remarks>
/// <para>
/// The policy is pure and clock-free: the caller supplies the failed count and the current instant,
/// so the escalation ladder is unit-testable without waiting for real time to pass.
/// </para>
/// <para>
/// The ladder is deliberately capped. A lockout must slow credential stuffing without letting an
/// attacker lock a known account out of the game indefinitely — the final tier repeats forever
/// rather than growing unbounded.
/// </para>
/// </remarks>
public static class AccountLockoutPolicy
{
    /// <summary>Failed attempts allowed before the first lockout.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>
    /// Escalating lockout durations, indexed by how far past <see cref="MaxFailedAttempts"/> the
    /// failed count has climbed.
    /// </summary>
    private static readonly TimeSpan[] LockoutLadder =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
    ];

    /// <summary>Longest lockout the ladder can produce.</summary>
    public static TimeSpan MaxLockoutDuration => LockoutLadder[^1];

    /// <summary>Reports whether the account is currently locked out.</summary>
    public static bool IsLocked(DateTimeOffset? lockoutUntil, DateTimeOffset now) =>
        lockoutUntil is not null && lockoutUntil > now;

    /// <summary>
    /// Returns the lockout duration a failed attempt should apply, or <see langword="null"/> when
    /// the account has not yet reached the threshold.
    /// </summary>
    /// <param name="failedLoginCount">Failed attempts including the one being recorded.</param>
    public static TimeSpan? LockoutDurationFor(int failedLoginCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failedLoginCount);

        if (failedLoginCount < MaxFailedAttempts)
        {
            return null;
        }

        var tier = Math.Min(failedLoginCount - MaxFailedAttempts, LockoutLadder.Length - 1);

        return LockoutLadder[tier];
    }
}
