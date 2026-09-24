namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>A transactional email to dispatch.</summary>
/// <param name="To">The recipient address.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="TextBody">The plain-text body. Always present.</param>
/// <param name="HtmlBody">The HTML body.</param>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>
/// The transactional email provider abstraction (master plan §4.3).
/// </summary>
/// <remarks>
/// Local development and CI send to a mail catcher; production uses a transactional provider. Tests
/// substitute an in-memory recorder, so no test needs an SMTP server.
/// </remarks>
public interface IEmailSender
{
    /// <summary>Sends an email. Implementations throw on transport failure; callers decide whether that is fatal.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
