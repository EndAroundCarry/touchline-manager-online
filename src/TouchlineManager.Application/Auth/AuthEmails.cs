using TouchlineManager.Application.Abstractions.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// The transactional emails the auth module sends.
/// </summary>
/// <remarks>
/// Bodies are plain text plus a minimal HTML alternative. They are never localized in the database
/// — the interface and content languages are an English-first product decision
/// (content-and-fictional-data policy) — and they always include the text link so a client that
/// strips HTML still works.
/// </remarks>
public static class AuthEmails
{
    /// <summary>Builds the email-verification message.</summary>
    public static EmailMessage Verification(string to, string link) => new(
        to,
        "Confirm your Touchline Manager account",
        $"""
         Welcome to Touchline Manager.

         Confirm this email address to activate your account:
         {link}

         The link expires in 24 hours. If you did not create an account, ignore this email.
         """,
        $"""
         <p>Welcome to Touchline Manager.</p>
         <p>Confirm this email address to activate your account:</p>
         <p><a href="{link}">Confirm my email address</a></p>
         <p>The link expires in 24 hours. If you did not create an account, ignore this email.</p>
         """);

    /// <summary>Builds the password-reset message.</summary>
    public static EmailMessage PasswordReset(string to, string link) => new(
        to,
        "Reset your Touchline Manager password",
        $"""
         A password reset was requested for this account.

         Choose a new password:
         {link}

         The link expires in 60 minutes and can be used once. If you did not request this, ignore
         this email; your password has not changed.
         """,
        $"""
         <p>A password reset was requested for this account.</p>
         <p>Choose a new password:</p>
         <p><a href="{link}">Choose a new password</a></p>
         <p>The link expires in 60 minutes and can be used once. If you did not request this, ignore
         this email; your password has not changed.</p>
         """);
}
