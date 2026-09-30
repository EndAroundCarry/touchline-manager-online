using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when enrolment was confirmed.</summary>
public enum MfaConfirmOutcome
{
    /// <summary>The credential is now confirmed and the second factor is in force.</summary>
    Confirmed = 0,

    /// <summary>The code did not match the pending secret.</summary>
    InvalidCode = 1,

    /// <summary>The account has not started enrolling.</summary>
    NotEnrolled = 2,

    /// <summary>No account exists for that identity.</summary>
    AccountNotFound = 3,
}

/// <summary>
/// Confirms a pending multi-factor enrolment with the first valid code (ADR-0042).
/// </summary>
public sealed class ConfirmMfaEnrolment
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IMfaCredentialRepository _credentials;
    private readonly MfaAuthenticator _authenticator;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public ConfirmMfaEnrolment(
        IClock clock,
        IUserRepository users,
        IMfaCredentialRepository credentials,
        MfaAuthenticator authenticator,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _credentials = credentials;
        _authenticator = authenticator;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Confirms the account's pending credential.</summary>
    /// <param name="userId">The account confirming.</param>
    /// <param name="code">The code derived from the pending secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MfaConfirmOutcome> ExecuteAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return MfaConfirmOutcome.AccountNotFound;
        }

        var credential = await _credentials.FindByUserIdAsync(userId, cancellationToken);

        if (credential is null)
        {
            return MfaConfirmOutcome.NotEnrolled;
        }

        if (credential.IsConfirmed)
        {
            return MfaConfirmOutcome.Confirmed;
        }

        var now = _clock.UtcNow;

        if (!_authenticator.VerifyCode(credential, code, now))
        {
            return MfaConfirmOutcome.InvalidCode;
        }

        credential.Confirm(now);

        _audit.Record(new AuditEntry(
            AdminAuditActions.MfaEnabled,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.MfaCredential,
            userId,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MfaConfirmOutcome.Confirmed;
    }
}
