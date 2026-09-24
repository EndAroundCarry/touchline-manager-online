using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when a password reset was completed.</summary>
public enum ResetPasswordOutcome
{
    /// <summary>The password was changed and every existing session was revoked.</summary>
    Reset = 0,

    /// <summary>The token was unknown, expired, already used, or belonged to a different account.</summary>
    InvalidToken = 1,

    /// <summary>The account may not authenticate, so the reset is refused.</summary>
    AccountUnavailable = 2,
}

/// <summary>
/// Completes a password reset.
/// </summary>
/// <remarks>
/// A successful reset revokes every session and bumps the security stamp: if the reset was triggered
/// because the account was compromised, the attacker's session must die with the old password
/// (ADR-0002).
/// </remarks>
public sealed class ResetPassword
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IEmailTokenRepository _emailTokens;
    private readonly IRefreshSessionRepository _sessions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public ResetPassword(
        IClock clock,
        IUserRepository users,
        IEmailTokenRepository emailTokens,
        IRefreshSessionRepository sessions,
        IPasswordHasher passwordHasher,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _emailTokens = emailTokens;
        _sessions = sessions;
        _passwordHasher = passwordHasher;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Completes the reset.</summary>
    public async Task<ResetPasswordOutcome> ExecuteAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;

        var token = await _emailTokens.FindByTokenHashAsync(
            EmailTokenPurpose.ResetPassword,
            _secureTokens.HashToken(request.Token),
            cancellationToken);

        if (token is null || token.UserId != request.UserId || !token.IsUsable(now))
        {
            return ResetPasswordOutcome.InvalidToken;
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);

        if (user is null || !UserStatusRules.CanAuthenticate(user.Status))
        {
            return ResetPasswordOutcome.AccountUnavailable;
        }

        token.Consume(now);
        user.ChangePassword(
            _passwordHasher.Hash(request.NewPassword),
            _secureTokens.CreateSecurityStamp(),
            now);

        await _sessions.RevokeAllForUserAsync(
            user.Id,
            RefreshSessionRevocationReasons.PasswordReset,
            now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.PasswordReset,
            AuditActorTypes.Anonymous,
            user.Id,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ResetPasswordOutcome.Reset;
    }
}
