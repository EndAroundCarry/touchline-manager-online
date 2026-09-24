using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>A newly issued session, ready to be returned to the client.</summary>
/// <param name="Response">The body sent to the client. Never contains the refresh token.</param>
/// <param name="RefreshSessionId">The stored session identity, used when rotating.</param>
/// <param name="RefreshToken">The raw refresh token, placed only in the HttpOnly cookie.</param>
/// <param name="RefreshTokenExpiresAt">When the refresh session expires.</param>
public sealed record IssuedSession(
    AuthSessionResponse Response,
    Guid RefreshSessionId,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// Creates a refresh session and the matching access token.
/// </summary>
/// <remarks>
/// Shared by login and refresh so the two paths cannot drift: the cookie lifetime, the token family,
/// the recorded client fingerprint, and the access-token claims are decided in one place.
/// </remarks>
public sealed class SessionIssuer
{
    private readonly IRefreshSessionRepository _sessions;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAccessTokenIssuer _accessTokens;
    private readonly IRequestContext _requestContext;
    private readonly AuthOptions _options;

    /// <summary>Initializes the issuer.</summary>
    public SessionIssuer(
        IRefreshSessionRepository sessions,
        ISecureTokenService secureTokens,
        IAccessTokenIssuer accessTokens,
        IRequestContext requestContext,
        IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sessions = sessions;
        _secureTokens = secureTokens;
        _accessTokens = accessTokens;
        _requestContext = requestContext;
        _options = options.Value;
    }

    /// <summary>Issues a session within a token family, staging it for the next save.</summary>
    /// <param name="user">The authenticated account.</param>
    /// <param name="familyId">The token family. A login starts one; a rotation continues one.</param>
    /// <param name="sessionId">
    /// The identity of the new session. The caller chooses it, because rotation must name its
    /// replacement before the old row is updated.
    /// </param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IssuedSession> IssueAsync(
        User user,
        Guid familyId,
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        cancellationToken.ThrowIfCancellationRequested();

        var rawToken = _secureTokens.CreateToken();
        var expiresAt = now.Add(_options.RefreshTokenLifetime);
        var roles = user.RoleNames();

        _sessions.Add(RefreshSession.Issue(
            sessionId,
            user.Id,
            _secureTokens.HashToken(rawToken),
            familyId,
            now,
            expiresAt,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            _secureTokens.HashClientValue(_requestContext.UserAgent)));

        var accessToken = _accessTokens.Issue(user, roles, now);

        var response = new AuthSessionResponse(
            accessToken.Token,
            accessToken.ExpiresAt,
            now,
            UserProfileMapper.ToProfile(user));

        return Task.FromResult(new IssuedSession(response, sessionId, rawToken, expiresAt));
    }
}
