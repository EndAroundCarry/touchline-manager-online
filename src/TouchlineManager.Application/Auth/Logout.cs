using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Revokes the session presented in the refresh cookie.
/// </summary>
/// <remarks>
/// Signing out is idempotent and never reports an error for an unknown or already-revoked token: the
/// desired end state — that token no longer works — is true either way, and reporting otherwise would
/// leak whether a token was valid.
/// </remarks>
public sealed class Logout
{
    private readonly IClock _clock;
    private readonly IRefreshSessionRepository _sessions;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public Logout(
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

    /// <summary>Revokes the presented session.</summary>
    public async Task ExecuteAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var now = _clock.UtcNow;
        var session = await _sessions.FindByTokenHashAsync(_secureTokens.HashToken(refreshToken), cancellationToken);

        if (session is null)
        {
            return;
        }

        session.Revoke(RefreshSessionRevocationReasons.Logout, now);

        _audit.Record(new AuditEntry(
            AuthAuditActions.LoggedOut,
            AuditActorTypes.User,
            session.UserId,
            AuditTargetTypes.RefreshSession,
            session.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
