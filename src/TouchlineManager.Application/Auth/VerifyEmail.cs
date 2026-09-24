using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when an email address was confirmed.</summary>
public enum VerifyEmailOutcome
{
    /// <summary>The address is verified.</summary>
    Verified = 0,

    /// <summary>The token was unknown, expired, or already used.</summary>
    InvalidToken = 1,
}

/// <summary>Confirms ownership of an email address and activates the account.</summary>
public sealed class VerifyEmail
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IEmailTokenRepository _emailTokens;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public VerifyEmail(
        IClock clock,
        IUserRepository users,
        IEmailTokenRepository emailTokens,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _emailTokens = emailTokens;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Verifies the address.</summary>
    public async Task<VerifyEmailOutcome> ExecuteAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var ipHash = _secureTokens.HashClientValue(_requestContext.IpAddress);

        var token = await _emailTokens.FindByTokenHashAsync(
            EmailTokenPurpose.VerifyEmail,
            _secureTokens.HashToken(request.Token),
            cancellationToken);

        // The token must exist, be usable, and belong to the account named in the request. Checking
        // the owner means a token cannot be replayed against a different account.
        if (token is null || token.UserId != request.UserId || !token.IsUsable(now))
        {
            return VerifyEmailOutcome.InvalidToken;
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);

        if (user is null || UserStatusRules.IsTerminal(user.Status))
        {
            return VerifyEmailOutcome.InvalidToken;
        }

        token.Consume(now);
        user.MarkEmailVerified(now);

        _audit.Record(new AuditEntry(
            AuthAuditActions.EmailVerified,
            AuditActorTypes.Anonymous,
            user.Id,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            ipHash,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return VerifyEmailOutcome.Verified;
    }
}
