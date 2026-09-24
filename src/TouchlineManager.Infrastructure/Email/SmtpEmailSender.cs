using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TouchlineManager.Application.Abstractions.Auth;

namespace TouchlineManager.Infrastructure.Email;

/// <summary>
/// Sends transactional email over SMTP.
/// </summary>
/// <remarks>
/// A fresh connection per message is deliberate. Transactional auth email is low volume and latency
/// is not on a deadline path, so the simplicity of not pooling and reconnecting SMTP — with its
/// attendant idle-timeout and stale-connection failure modes — is worth more than the connection
/// setup cost.
/// </remarks>
internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    /// <summary>Initializes the sender.</summary>
    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromDisplayName, _options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        using var client = new SmtpClient();

        var socketOptions = _options.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            await client.AuthenticateAsync(_options.UserName, _options.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
