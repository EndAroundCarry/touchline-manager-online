namespace TouchlineManager.Domain.Auth;

/// <summary>
/// The account lifecycle state of a user (master plan §6.2, ADR-0002).
/// </summary>
/// <remarks>
/// Email verification is tracked separately by <c>email_verified_at</c>, so an account can be
/// <see cref="Pending"/> (registered, unverified) without a second state variable. A verified
/// account becomes <see cref="Active"/>.
/// </remarks>
public enum UserStatus
{
    /// <summary>Registered but not yet email-verified. May authenticate; may not write game state.</summary>
    Pending = 0,

    /// <summary>Verified and in good standing.</summary>
    Active = 1,

    /// <summary>Suspended by an administrator. Loses write access immediately.</summary>
    Suspended = 2,

    /// <summary>Deletion requested; access closed while the cooling period runs.</summary>
    DeletionPending = 3,

    /// <summary>Identity anonymized after deletion. Terminal; historical records are retained.</summary>
    Anonymized = 4,
}

/// <summary>
/// The authorization questions the rest of the product asks about <see cref="UserStatus"/>.
/// </summary>
/// <remarks>
/// Keeping these as named rules rather than scattered status comparisons is what makes
/// "a suspended account loses write access immediately" a single enforced statement (ADR-0002,
/// master plan §12.1) instead of a check each feature has to remember.
/// </remarks>
public static class UserStatusRules
{
    /// <summary>Whether an account in this state may present credentials and hold a session.</summary>
    public static bool CanAuthenticate(UserStatus status) =>
        status is UserStatus.Pending or UserStatus.Active;

    /// <summary>
    /// Whether an account in this state may perform writes. Verification is required before a
    /// club claim, bid, listing, or display-name change (ADR-0002), so only <see cref="UserStatus.Active"/>
    /// qualifies.
    /// </summary>
    public static bool CanWrite(UserStatus status) => status == UserStatus.Active;

    /// <summary>Whether the account is past the point of no return and must not be reactivated.</summary>
    public static bool IsTerminal(UserStatus status) => status == UserStatus.Anonymized;
}

/// <summary>
/// Storage and transport representation of <see cref="UserStatus"/>.
/// </summary>
/// <remarks>
/// The persisted and exposed form is a stable lowercase code rather than the enum's numeric value,
/// so the database stays readable and reordering the enum cannot silently reinterpret existing rows.
/// </remarks>
public static class UserStatuses
{
    /// <summary>The code for <see cref="UserStatus.Pending"/>.</summary>
    public const string PendingCode = "pending";

    /// <summary>The code for <see cref="UserStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="UserStatus.Suspended"/>.</summary>
    public const string SuspendedCode = "suspended";

    /// <summary>The code for <see cref="UserStatus.DeletionPending"/>.</summary>
    public const string DeletionPendingCode = "deletion_pending";

    /// <summary>The code for <see cref="UserStatus.Anonymized"/>.</summary>
    public const string AnonymizedCode = "anonymized";

    /// <summary>Converts a status to its stable code.</summary>
    public static string ToCode(this UserStatus status) => status switch
    {
        UserStatus.Pending => PendingCode,
        UserStatus.Active => ActiveCode,
        UserStatus.Suspended => SuspendedCode,
        UserStatus.DeletionPending => DeletionPendingCode,
        UserStatus.Anonymized => AnonymizedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown user status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    public static UserStatus FromCode(string code) => code switch
    {
        PendingCode => UserStatus.Pending,
        ActiveCode => UserStatus.Active,
        SuspendedCode => UserStatus.Suspended,
        DeletionPendingCode => UserStatus.DeletionPending,
        AnonymizedCode => UserStatus.Anonymized,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown user status code."),
    };
}
