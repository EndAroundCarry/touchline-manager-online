namespace TouchlineManager.Contracts.Auth;

/// <summary>Stable machine-readable error codes for the auth module (master plan §10).</summary>
/// <remarks>
/// Clients branch on these codes, never on the human-readable text, so wording can change without
/// breaking behaviour. Adding a code is a contract change.
/// </remarks>
public static class AuthErrorCodes
{
    /// <summary>The email address is already registered.</summary>
    public const string EmailAlreadyRegistered = "EMAIL_ALREADY_REGISTERED";

    /// <summary>The display name is already taken.</summary>
    public const string DisplayNameTaken = "DISPLAY_NAME_TAKEN";

    /// <summary>The email/password pair did not match. Deliberately does not say which.</summary>
    public const string InvalidCredentials = "INVALID_CREDENTIALS";

    /// <summary>The account is temporarily locked after repeated failures.</summary>
    public const string AccountLocked = "ACCOUNT_LOCKED";

    /// <summary>The account is suspended and may not authenticate.</summary>
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";

    /// <summary>The action requires a verified email address.</summary>
    public const string AccountNotVerified = "ACCOUNT_NOT_VERIFIED";

    /// <summary>The email token is unknown, expired, or already used.</summary>
    public const string InvalidToken = "INVALID_TOKEN";

    /// <summary>The refresh session is unknown, expired, revoked, or was replayed.</summary>
    public const string SessionInvalid = "SESSION_INVALID";

    /// <summary>The account was deleted or is awaiting anonymization.</summary>
    public const string AccountDeleted = "ACCOUNT_DELETED";
}
