using System.Globalization;

namespace TouchlineManager.Domain.Auth;

/// <summary>
/// An account. The aggregate root for everything identity-related.
/// </summary>
/// <remarks>
/// <para>
/// The invariants that matter competitively live here rather than in an endpoint: an account cannot
/// be verified from a suspended state, a display name cannot change before verification, and every
/// event that must invalidate existing sessions bumps the security stamp (ADR-0002).
/// </para>
/// <para>
/// The aggregate is deterministic — it never reads the clock or a random source. Callers pass the
/// current instant and any newly generated secret, which is what makes the lifecycle testable
/// without freezing time.
/// </para>
/// </remarks>
public sealed class User
{
    private readonly List<UserRoleAssignment> _roles = [];

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private User()
    {
    }

    /// <summary>Gets the account identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the email address exactly as the account holder typed it.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Gets the comparison form of <see cref="Email"/>, which carries the unique index.</summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>Gets the password hash. Raw passwords are never stored or logged.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>Gets the public display name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Gets the comparison form of <see cref="DisplayName"/>, which carries the unique index.</summary>
    public string NormalizedDisplayName { get; private set; } = string.Empty;

    /// <summary>Gets when the address was verified, or <see langword="null"/> while unverified.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>Gets when the account last authenticated successfully.</summary>
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>Gets the stamp that invalidates previously issued access tokens when it changes.</summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    /// <summary>Gets the number of consecutive failed login attempts.</summary>
    public int FailedLoginCount { get; private set; }

    /// <summary>Gets when the current lockout expires, if one is in force.</summary>
    public DateTimeOffset? LockoutUntil { get; private set; }

    /// <summary>Gets when the account was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the account was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets the roles granted to the account.</summary>
    public IReadOnlyCollection<UserRoleAssignment> Roles => _roles;

    /// <summary>Gets a value indicating whether the account is currently locked out.</summary>
    /// <param name="now">The current instant.</param>
    public bool IsLockedOut(DateTimeOffset now) => AccountLockoutPolicy.IsLocked(LockoutUntil, now);

    /// <summary>Gets a value indicating whether the account may present credentials right now.</summary>
    /// <param name="now">The current instant.</param>
    public bool CanAuthenticate(DateTimeOffset now) =>
        UserStatusRules.CanAuthenticate(Status) && !IsLockedOut(now);

    /// <summary>Registers a new, unverified account.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="email">The address as entered.</param>
    /// <param name="displayName">The requested display name.</param>
    /// <param name="passwordHash">The already-hashed password.</param>
    /// <param name="securityStamp">A freshly generated security stamp.</param>
    /// <param name="now">The current instant.</param>
    public static User Register(
        Guid id,
        string email,
        string displayName,
        string passwordHash,
        string securityStamp,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);

        var user = new User
        {
            Id = id,
            Email = email.Trim(),
            NormalizedEmail = NormalizeEmail(email),
            DisplayName = displayName.Trim(),
            NormalizedDisplayName = NormalizeDisplayName(displayName),
            PasswordHash = passwordHash,
            SecurityStamp = securityStamp,
            Status = UserStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        user.GrantRole(UserRoles.Player, now);

        return user;
    }

