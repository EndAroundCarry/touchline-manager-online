using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>What happened when multi-factor authentication was turned off.</summary>
public enum MfaDisableOutcome
{
    /// <summary>The credential was removed.</summary>
    Disabled = 0,

    /// <summary>The account holds a role for which a second factor is mandatory, so it cannot be removed.</summary>
    RequiredForRole = 1,

    /// <summary>The code or recovery code did not match.</summary>
    InvalidCode = 2,

    /// <summary>The account has no credential to remove.</summary>
    NotEnrolled = 3,

    /// <summary>No account exists for that identity.</summary>
    AccountNotFound = 4,
}

/// <summary>
/// Turns off multi-factor authentication for an account that is allowed to turn it off (ADR-0042).
/// </summary>
/// <remarks>
/// An account holding <c>support</c>, <c>operator</c>, or <c>admin</c> cannot disable its own factor: the
/// requirement is a property of the role, so the recovery path is an operator resetting it, not the account
/// removing it.
/// </remarks>
public sealed class DisableMfa
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IMfaCredentialRepository _credentials;
    private readonly MfaAuthenticator _authenticator;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public DisableMfa(
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

    /// <summary>Disables the account's second factor.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="code">A current code or an unused recovery code, proving possession.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MfaDisableOutcome> ExecuteAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return MfaDisableOutcome.AccountNotFound;
        }

        if (UserRoles.RequiresSecondFactor(user.RoleNames()))
        {
            return MfaDisableOutcome.RequiredForRole;
        }

        var credential = await _credentials.FindByUserIdAsync(userId, cancellationToken);

        if (credential is null)
        {
            return MfaDisableOutcome.NotEnrolled;
        }

        var now = _clock.UtcNow;

        if (!_authenticator.VerifyCode(credential, code, now)
            && !_authenticator.TryConsumeRecoveryCode(credential, code, now))
        {
            return MfaDisableOutcome.InvalidCode;
        }

        _credentials.Remove(credential);

        _audit.Record(new AuditEntry(
            AdminAuditActions.MfaDisabled,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.MfaCredential,
            userId,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MfaDisableOutcome.Disabled;
    }
}
