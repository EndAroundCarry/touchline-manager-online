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

    /// <summary>The session to revoke does not exist or does not belong to the account.</summary>
    public const string SessionNotFound = "SESSION_NOT_FOUND";

    /// <summary>The session is the one making the request; signing out is how to end it.</summary>
    public const string CurrentSessionCannotBeRevoked = "CURRENT_SESSION";

    /// <summary>The account was deleted or is awaiting anonymization.</summary>
    public const string AccountDeleted = "ACCOUNT_DELETED";

    /// <summary>The password was accepted but a second factor is required to finish signing in.</summary>
    public const string MfaRequired = "MFA_REQUIRED";

    /// <summary>The account has no confirmed second factor to satisfy the requirement with.</summary>
    public const string MfaNotEnrolled = "MFA_NOT_ENROLLED";

    /// <summary>The account has already confirmed a second factor and must reset it to enrol again.</summary>
    public const string MfaAlreadyEnrolled = "MFA_ALREADY_ENROLLED";

    /// <summary>The multi-factor code or recovery code did not match.</summary>
    public const string MfaCodeInvalid = "MFA_CODE_INVALID";

    /// <summary>The account holds a role for which a second factor is mandatory, so it cannot be disabled.</summary>
    public const string MfaRequiredForRole = "MFA_REQUIRED_FOR_ROLE";

    /// <summary>The multi-factor login challenge is unknown, expired, or not a challenge.</summary>
    public const string MfaChallengeInvalid = "MFA_CHALLENGE_INVALID";
}
