namespace TouchlineManager.Infrastructure.Email;

/// <summary>
/// Binds the <c>Email</c> configuration section (master plan §4.3).
/// </summary>
/// <remarks>
/// Local development and CI point this at the mail catcher from <c>infra/compose.yaml</c>; production
/// points it at a transactional provider. Credentials come from secret storage, never from source
/// control.
/// </remarks>
public sealed class EmailOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Email";

    /// <summary>Gets or sets the SMTP host.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Gets or sets the SMTP port.</summary>
    public int Port { get; set; } = 1025;

    /// <summary>Gets or sets a value indicating whether the connection must be upgraded with STARTTLS.</summary>
    public bool UseStartTls { get; set; }

    /// <summary>Gets or sets the envelope sender address.</summary>
    public string FromAddress { get; set; } = "no-reply@touchline.local";

    /// <summary>Gets or sets the sender display name.</summary>
    public string FromDisplayName { get; set; } = "Touchline Manager";

    /// <summary>Gets or sets the SMTP user name, when the server requires authentication.</summary>
    public string? UserName { get; set; }

    /// <summary>Gets or sets the SMTP password, when the server requires authentication.</summary>
    public string? Password { get; set; }
}
