namespace TouchlineManager.Contracts.Ops;

/// <summary>Stable machine-readable error codes for the admin surface (master plan §10.8, §13).</summary>
/// <remarks>
/// Clients branch on these codes, never on the human-readable text. Adding a code is a contract change.
/// </remarks>
public static class AdminErrorCodes
{
    /// <summary>The account to suspend or restore does not exist, or is anonymized.</summary>
    public const string AccountNotFound = "ACCOUNT_NOT_FOUND";

    /// <summary>The account is already in the requested state.</summary>
    public const string AccountStateUnchanged = "ACCOUNT_STATE_UNCHANGED";

    /// <summary>An operator may not suspend its own account.</summary>
    public const string SelfSuspensionNotAllowed = "SELF_SUSPENSION_NOT_ALLOWED";

    /// <summary>The operator reason is missing or too long.</summary>
    public const string ReasonRequired = "REASON_REQUIRED";

    /// <summary>The required <c>Idempotency-Key</c> header is missing or too long.</summary>
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";

    /// <summary>A page cursor is not one this server produced.</summary>
    public const string InvalidCursor = "INVALID_CURSOR";

    /// <summary>A list filter names a value this server does not recognise.</summary>
    public const string InvalidFilter = "INVALID_FILTER";

    /// <summary>No job exists with the requested identity.</summary>
    public const string JobNotFound = "JOB_NOT_FOUND";

    /// <summary>The job is not dead-lettered, so a retry would change nothing.</summary>
    public const string JobNotRetryable = "JOB_NOT_RETRYABLE";

    /// <summary>The job is already completed or cancelled, so there is nothing to cancel.</summary>
    public const string JobNotCancellable = "JOB_NOT_CANCELLABLE";

    /// <summary>No matchday exists with the requested identity.</summary>
    public const string MatchdayNotFound = "MATCHDAY_NOT_FOUND";

    /// <summary>The matchday is published, or the queue already owns its job, so there is nothing to resume.</summary>
    public const string MatchdayNotResumable = "MATCHDAY_NOT_RESUMABLE";

    /// <summary>No club exists with the requested identity.</summary>
    public const string ClubNotFound = "CLUB_NOT_FOUND";

    /// <summary>The club already has no human manager, so there is nothing to assign to the AI.</summary>
    public const string ClubAlreadyAi = "CLUB_ALREADY_AI";

    /// <summary>The ledger entry a compensating repair names does not exist for that club.</summary>
    public const string LedgerEntryNotFound = "LEDGER_ENTRY_NOT_FOUND";

    /// <summary>The compensating entry is not a repair of an earlier entry, or moves nothing.</summary>
    public const string CompensationInvalid = "COMPENSATION_INVALID";

    /// <summary>The correction would take the club's cash below zero or below its reserved funds.</summary>
    public const string CompensationNotAffordable = "COMPENSATION_NOT_AFFORDABLE";

    /// <summary>This idempotency key has already posted a compensating entry.</summary>
    public const string CompensationAlreadyPosted = "COMPENSATION_ALREADY_POSTED";

    /// <summary>The announcement has no title or body, or would expire before it is published.</summary>
    public const string AnnouncementInvalid = "ANNOUNCEMENT_INVALID";

    /// <summary>The country or division the announcement is scoped to does not exist.</summary>
    public const string AnnouncementScopeNotFound = "ANNOUNCEMENT_SCOPE_NOT_FOUND";

    /// <summary>No world has been seeded, so there is nowhere to publish.</summary>
    public const string WorldNotSeeded = "WORLD_NOT_SEEDED";

    /// <summary>The feature-flag key or value is not one this surface accepts.</summary>
    public const string FeatureFlagInvalid = "FEATURE_FLAG_INVALID";
}
