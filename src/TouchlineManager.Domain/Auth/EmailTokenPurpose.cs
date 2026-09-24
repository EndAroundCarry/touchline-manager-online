namespace TouchlineManager.Domain.Auth;

/// <summary>
/// Why an <see cref="EmailToken"/> was issued. A token is valid only for its own purpose, so a
/// leaked verification link cannot be replayed against the password-reset endpoint (ADR-0002).
/// </summary>
public enum EmailTokenPurpose
{
    /// <summary>Confirms ownership of the registered email address.</summary>
    VerifyEmail = 0,

    /// <summary>Authorizes a password reset.</summary>
    ResetPassword = 1,
}

/// <summary>
/// Storage representation of <see cref="EmailTokenPurpose"/>.
/// </summary>
/// <remarks>
/// The persisted form is a stable lowercase string rather than the enum's numeric value, so the
/// database stays readable and reordering the enum cannot silently reinterpret existing rows.
/// </remarks>
public static class EmailTokenPurposes
{
    /// <summary>The persisted code for <see cref="EmailTokenPurpose.VerifyEmail"/>.</summary>
    public const string VerifyEmailCode = "verify_email";

    /// <summary>The persisted code for <see cref="EmailTokenPurpose.ResetPassword"/>.</summary>
    public const string ResetPasswordCode = "reset_password";

    /// <summary>Converts a purpose to its persisted code.</summary>
    public static string ToStorageValue(this EmailTokenPurpose purpose) => purpose switch
    {
        EmailTokenPurpose.VerifyEmail => VerifyEmailCode,
        EmailTokenPurpose.ResetPassword => ResetPasswordCode,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown email token purpose."),
    };

    /// <summary>Parses a persisted code back to its purpose.</summary>
    public static EmailTokenPurpose FromStorageValue(string value) => value switch
    {
        VerifyEmailCode => EmailTokenPurpose.VerifyEmail,
        ResetPasswordCode => EmailTokenPurpose.ResetPassword,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown email token purpose code."),
    };
}
