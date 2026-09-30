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
}
