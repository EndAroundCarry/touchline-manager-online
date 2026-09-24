using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Signs the account out of every session and invalidates every issued access token.
/// </summary>
/// <remarks>
/// Revoking the sessions is not sufficient on its own: an access token issued minutes ago is still
/// cryptographically valid. Bumping the security stamp is what makes the already-issued tokens stop
/// being accepted, because the API compares the token's stamp with the stored one on every request
/// (ADR-0002).
/// </remarks>
public sealed class LogoutAll
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IRefreshSessionRepository _sessions;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public LogoutAll(
        IClock clock,
        IUserRepository users,
        IRefreshSessionRepository sessions,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _sessions = sessions;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Revokes every session for the account.</summary>
    /// <returns><see langword="false"/> when the account no longer exists.</returns>
    public async Task<bool> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return false;
        }

        await _sessions.RevokeAllForUserAsync(
            userId,
            RefreshSessionRevocationReasons.LogoutAll,
            now,
            cancellationToken);

        user.RevokeAllSessions(_secureTokens.CreateSecurityStamp(), now);

        _audit.Record(new AuditEntry(
            AuthAuditActions.LoggedOutAll,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.User,
            userId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
