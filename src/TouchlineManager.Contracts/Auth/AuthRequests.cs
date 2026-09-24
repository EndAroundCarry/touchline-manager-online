namespace TouchlineManager.Contracts.Auth;

/// <summary>Request to create an account (master plan §10.1).</summary>
public sealed record RegisterRequest
{
    /// <summary>Gets the email address. Verified before the account may write game state.</summary>
    public required string Email { get; init; }

    /// <summary>Gets the public display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the chosen password. Never logged or stored.</summary>
    public required string Password { get; init; }

    /// <summary>Gets whether the account holder accepted the current terms and privacy policy.</summary>
    public required bool AcceptTerms { get; init; }
}

/// <summary>Request to confirm ownership of an email address.</summary>
public sealed record VerifyEmailRequest
{
    /// <summary>Gets the account the token was issued for.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets the single-use verification token from the emailed link.</summary>
    public required string Token { get; init; }
}

/// <summary>Request to send a fresh verification email.</summary>
public sealed record ResendVerificationRequest
{
    /// <summary>Gets the email address to resend to.</summary>
    public required string Email { get; init; }
}

/// <summary>Request to authenticate.</summary>
public sealed record LoginRequest
{
    /// <summary>Gets the email address.</summary>
    public required string Email { get; init; }

    /// <summary>Gets the password.</summary>
    public required string Password { get; init; }
}

/// <summary>Request to start a password reset.</summary>
public sealed record ForgotPasswordRequest
{
    /// <summary>Gets the email address to send the reset link to.</summary>
    public required string Email { get; init; }
}

/// <summary>Request to complete a password reset.</summary>
public sealed record ResetPasswordRequest
{
    /// <summary>Gets the account the token was issued for.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets the single-use reset token from the emailed link.</summary>
    public required string Token { get; init; }

    /// <summary>Gets the new password.</summary>
    public required string NewPassword { get; init; }
}

/// <summary>Request to change the public display name. Requires a verified account.</summary>
public sealed record UpdateProfileRequest
{
    /// <summary>Gets the new display name.</summary>
    public required string DisplayName { get; init; }
}

/// <summary>Request to delete the account. Requires the current password as confirmation.</summary>
public sealed record DeleteAccountRequest
{
    /// <summary>Gets the current password, used to confirm the request.</summary>
    public required string Password { get; init; }
}
