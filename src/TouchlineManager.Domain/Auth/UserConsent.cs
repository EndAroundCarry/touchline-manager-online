namespace TouchlineManager.Domain.Auth;

/// <summary>A document whose acceptance is recorded for consent (<c>auth.user_consents</c>).</summary>
public static class ConsentDocumentTypes
{
    /// <summary>The terms of service.</summary>
    public const string Terms = "terms";

    /// <summary>The privacy policy.</summary>
    public const string Privacy = "privacy";

    /// <summary>Every document type the product records consent for.</summary>
    public static IReadOnlyList<string> All { get; } = [Terms, Privacy];
}

/// <summary>
/// A record that an account accepted a particular version of a legal document.
/// </summary>
/// <remarks>
/// Consent rows are append-only evidence (master plan §12.4): accepting a new version adds a row
/// rather than updating the old one, so the version accepted at any past instant is recoverable.
/// </remarks>
public sealed class UserConsent
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private UserConsent()
    {
    }

    /// <summary>Gets the consent identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the account that accepted the document.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the document type. See <see cref="ConsentDocumentTypes"/>.</summary>
    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>Gets the accepted document version.</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>Gets when the document was accepted.</summary>
    public DateTimeOffset AcceptedAt { get; private set; }

    /// <summary>Gets the hashed client IP, or <see langword="null"/> when unavailable.</summary>
    public string? IpHash { get; private set; }

    /// <summary>Records an acceptance.</summary>
    public static UserConsent Record(
        Guid id,
        Guid userId,
        string documentType,
        string version,
        DateTimeOffset now,
        string? ipHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (!ConsentDocumentTypes.All.Contains(documentType, StringComparer.Ordinal))
        {
            throw new ArgumentException($"'{documentType}' is not a known consent document.", nameof(documentType));
        }

        return new UserConsent
        {
            Id = id,
            UserId = userId,
            DocumentType = documentType,
            Version = version,
            AcceptedAt = now,
            IpHash = ipHash,
        };
    }
}
