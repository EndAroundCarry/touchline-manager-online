using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when a session was refreshed.</summary>
public enum RefreshAccessTokenOutcome
{
    /// <summary>A new access token and rotated refresh token were issued.</summary>
    Refreshed = 0,

    /// <summary>The token was unknown, expired, revoked, or replayed. The client must sign in again.</summary>
    SessionInvalid = 1,

    /// <summary>The token was valid but the account may no longer authenticate.</summary>
    AccountUnavailable = 2,
}

/// <summary>The result of a refresh attempt.</summary>
public sealed record RefreshAccessTokenResult(RefreshAccessTokenOutcome Outcome, IssuedSession? Session);

/// <summary>
/// Rotates a refresh session and issues a new access token (ADR-0002).
/// </summary>
/// <remarks>
/// <para>
/// Rotation is mandatory: every refresh consumes the presented token and issues a replacement in the
/// same family. Presenting a token that has already been rotated means the token leaked, so the
/// entire family is revoked rather than just that session.
/// </para>
/// <para>
/// A crash between rotating the stored session and persisting its replacement signs the manager out
/// rather than leaving a half-issued session, which is the safe direction to fail.
/// </para>
/// </remarks>
public sealed partial class RefreshAccessToken
{
    private readonly IClock _clock;
    private readonly IRefreshSessionRepository _sessions;
    private readonly IUserRepository _users;
    private readonly ISecureTokenService _secureTokens;
    private readonly SessionIssuer _sessionIssuer;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RefreshAccessToken> _logger;

    /// <summary>Initializes the use case.</summary>
    public RefreshAccessToken(
        IClock clock,
        IRefreshSessionRepository sessions,
        IUserRepository users,
        ISecureTokenService secureTokens,
        SessionIssuer sessionIssuer,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        ILogger<RefreshAccessToken> logger)
    {
        _clock = clock;
        _sessions = sessions;
        _users = users;
        _secureTokens = secureTokens;
        _sessionIssuer = sessionIssuer;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Refreshes the session presented in the refresh cookie.</summary>
    /// <param name="refreshToken">The raw refresh token from the cookie.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RefreshAccessTokenResult> ExecuteAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new RefreshAccessTokenResult(RefreshAccessTokenOutcome.SessionInvalid, Session: null);
        }

        var now = _clock.UtcNow;
        var session = await _sessions.FindByTokenHashAsync(_secureTokens.HashToken(refreshToken), cancellationToken);

        if (session is null)
        {
            return new RefreshAccessTokenResult(RefreshAccessTokenOutcome.SessionInvalid, Session: null);
        }

        if (!session.IsActive(now))
        {
            return await HandleInactiveSessionAsync(session, now, cancellationToken);
        }

        var user = await _users.FindByIdAsync(session.UserId, cancellationToken);

        if (user is null || !UserStatusRules.CanAuthenticate(user.Status))
        {
            session.Revoke(RefreshSessionRevocationReasons.AccountSuspended, now);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new RefreshAccessTokenResult(RefreshAccessTokenOutcome.AccountUnavailable, Session: null);
        }

        var replacementSessionId = Guid.CreateVersion7();

        var rotated = await _sessions.TryRotateAsync(
            session.Id,
            session.Version,
            replacementSessionId,
            now,
            cancellationToken);

        if (!rotated)
        {
            // Another request already consumed this token. Reaching here with a token that was active
            // moments ago is the replay signature, so the family is treated as compromised.
            return await RevokeFamilyAsReuseAsync(session, now, cancellationToken);
        }

        var issued = await _sessionIssuer.IssueAsync(
            user,
            session.FamilyId,
            replacementSessionId,
            now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.SessionRefreshed,
            AuditActorTypes.User,
            user.Id,
            AuditTargetTypes.RefreshSession,
            replacementSessionId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RefreshAccessTokenResult(RefreshAccessTokenOutcome.Refreshed, issued);
    }

    private async Task<RefreshAccessTokenResult> HandleInactiveSessionAsync(
        RefreshSession session,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A session that was revoked *because it was rotated* and is presented again is a replayed
        // token, not a stale one: the legitimate holder already moved on. Revoking the family
        // contains the theft (ADR-0002).
        if (session.RevocationReason == RefreshSessionRevocationReasons.Rotated)
        {
            return await RevokeFamilyAsReuseAsync(session, now, cancellationToken);
        }

        return new RefreshAccessTokenResult(RefreshAccessTokenOutcome.SessionInvalid, Session: null);
    }

    private async Task<RefreshAccessTokenResult> RevokeFamilyAsReuseAsync(
        RefreshSession session,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _sessions.RevokeFamilyAsync(
            session.FamilyId,
            RefreshSessionRevocationReasons.ReuseDetected,
            now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.RefreshReuseDetected,
            AuditActorTypes.Anonymous,
            session.UserId,
            AuditTargetTypes.RefreshSession,
            session.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            "rotated token replayed"));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogReuseDetected(session.Id, session.FamilyId);

        return new RefreshAccessTokenResult(RefreshAccessTokenOutcome.SessionInvalid, Session: null);
    }

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Warning,
        Message = "Refresh token reuse detected for session {SessionId}; family {FamilyId} revoked.")]
    private partial void LogReuseDetected(Guid sessionId, Guid familyId);
}
