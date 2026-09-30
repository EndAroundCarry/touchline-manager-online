namespace TouchlineManager.Domain.Auth;

/// <summary>
/// One single-use recovery code for an account's multi-factor credential
/// (<c>auth.mfa_recovery_codes</c>, ADR-0042).
/// </summary>
/// <remarks>
/// Only the hash is stored, the way a refresh token is, so a disclosure of the table does not yield a
/// usable code. A code is consumed at most once; a consumed code stays as evidence.
/// </remarks>
public sealed class MfaRecoveryCode
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private MfaRecoveryCode()
    {
    }

    /// <summary>Gets the account the code belongs to. Composite key with <see cref="CodeHash"/>.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the hash of the code. The raw value is shown once and never stored.</summary>
    public string CodeHash { get; private set; } = string.Empty;

    /// <summary>Gets when the code was used, or <see langword="null"/> while it is still available.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Gets a value indicating whether the code has been used.</summary>
    public bool IsUsed => UsedAt.HasValue;

    /// <summary>Consumes the code. Idempotent: the first use records the instant.</summary>
    /// <param name="now">The current instant.</param>
    public void Consume(DateTimeOffset now) => UsedAt ??= now;

    internal static MfaRecoveryCode Create(Guid userId, string codeHash) => new()
    {
        UserId = userId,
        CodeHash = codeHash,
    };
}
