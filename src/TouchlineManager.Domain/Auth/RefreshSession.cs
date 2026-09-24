namespace TouchlineManager.Domain.Auth;

/// <summary>
/// A refresh session: one rotating refresh token in a token family (<c>auth.refresh_sessions</c>).
/// </summary>
/// <remarks>
/// <para>
/// Only the hash of the token is stored, so a database disclosure does not yield usable
/// credentials (ADR-0002). Rotation links each session to its replacement, which is what makes
/// reuse detectable: presenting a token that has already been rotated is proof of theft, and the
/// whole family is revoked.
/// </para>
/// <para>
/// <see cref="Version"/> is the optimistic concurrency token. Two simultaneous refreshes with the
/// same token therefore cannot both succeed — the loser is treated as reuse, which is the
/// documented behaviour.
/// </para>
/// </remarks>
public sealed class RefreshSession
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private RefreshSession()
    {
    }

    /// <summary>Gets the session identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the account that owns the session.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the hash of the opaque refresh token. The raw value is never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Gets the token family. Every rotation within one login shares a family.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>Gets when the session was issued.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>Gets when the session expires.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets when the session was last used to refresh.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>Gets when the session was revoked, if it has been.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Gets why the session was revoked.</summary>
    public string? RevocationReason { get; private set; }

    /// <summary>Gets the session that replaced this one during rotation, if any.</summary>
    public Guid? ReplacedBySessionId { get; private set; }

    /// <summary>Gets the hashed client IP prefix, recorded for support analysis (INT-3).</summary>
    public string? IpPrefixHash { get; private set; }

    /// <summary>Gets the hashed user agent, recorded for support analysis (INT-3).</summary>
    public string? UserAgentHash { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Whether the session may still be used to refresh.</summary>
    /// <param name="now">The current instant.</param>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Issues a new session.</summary>
    public static RefreshSession Issue(
        Guid id,
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        string? ipPrefixHash,
        string? userAgentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, "A session must expire in the future.");
        }

        return new RefreshSession
        {
            Id = id,
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            IpPrefixHash = ipPrefixHash,
            UserAgentHash = userAgentHash,
            Version = 1,
        };
    }

    /// <summary>Records that the session was rotated into a replacement.</summary>
    /// <param name="replacementSessionId">The identity of the session that replaces this one.</param>
    /// <param name="now">The current instant.</param>
    public void Rotate(Guid replacementSessionId, DateTimeOffset now)
    {
        // Idempotent: the first rotation names the replacement. A second one is a bug, and silently
        // repointing the chain would make the recorded token lineage a lie.
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = RefreshSessionRevocationReasons.Rotated;
        ReplacedBySessionId = replacementSessionId;
        LastUsedAt = now;

        Version++;
    }

    /// <summary>Revokes the session.</summary>
    /// <param name="reason">The audit reason. See <see cref="RefreshSessionRevocationReasons"/>.</param>
    /// <param name="now">The current instant.</param>
    public void Revoke(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;

        Version++;
    }
}

/// <summary>Recorded reasons a refresh session was revoked.</summary>
public static class RefreshSessionRevocationReasons
{
    /// <summary>The session was consumed by rotation.</summary>
    public const string Rotated = "rotated";

    /// <summary>The account holder signed out of this session.</summary>
    public const string Logout = "logout";

    /// <summary>The account holder signed out of every session.</summary>
    public const string LogoutAll = "logout_all";

    /// <summary>A reused token was presented, so the whole family was revoked (ADR-0002).</summary>
    public const string ReuseDetected = "reuse_detected";

    /// <summary>The password was reset, which invalidates every existing session.</summary>
    public const string PasswordReset = "password_reset";

    /// <summary>The account was suspended.</summary>
    public const string AccountSuspended = "account_suspended";

    /// <summary>The account requested deletion.</summary>
    public const string AccountDeletion = "account_deletion";
}
