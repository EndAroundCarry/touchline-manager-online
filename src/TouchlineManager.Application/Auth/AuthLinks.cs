namespace TouchlineManager.Application.Auth;

/// <summary>
/// Builds the links that appear in transactional email.
/// </summary>
/// <remarks>
/// The link points at the web client, not the API: the client collects the token and posts it, which
/// keeps the token out of the API's own access logs and referrers. The account id travels alongside
/// the token so the client never has to guess which account a reset belongs to.
/// </remarks>
public static class AuthLinks
{
    /// <summary>Builds the email-verification link.</summary>
    public static string VerifyEmail(string clientBaseUrl, Guid userId, string token) =>
        Build(clientBaseUrl, "verify-email", userId, token);

    /// <summary>Builds the password-reset link.</summary>
    public static string ResetPassword(string clientBaseUrl, Guid userId, string token) =>
        Build(clientBaseUrl, "reset-password", userId, token);

    private static string Build(string clientBaseUrl, string path, Guid userId, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var baseUrl = clientBaseUrl.TrimEnd('/');

        return $"{baseUrl}/{path}?userId={userId}&token={Uri.EscapeDataString(token)}";
    }
}
