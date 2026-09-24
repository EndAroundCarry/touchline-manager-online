using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when a login was attempted.</summary>
public enum LoginOutcome
{
    /// <summary>Credentials were accepted and a session was issued.</summary>
    Succeeded = 0,

    /// <summary>The email or password was wrong.</summary>
    InvalidCredentials = 1,

    /// <summary>The account is temporarily locked after repeated failures.</summary>
    AccountLocked = 2,

    /// <summary>The account is suspended.</summary>
    AccountSuspended = 3,

    /// <summary>The account is closing or already anonymized.</summary>
    AccountDeleted = 4,
}

/// <summary>The result of a login attempt.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Session">The issued session when the outcome is <see cref="LoginOutcome.Succeeded"/>.</param>
/// <param name="LockedUntil">When the lockout expires, when one is in force.</param>
public sealed record LoginResult(LoginOutcome Outcome, IssuedSession? Session, DateTimeOffset? LockedUntil);

/// <summary>
/// Authenticates an account and issues a session.
/// </summary>
/// <remarks>
/// <para>
/// Password verification happens before any status decision, so the response cannot be used to
/// discover whether an address is registered. An unknown address still performs a throwaway hash
/// comparison, because otherwise response time alone would answer that question (ADR-0002).
/// </para>
/// <para>
/// A successful login with a stale password hash transparently rehashes it, which is how the work
/// factor is raised over time without a mass reset.
/// </para>
/// </remarks>
public sealed class Login
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SessionIssuer _sessionIssuer;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public Login(
        IClock clock,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        SessionIssuer sessionIssuer,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _passwordHasher = passwordHasher;
        _sessionIssuer = sessionIssuer;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Authenticates the account.</summary>
    public async Task<LoginResult> ExecuteAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var ipHash = _secureTokens.HashClientValue(_requestContext.IpAddress);
        var user = await _users.FindByNormalizedEmailAsync(User.NormalizeEmail(request.Email), cancellationToken);

        if (user is null)
        {
            // Burn comparable time, then answer exactly as a wrong password would.
            _passwordHasher.PerformTimingEqualization(request.Password);
            RecordFailure(ipHash, actorUserId: null, "unknown_account");

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new LoginResult(LoginOutcome.InvalidCredentials, Session: null, LockedUntil: null);
        }

        if (user.IsLockedOut(now))
        {
            RecordFailure(ipHash, user.Id, "locked_out");

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new LoginResult(LoginOutcome.AccountLocked, Session: null, user.LockoutUntil);
        }

        var verification = _passwordHasher.Verify(user.PasswordHash, request.Password);

        if (verification == PasswordVerificationOutcome.Failed)
        {
            user.RecordFailedLogin(now);
            RecordFailure(ipHash, user.Id, "bad_password");

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new LoginResult(LoginOutcome.InvalidCredentials, Session: null, null);
        }

        if (!UserStatusRules.CanAuthenticate(user.Status))
        {
            var outcome = user.Status == UserStatus.Suspended
                ? LoginOutcome.AccountSuspended
                : LoginOutcome.AccountDeleted;

            RecordFailure(ipHash, user.Id, user.Status.ToCode());

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new LoginResult(outcome, Session: null, LockedUntil: null);
        }

        if (verification == PasswordVerificationOutcome.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(_passwordHasher.Hash(request.Password), now);
        }

        user.RecordSuccessfulLogin(now);

        var session = await _sessionIssuer.IssueAsync(
            user,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            now,
            cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.LoginSucceeded,
            AuditActorTypes.User,
            user.Id,
            AuditTargetTypes.User,
            user.Id,
            _requestContext.CorrelationId,
            ipHash,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResult(LoginOutcome.Succeeded, session, LockedUntil: null);
    }

    private void RecordFailure(string? ipHash, Guid? actorUserId, string reason)
    {
        _audit.Record(new AuditEntry(
            AuthAuditActions.LoginFailed,
            actorUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.Anonymous,
            actorUserId,
            AuditTargetTypes.User,
            actorUserId,
            _requestContext.CorrelationId,
            ipHash,
            reason));
    }
}
