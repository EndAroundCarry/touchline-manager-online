using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when a session was revoked from the session list (`F-07`).</summary>
public enum RevokeSessionOutcome
{
    /// <summary>The session was revoked.</summary>
    Revoked = 0,

    /// <summary>The session does not exist, is already revoked, or belongs to another account.</summary>
    NotFound = 1,

    /// <summary>The session being revoked is the one making the request.</summary>
    CurrentSession = 2,
}

/// <summary>
/// Lists the authenticated account's active sessions, marking the one that is asking (`F-07`).
/// </summary>
/// <remarks>
/// The current session is identified by hashing the refresh cookie presented with the request and matching
/// it against the stored token hash — the only client-visible identifier a session has. The IP and
/// user-agent hashes the server keeps for support are deliberately not returned: they are class C3 and a
/// manager lists sessions to revoke them, not to inspect fingerprints.
/// </remarks>
public sealed class ListSessions
{
    private readonly IClock _clock;
    private readonly IRefreshSessionRepository _sessions;
    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the query.</summary>
    public ListSessions(
        IClock clock,
        IRefreshSessionRepository sessions,
        ISecureTokenService secureTokens)
    {
        _clock = clock;
        _sessions = sessions;
        _secureTokens = secureTokens;
    }

    /// <summary>Lists the account's usable sessions.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="currentRefreshToken">The raw refresh cookie value, or null when it was not sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SessionsResponse> ExecuteAsync(
        Guid userId,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var sessions = await _sessions.FindActiveByUserIdAsync(userId, cancellationToken);
        var currentHash = CurrentHash(currentRefreshToken);

        // A revoked-but-unexpired row is not offered: it cannot refresh, so listing it would invite a
        // manager to revoke something already dead. `IsActive` is the same test the refresh path uses.
        var active = sessions
            .Where(session => session.IsActive(now))
            .Select(session => new ActiveSessionResponse(
                session.Id,
                session.IssuedAt,
                session.ExpiresAt,
                session.LastUsedAt,
                currentHash is not null && string.Equals(session.TokenHash, currentHash, StringComparison.Ordinal)))
            .ToList();

        return new SessionsResponse(active, now);
    }

    private string? CurrentHash(string? currentRefreshToken) =>
        string.IsNullOrEmpty(currentRefreshToken) ? null : _secureTokens.HashToken(currentRefreshToken);
}

/// <summary>
/// Revokes one of the account's sessions from the session list (`F-07`).
/// </summary>
/// <remarks>
/// The session making the request is refused: ending it is what sign-out means, and revoking it here would
/// leave the manager holding an access token for up to fifteen more minutes while the screen claimed the
/// session was gone. Every other session can be ended, so a manager who has lost a device can cut it off
/// without disturbing the one they are using.
/// </remarks>
public sealed class RevokeSession
{
    private readonly IClock _clock;
    private readonly IRefreshSessionRepository _sessions;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public RevokeSession(
        IClock clock,
        IRefreshSessionRepository sessions,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _sessions = sessions;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Revokes one session owned by the account.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="sessionId">The session to revoke.</param>
    /// <param name="currentRefreshToken">The raw refresh cookie value, or null when it was not sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RevokeSessionOutcome> ExecuteAsync(
        Guid userId,
        Guid sessionId,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (!string.IsNullOrEmpty(currentRefreshToken))
        {
            var current = await _sessions.FindByTokenHashAsync(
                _secureTokens.HashToken(currentRefreshToken),
                cancellationToken);

            if (current is not null && current.Id == sessionId)
            {
                return RevokeSessionOutcome.CurrentSession;
            }
        }

        var revoked = await _sessions.RevokeByIdAsync(
            sessionId,
            userId,
            RefreshSessionRevocationReasons.RevokedByUser,
            now,
            cancellationToken);

        if (!revoked)
        {
            return RevokeSessionOutcome.NotFound;
        }

        _audit.Record(new AuditEntry(
            AuthAuditActions.SessionRevoked,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.RefreshSession,
            sessionId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return RevokeSessionOutcome.Revoked;
    }
}
