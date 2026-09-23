using System.Text.RegularExpressions;

namespace TouchlineManager.Infrastructure.Logging;

/// <summary>
/// Removes values that must never reach a log sink, trace, or error response.
/// </summary>
/// <remarks>
/// The redaction list is normative and lives in <c>docs/security/data-classification.md</c> §4. It
/// is implemented here as a filter with automated tests rather than left as a convention someone
/// has to remember. Both composition roots install it, so the worker cannot log what the API
/// redacts.
/// </remarks>
public static partial class LogRedactor
{
    /// <summary>What a redacted value is replaced with.</summary>
    public const string Mask = "[REDACTED]";

    /// <summary>
    /// Redacts secrets and personal data from a log message or error text.
    /// </summary>
    public static string Redact(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message ?? string.Empty;
        }

        var redacted = JsonWebToken().Replace(message, Mask);
        redacted = EmailAddress().Replace(redacted, Mask);
        redacted = InternetProtocolAddress().Replace(redacted, Mask);
        redacted = NamedSecret().Replace(redacted, $"$1$2{Mask}");

        return redacted;
    }

    /// <summary>Bearer tokens and JWTs, which have a recognizable three-part shape.</summary>
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\b")]
    private static partial Regex JsonWebToken();

    /// <summary>Email addresses. Log the user id or a hash instead.</summary>
    [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailAddress();

    /// <summary>Raw IPv4 addresses. Store and log a prefix hash instead.</summary>
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex InternetProtocolAddress();

    /// <summary>
    /// Secrets written as a key/value pair, which also covers connection-string passwords such as
    /// <c>Password=...</c> because the captured value stops at a delimiter.
    /// </summary>
    /// <remarks>
    /// The key is matched as any identifier <em>containing</em> a sensitive word, so
    /// <c>refresh_token</c>, <c>accessToken</c>, and <c>Api_Key</c> are all caught. A plain
    /// <c>\btoken\b</c> would miss them, because an underscore is a word character and therefore
    /// provides no word boundary.
    /// </remarks>
    [GeneratedRegex(
        @"(?i)\b([A-Za-z0-9_\-]*(?:password|passwd|pwd|secret|token|api[_-]?key|apikey|authorization|security[_-]?stamp)[A-Za-z0-9_\-]*)(\s*[:=]\s*)(""[^""]*""|[^\s,;]+)")]
    private static partial Regex NamedSecret();
}
