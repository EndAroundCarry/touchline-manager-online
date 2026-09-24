namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>
/// Binds the <c>Auth</c> configuration section (ADR-0002).
/// </summary>
/// <remarks>
/// The lifetimes here are the ones the ADR fixes: a fifteen-minute access token and a rotation on
/// every refresh. The work factor of the password hasher is configured separately on the hasher.
/// </remarks>
public sealed class AuthOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Auth";

    /// <summary>Gets or sets how long an access token remains valid. Kept short; it cannot be revoked.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets how long a refresh session remains valid without rotation.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Gets or sets how long a verification link remains valid.</summary>
    public TimeSpan EmailVerificationLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Gets or sets how long a password-reset link remains valid.</summary>
    public TimeSpan PasswordResetLifetime { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>Gets or sets how long an account stays accessible-but-closing before anonymization.</summary>
    public TimeSpan DeletionCoolingPeriod { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Gets or sets the HMAC signing key for access tokens. Must be at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the token issuer.</summary>
    public string Issuer { get; set; } = "touchline-manager";

    /// <summary>Gets or sets the token audience.</summary>
    public string Audience { get; set; } = "touchline-manager-web";

    /// <summary>Gets or sets the web client's base URL, used to build links in emails.</summary>
    public string ClientBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Gets or sets the name of the HttpOnly refresh cookie.</summary>
    public string RefreshCookieName { get; set; } = "touchline_refresh";

    /// <summary>Gets or sets the path the refresh cookie is scoped to.</summary>
    public string RefreshCookiePath { get; set; } = "/api/v1/auth";

    /// <summary>Gets or sets the current terms-of-service version recorded at registration.</summary>
    public string TermsVersion { get; set; } = "2026-01-01";

    /// <summary>Gets or sets the current privacy-policy version recorded at registration.</summary>
    public string PrivacyVersion { get; set; } = "2026-01-01";
}
