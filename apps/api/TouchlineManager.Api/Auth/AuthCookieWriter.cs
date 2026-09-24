using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions.Auth;

namespace TouchlineManager.Api.Auth;

/// <summary>
/// Reads and writes the HttpOnly refresh cookie (ADR-0002).
/// </summary>
/// <remarks>
/// <para>
/// The refresh token exists in exactly two places: this cookie and a hash in the database. It is never
/// in a response body, never in JavaScript's reach, and never in a log.
/// </para>
/// <para>
/// <c>Secure</c> is relaxed in Development only, because the local client is served over plain HTTP
/// and a Secure cookie would simply never arrive — which would look like a broken login rather than a
/// configuration choice.
/// </para>
/// </remarks>
internal sealed class AuthCookieWriter
{
    private readonly AuthOptions _options;
    private readonly bool _secure;

    /// <summary>Initializes the writer.</summary>
    public AuthCookieWriter(IOptions<AuthOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        _options = options.Value;
        _secure = !environment.IsDevelopment();
    }

    /// <summary>Reads the refresh token from the request, if present.</summary>
    public string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies.TryGetValue(_options.RefreshCookieName, out var token) ? token : null;
    }

    /// <summary>Writes a rotated refresh token.</summary>
    public void Write(HttpResponse response, string refreshToken, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(_options.RefreshCookieName, refreshToken, Build(expiresAt));
    }

    /// <summary>Removes the refresh cookie.</summary>
    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(_options.RefreshCookieName, Build(DateTimeOffset.UnixEpoch));
    }

    private CookieOptions Build(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = _secure,
        SameSite = SameSiteMode.Lax,
        Path = _options.RefreshCookiePath,
        Expires = expiresAt,
        IsEssential = true,
    };
}
