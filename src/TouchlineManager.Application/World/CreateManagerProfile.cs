using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when a manager profile was requested.</summary>
public enum CreateManagerProfileOutcome
{
    /// <summary>The profile was created.</summary>
    Created = 0,

    /// <summary>The account already has a profile. One profile per account.</summary>
    ProfileExists = 1,

    /// <summary>The account is suspended, closing, or otherwise not able to write.</summary>
    AccountUnavailable = 2,
}

/// <summary>The result of a manager-profile request.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Profile">The profile, when one exists — including on an idempotent repeat.</param>
public sealed record CreateManagerProfileResult(
    CreateManagerProfileOutcome Outcome,
    ManagerProfileResponse? Profile);

/// <summary>
/// Creates the account's manager profile (`WORLD-7`, master plan §10.2).
/// </summary>
/// <remarks>
/// The game-facing identity of an account, created once and deliberately separate from the account
/// itself: the auth module owns credentials and knows nothing about clubs. A second call returns the
/// existing profile rather than failing, so a client that lost the response to a flaky connection does
/// not have to reconcile a conflict it cannot resolve.
/// </remarks>
public sealed class CreateManagerProfile
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IManagerRepository _managers;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public CreateManagerProfile(
        IClock clock,
        IUserRepository users,
        IManagerRepository managers,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _managers = managers;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Creates the profile for an account.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="request">The requested locale and time zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CreateManagerProfileResult> ExecuteAsync(
        Guid userId,
        CreateManagerProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null || !UserStatusRules.CanWrite(user.Status))
        {
            return new CreateManagerProfileResult(CreateManagerProfileOutcome.AccountUnavailable, null);
        }

        var existing = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (existing is not null)
        {
            return new CreateManagerProfileResult(
                CreateManagerProfileOutcome.ProfileExists,
                existing.ToResponse());
        }

        var now = _clock.UtcNow;
        var manager = Manager.Create(Guid.CreateVersion7(), userId, request.Locale, request.TimeZone, now);

        _managers.Add(manager);

        _audit.Record(new AuditEntry(
            WorldAuditActions.ManagerProfileCreated,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.Manager,
            manager.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateManagerProfileResult(CreateManagerProfileOutcome.Created, manager.ToResponse());
    }
}