    /// <summary>Gets the comparison form of an email address.</summary>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Trim().ToLowerInvariant();
    }

    /// <summary>Gets the comparison form of a display name.</summary>
    public static string NormalizeDisplayName(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        return displayName.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Marks the address verified and activates the account. Idempotent for an already-verified,
    /// non-suspended account.
    /// </summary>
    /// <param name="now">The current instant.</param>
    public void MarkEmailVerified(DateTimeOffset now)
    {
        if (Status is UserStatus.Suspended or UserStatus.Anonymized)
        {
            throw new InvalidOperationException(
                $"An account in state '{Status}' cannot be verified.");
        }

        EmailVerifiedAt ??= now;

        if (Status == UserStatus.Pending)
        {
            Status = UserStatus.Active;
        }

        Touch(now);
    }

    /// <summary>Records a successful authentication and clears any lockout.</summary>
    /// <param name="now">The current instant.</param>
    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
        LastLoginAt = now;

        Touch(now);
    }

    /// <summary>
    /// Records a failed authentication and escalates the lockout ladder when the threshold is
    /// reached.
    /// </summary>
    /// <param name="now">The current instant.</param>
    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;

        var duration = AccountLockoutPolicy.LockoutDurationFor(FailedLoginCount);

        if (duration is not null)
        {
            LockoutUntil = now.Add(duration.Value);
        }

        Touch(now);
    }

    /// <summary>
    /// Suspends the account and invalidates its sessions. A suspended account loses write access
    /// immediately, independent of token validity (ADR-0002).
    /// </summary>
    /// <param name="newSecurityStamp">A freshly generated security stamp.</param>
    /// <param name="now">The current instant.</param>
    public void Suspend(string newSecurityStamp, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newSecurityStamp);

        Status = UserStatus.Suspended;
        SecurityStamp = newSecurityStamp;

        Touch(now);
    }

    /// <summary>Reinstates a suspended account at its natural state for its verification history.</summary>
    /// <param name="now">The current instant.</param>
    public void Restore(DateTimeOffset now)
    {
        if (Status != UserStatus.Suspended)
        {
            throw new InvalidOperationException($"Only a suspended account can be restored, not '{Status}'.");
        }

        Status = EmailVerifiedAt.HasValue ? UserStatus.Active : UserStatus.Pending;

        Touch(now);
    }

    /// <summary>Changes the display name. Requires a verified account (ADR-0002).</summary>
    /// <param name="displayName">The new display name.</param>
    /// <param name="now">The current instant.</param>
    public void ChangeDisplayName(string displayName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!UserStatusRules.CanWrite(Status))
        {
            throw new InvalidOperationException(
                "A display name can only be changed by a verified account (ADR-0002).");
        }

        DisplayName = displayName.Trim();
        NormalizedDisplayName = NormalizeDisplayName(displayName);

        Touch(now);
    }

    /// <summary>
    /// Replaces the password hash, clears lockout state, and invalidates existing sessions.
    /// </summary>
    /// <param name="passwordHash">The already-hashed new password.</param>
    /// <param name="newSecurityStamp">A freshly generated security stamp.</param>
    /// <param name="now">The current instant.</param>
    public void ChangePassword(string passwordHash, string newSecurityStamp, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(newSecurityStamp);

        PasswordHash = passwordHash;
        SecurityStamp = newSecurityStamp;
        FailedLoginCount = 0;
        LockoutUntil = null;

        Touch(now);
    }

    /// <summary>Requests deletion: access closes now, anonymization follows after the cooling period.</summary>
    /// <param name="newSecurityStamp">A freshly generated security stamp.</param>
    /// <param name="now">The current instant.</param>
    public void RequestDeletion(string newSecurityStamp, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newSecurityStamp);

        // Idempotent: a second request must not rotate the stamp again, because that would invalidate
        // the session the first request already established as the only surviving one.
        if (Status is UserStatus.DeletionPending or UserStatus.Anonymized)
        {
            return;
        }

        Status = UserStatus.DeletionPending;
        SecurityStamp = newSecurityStamp;

        Touch(now);
    }

    /// <summary>
    /// Replaces the password hash without touching sessions, for transparent rehash after a
    /// successful login with a stale work factor (ADR-0002).
    /// </summary>
    /// <param name="passwordHash">The rehashed password.</param>
    /// <param name="now">The current instant.</param>
    public void UpgradePasswordHash(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;

        Touch(now);
    }

    /// <summary>
    /// Invalidates every access token already issued for the account without otherwise changing its
    /// state. Used by sign-out-everywhere and after a password change (ADR-0002).
    /// </summary>
    /// <param name="newSecurityStamp">A freshly generated security stamp.</param>
    /// <param name="now">The current instant.</param>
    public void RevokeAllSessions(string newSecurityStamp, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newSecurityStamp);

        SecurityStamp = newSecurityStamp;

        Touch(now);
    }

    /// <summary>Grants a known role. Idempotent.</summary>
    /// <param name="role">The role name.</param>
    /// <param name="now">The current instant.</param>
    public void GrantRole(string role, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (!UserRoles.IsKnown(role))
        {
            throw new ArgumentException($"'{role}' is not a known role.", nameof(role));
        }

        if (_roles.Exists(existing => string.Equals(existing.Role, role, StringComparison.Ordinal)))
        {
            return;
        }

        _roles.Add(UserRoleAssignment.Create(Id, role, now));
    }

    /// <summary>Gets whether the account holds a role.</summary>
    public bool HasRole(string role) =>
        _roles.Exists(assignment => string.Equals(assignment.Role, role, StringComparison.Ordinal));

    /// <summary>Gets the role names the account holds, in a stable order.</summary>
    public IReadOnlyList<string> RoleNames() =>
        _roles.Select(assignment => assignment.Role).Order(StringComparer.Ordinal).ToList();

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
