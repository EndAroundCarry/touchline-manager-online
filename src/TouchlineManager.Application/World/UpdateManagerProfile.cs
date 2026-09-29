using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when the manager's preferences were changed (`VOI-4`).</summary>
public enum UpdateManagerProfileOutcome
{
    /// <summary>The locale and time zone were changed.</summary>
    Updated = 0,

    /// <summary>The account has no manager profile, so it has no preferences to change.</summary>
    ManagerProfileRequired = 1,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 2,
}

/// <summary>The result of a manager-preference change.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Profile">The updated profile, when the change succeeded.</param>
public sealed record UpdateManagerProfileResult(
    UpdateManagerProfileOutcome Outcome,
    ManagerProfileResponse? Profile);

/// <summary>
/// Changes the manager's locale and time zone under an optimistic concurrency check (`CAL-4`, `CONC-1`,
/// master plan §10.2).
/// </summary>
/// <remarks>
/// The time zone is what renders every deadline in the manager's local time, so a change here changes what
/// they see across the whole application. Taking an <c>If-Match</c> version means a manager who edits it on
/// two devices is told to reapply rather than silently having one edit discarded.
/// </remarks>
public sealed class UpdateManagerProfile
{
    private readonly IClock _clock;
    private readonly IManagerRepository _managers;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public UpdateManagerProfile(
        IClock clock,
        IManagerRepository managers,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _managers = managers;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Applies the change.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c> header.</param>
    /// <param name="request">The requested locale and time zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UpdateManagerProfileResult> ExecuteAsync(
        Guid userId,
        long expectedVersion,
        UpdateManagerProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return new UpdateManagerProfileResult(UpdateManagerProfileOutcome.ManagerProfileRequired, null);
        }

        if (manager.Version != expectedVersion)
        {
            return new UpdateManagerProfileResult(
                UpdateManagerProfileOutcome.PreconditionFailed,
                manager.ToResponse());
        }

        var now = _clock.UtcNow;
        manager.ChangePreferences(request.Locale, request.TimeZone, now);

        _audit.Record(new AuditEntry(
            WorldAuditActions.ManagerPreferencesChanged,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.Manager,
            manager.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The row moved between the read and the write. Report a stale precondition and let the
            // manager decide, rather than overwriting a concurrent change.
            return new UpdateManagerProfileResult(UpdateManagerProfileOutcome.PreconditionFailed, null);
        }

        return new UpdateManagerProfileResult(UpdateManagerProfileOutcome.Updated, manager.ToResponse());
    }
}
