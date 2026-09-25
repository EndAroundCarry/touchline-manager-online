using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>
/// An immutable audit fact. Append-only; corrections are new entries, never edits (master plan §13).
/// </summary>
/// <param name="Action">The action performed, e.g. <c>auth.login.succeeded</c>.</param>
/// <param name="ActorType">What kind of actor acted: <c>user</c>, <c>service</c>, <c>anonymous</c>.</param>
/// <param name="ActorUserId">The acting account, when one is authenticated.</param>
/// <param name="TargetType">The kind of entity acted upon, when there is one.</param>
/// <param name="TargetId">The identity of the entity acted upon, when there is one.</param>
/// <param name="CorrelationId">The correlation ID that ties this row to logs and jobs.</param>
/// <param name="IpHash">The hashed client IP, never the raw address.</param>
/// <param name="Reason">Why the action was taken, for operator actions.</param>
public sealed record AuditEntry(
    string Action,
    string ActorType,
    Guid? ActorUserId,
    string? TargetType,
    Guid? TargetId,
    string CorrelationId,
    string? IpHash,
    string? Reason);

/// <summary>Recorded actor kinds for <see cref="AuditEntry.ActorType"/>.</summary>
public static class AuditActorTypes
{
    /// <summary>An authenticated account.</summary>
    public const string User = "user";

    /// <summary>An automated worker or job.</summary>
    public const string Service = "service";

    /// <summary>An unauthenticated client, as in a failed login.</summary>
    public const string Anonymous = "anonymous";
}

/// <summary>Recorded audit actions for the auth module.</summary>
public static class AuthAuditActions
{
    /// <summary>An account was registered.</summary>
    public const string Registered = "auth.register";

    /// <summary>An email address was verified.</summary>
    public const string EmailVerified = "auth.email.verified";

    /// <summary>A verification email was re-sent.</summary>
    public const string VerificationResent = "auth.email.verification_resent";

    /// <summary>A login succeeded.</summary>
    public const string LoginSucceeded = "auth.login.succeeded";

    /// <summary>A login failed.</summary>
    public const string LoginFailed = "auth.login.failed";

    /// <summary>A refresh token was rotated.</summary>
    public const string SessionRefreshed = "auth.session.refreshed";

    /// <summary>A reused refresh token revoked its whole family.</summary>
    public const string RefreshReuseDetected = "auth.session.reuse_detected";

    /// <summary>The current session was signed out.</summary>
    public const string LoggedOut = "auth.logout";

    /// <summary>Every session was signed out.</summary>
    public const string LoggedOutAll = "auth.logout_all";

    /// <summary>A password reset was requested.</summary>
    public const string PasswordResetRequested = "auth.password.reset_requested";

    /// <summary>A password was reset.</summary>
    public const string PasswordReset = "auth.password.reset";

    /// <summary>The display name changed.</summary>
    public const string ProfileUpdated = "auth.profile.updated";

    /// <summary>Account deletion was requested.</summary>
    public const string DeletionRequested = "auth.account.deletion_requested";
}

/// <summary>The audit trail writer for the ops module.</summary>
/// <remarks>
/// <see cref="Record"/> stages an entry in the current unit of work rather than writing
/// immediately, so an audit row commits atomically with the state change it describes. A use case
/// that changes state and records an audit entry can therefore never produce one without the other.
/// </remarks>
public interface IAuditWriter
{
    /// <summary>Stages an audit entry for the next save.</summary>
    void Record(AuditEntry entry);
}

/// <summary>Recorded target kinds for <see cref="AuditEntry.TargetType"/>.</summary>
public static class AuditTargetTypes
{
    /// <summary>An account.</summary>
    public const string User = "user";

    /// <summary>A refresh session.</summary>
    public const string RefreshSession = "refresh_session";

    /// <summary>A manager profile (`WORLD-7`).</summary>
    public const string Manager = "manager";

    /// <summary>A club.</summary>
    public const string Club = "club";

    /// <summary>A club tenure.</summary>
    public const string ClubTenure = "club_tenure";

    /// <summary>A division-provisioning request.</summary>
    public const string DivisionProvisioningRequest = "division_provisioning_request";

    /// <summary>A world and the generation run that produced it.</summary>
    public const string GameWorld = "game_world";
}
