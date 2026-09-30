using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when a multi-factor login was completed.</summary>
public enum MfaLoginOutcome
{
    /// <summary>The second factor was accepted and a session was issued.</summary>
    Succeeded = 0,

    /// <summary>The challenge token was unknown, expired, or not a challenge.</summary>
    InvalidChallenge = 1,

    /// <summary>The account may no longer authenticate, or the challenge was issued for a stale session.</summary>
    AccountUnavailable = 2,

    /// <summary>The account has no confirmed second factor.</summary>
    NotEnrolled = 3,

    /// <summary>The code or recovery code did not match.</summary>
    InvalidCode = 4,
}

/// <summary>The result of completing a multi-factor login.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Session">The issued session, when the outcome is <see cref="MfaLoginOutcome.Succeeded"/>.</param>
public sealed record MfaLoginResult(MfaLoginOutcome Outcome, IssuedSession? Session);

/// <summary>
/// Completes a two-step login: validates the challenge and the code, then issues the session (ADR-0042).
/// </summary>
/// <remarks>
/// The session this issues is the only one that carries the second factor, and the challenge token is
/// re-checked against the account's current security stamp so a suspension, password reset, or
/// sign-out-everywhere between the two steps still wins.
/// </remarks>
public sealed class CompleteMfaLogin
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IMfaCredentialRepository _credentials;
    private readonly IMfaChallengeIssuer _challenges;
    private readonly MfaAuthenticator _authenticator;
    private readonly SessionIssuer _sessionIssuer;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public CompleteMfaLogin(
        IClock clock,
        IUserRepository users,
        IMfaCredentialRepository credentials,
        IMfaChallengeIssuer challenges,
        MfaAuthenticator authenticator,
        SessionIssuer sessionIssuer,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _credentials = credentials;
        _challenges = challenges;
        _authenticator = authenticator;
        _sessionIssuer = sessionIssuer;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Completes the login.</summary>
    /// <param name="challengeToken">The challenge issued by the password step.</param>
    /// <param name="code">A current code or an unused recovery code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MfaLoginResult> ExecuteAsync(
        string challengeToken,
        string code,
        CancellationToken cancellationToken)
    {
        var identity = await _challenges.ValidateAsync(challengeToken, cancellationToken);

        if (identity is null)
        {
            return new MfaLoginResult(MfaLoginOutcome.InvalidChallenge, Session: null);
        }

        var user = await _users.FindByIdAsync(identity.UserId, cancellationToken);

        if (user is null
            || !UserStatusRules.CanAuthenticate(user.Status)
            || !string.Equals(user.SecurityStamp, identity.SecurityStamp, StringComparison.Ordinal))
        {
            return new MfaLoginResult(MfaLoginOutcome.AccountUnavailable, Session: null);
        }

        var credential = await _credentials.FindByUserIdAsync(user.Id, cancellationToken);

        if (credential is null || !credential.IsConfirmed)
        {
            return new MfaLoginResult(MfaLoginOutcome.NotEnrolled, Session: null);
        }

        var now = _clock.UtcNow;

        if (!_authenticator.VerifyCode(credential, code, now)
            && !_authenticator.TryConsumeRecoveryCode(credential, code, now))
        {
            return new MfaLoginResult(MfaLoginOutcome.InvalidCode, Session: null);
        }

        var session = await _sessionIssuer.IssueAsync(
            user,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            now,
            mfaCompletedAt: now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.LoginSucceeded,
            AuditActorTypes.User,
            user.Id,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new MfaLoginResult(MfaLoginOutcome.Succeeded, session);
    }
}
