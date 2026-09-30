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

    /// <summary>One session was revoked from the session list, while staying signed in elsewhere.</summary>
    public const string SessionRevoked = "auth.session.revoked";

    /// <summary>The account's own data was exported at its request (master plan §12.4).</summary>
    public const string AccountExported = "auth.account.exported";

    /// <summary>A password reset was requested.</summary>
    public const string PasswordResetRequested = "auth.password.reset_requested";

    /// <summary>A password was reset.</summary>
    public const string PasswordReset = "auth.password.reset";

    /// <summary>The display name changed.</summary>
    public const string ProfileUpdated = "auth.profile.updated";

    /// <summary>Account deletion was requested.</summary>
    public const string DeletionRequested = "auth.account.deletion_requested";
}

/// <summary>Recorded audit actions for operator and administrative authority (master plan §10.8, §13).</summary>
public static class AdminAuditActions
{
    /// <summary>A role was granted to an account.</summary>
    public const string RoleGranted = "admin.role.granted";

    /// <summary>A role was revoked from an account.</summary>
    public const string RoleRevoked = "admin.role.revoked";

    /// <summary>An account was suspended by an operator.</summary>
    public const string AccountSuspended = "admin.account.suspended";

    /// <summary>A suspended account was restored by an operator.</summary>
    public const string AccountRestored = "admin.account.restored";

    /// <summary>A dead-lettered job was returned to the queue by an operator (`F-46`, ADR-0044).</summary>
    public const string JobRetried = "admin.job.retried";

    /// <summary>A job was cancelled by an operator (`F-46`, ADR-0044).</summary>
    public const string JobCancelled = "admin.job.cancelled";

    /// <summary>A stuck matchday's resolution or publication job was requeued by an operator (`F-46`, ADR-0044).</summary>
    public const string MatchdayResumed = "admin.matchday.resumed";

    /// <summary>An operator published a game announcement to the news feed (`F-46`, ADR-0045).</summary>
    public const string AnnouncementPublished = "admin.announcement.published";

    /// <summary>An operator set a feature flag (`F-46`, ADR-0045).</summary>
    public const string FeatureFlagSet = "admin.feature_flag.set";

    /// <summary>Multi-factor enrolment was started (an unconfirmed credential was created).</summary>
    public const string MfaEnrolmentStarted = "auth.mfa.enrolment_started";

    /// <summary>Multi-factor authentication was enabled for an account.</summary>
    public const string MfaEnabled = "auth.mfa.enabled";

    /// <summary>Multi-factor authentication was disabled for an account.</summary>
    public const string MfaDisabled = "auth.mfa.disabled";

    /// <summary>A fresh set of recovery codes was issued.</summary>
    public const string RecoveryCodesRegenerated = "auth.mfa.recovery_codes_regenerated";
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

    /// <summary>A tactical plan (`INS-11`).</summary>
    public const string TacticalPlan = "tactical_plan";

    /// <summary>A club training plan (`TRN-1`).</summary>
    public const string TrainingPlan = "training_plan";

    /// <summary>A player's individual training focus (`TRN-2`).</summary>
    public const string PlayerTrainingFocus = "player_training_focus";

    /// <summary>A club's prepared side for one fixture (`SQ-4`).</summary>
    public const string FixtureTeamSheet = "fixture_team_sheet";

    /// <summary>A player's contract (`CON-3`, `CON-4`).</summary>
    public const string PlayerContract = "player_contract";

    /// <summary>A transfer listing (`TRF-1`, `TRF-15`).</summary>
    public const string TransferListing = "transfer_listing";

    /// <summary>A transfer bid (`TRF-4`, `TRF-7`).</summary>
    public const string TransferBid = "transfer_bid";

    /// <summary>A resolved transfer (`TRF-10`, `TRF-11`).</summary>
    public const string TransferOutcome = "transfer_outcome";

    /// <summary>A season rollover and the season it closed (`PR-4`, ADR-0031).</summary>
    public const string SeasonRollover = "season_rollover";

    /// <summary>An account's multi-factor credential (master plan §10.8, ADR-0042).</summary>
    public const string MfaCredential = "mfa_credential";

    /// <summary>A durable job row (`F-46`, ADR-0044).</summary>
    public const string Job = "job";

    /// <summary>A competition matchday, or the fixtures it holds (`F-46`, ADR-0044).</summary>
    public const string Matchday = "matchday";

    /// <summary>A club ledger entry, as written by an operator's compensating repair (`F-46`, ADR-0045).</summary>
    public const string LedgerEntry = "ledger_entry";

    /// <summary>A published news item, as written by an operator's announcement (`F-46`, ADR-0045).</summary>
    public const string NewsItem = "news_item";

    /// <summary>A feature flag (`F-46`, ADR-0045).</summary>
    public const string FeatureFlag = "feature_flag";
}
