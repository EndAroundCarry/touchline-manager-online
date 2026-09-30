namespace TouchlineManager.Domain.Auth;

/// <summary>
/// One account's multi-factor credential: a protected TOTP secret and its one-time recovery codes
/// (<c>auth.mfa_credentials</c>, master plan §10.8, ADR-0042).
/// </summary>
/// <remarks>
/// <para>
/// The secret is stored exactly as the protection layer handed it over. The aggregate never sees the raw
/// bytes, because decrypting and computing a code is an application concern and the domain stays pure
/// (ADR-0042).
/// </para>
/// <para>
/// A credential exists in two states: unconfirmed, between enrolment starting and the first valid code,
/// and confirmed, from then on. Only a confirmed credential satisfies the second-factor requirement.
/// </para>
/// </remarks>
public sealed class MfaCredential
{
    private readonly List<MfaRecoveryCode> _recoveryCodes = [];

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private MfaCredential()
    {
    }

    /// <summary>Gets the account the credential belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the protected (encrypted) TOTP secret. Never the raw secret.</summary>
    public string ProtectedSecret { get; private set; } = string.Empty;

    /// <summary>Gets when the first valid code confirmed the credential, or <see langword="null"/>.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Gets when the credential was first created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the credential was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets the recovery codes that were issued and not yet used.</summary>
    public IReadOnlyCollection<MfaRecoveryCode> RecoveryCodes => _recoveryCodes;

    /// <summary>Gets a value indicating whether the credential has been confirmed by a valid code.</summary>
    public bool IsConfirmed => ConfirmedAt.HasValue;

    /// <summary>Starts enrolment: a new, unconfirmed credential for a protected secret.</summary>
    /// <param name="userId">The account enrolling.</param>
    /// <param name="protectedSecret">The protected secret.</param>
    /// <param name="now">The current instant.</param>
    public static MfaCredential StartEnrolment(Guid userId, string protectedSecret, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);

        return new MfaCredential
        {
            UserId = userId,
            ProtectedSecret = protectedSecret,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Replaces the secret and returns the credential to unconfirmed, as a re-enrolment does.</summary>
    /// <param name="protectedSecret">The new protected secret.</param>
    /// <param name="now">The current instant.</param>
    public void ReplaceSecret(string protectedSecret, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);

        ProtectedSecret = protectedSecret;
        ConfirmedAt = null;

        Touch(now);
    }

    /// <summary>Confirms the credential with the first valid code. Idempotent.</summary>
    /// <param name="now">The current instant.</param>
    public void Confirm(DateTimeOffset now)
    {
        ConfirmedAt ??= now;

        Touch(now);
    }

    /// <summary>Replaces every recovery code. Idempotent only in the sense that it always replaces.</summary>
    /// <param name="codeHashes">The hashed codes to store.</param>
    /// <param name="now">The current instant.</param>
    public void ReplaceRecoveryCodes(IEnumerable<string> codeHashes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(codeHashes);

        _recoveryCodes.Clear();

        foreach (var hash in codeHashes)
        {
            _recoveryCodes.Add(MfaRecoveryCode.Create(UserId, hash));
        }

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
