using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Ops;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when an operator assigned a club to the AI.</summary>
public enum AssignClubToAiOutcome
{
    /// <summary>The manager's tenure was closed and the club returned to full AI control.</summary>
    Applied = 0,

    /// <summary>No club exists with the requested identity.</summary>
    ClubNotFound = 1,

    /// <summary>The club already has no human manager, so there is nothing to assign.</summary>
    AlreadyAi = 2,
}

/// <summary>The result of assigning a club to the AI.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="ClubId">The club that was addressed.</param>
/// <param name="ManagerId">The manager whose tenure was closed, when the change was applied.</param>
public sealed record AssignClubToAiResult(
    AssignClubToAiOutcome Outcome,
    Guid? ClubId = null,
    Guid? ManagerId = null);

/// <summary>
/// Hands a human-held club back to full AI control before the inactivity ladder reaches it (`OCC-6`).
/// </summary>
/// <remarks>
/// <para>
/// The club returns to the AI by closing the manager's tenure with the administrator reason, exactly as the
/// ladder's own close does (`OCC-3`): control over a club is the tenure and nothing else (`WORLD-7`), so
/// there is no AI tenure to create. Closing frees the club's pyramid occupancy and leaves it claimable again,
/// which is why the capacity evaluation runs afterwards (`PYR-1`, `OCC-8`).
/// </para>
/// <para>
/// No takeover cooldown is started. `OCC-4`'s cooldown exists to stop a manager shopping for clubs by
/// resigning, and only a voluntary resignation is that; a repair the game makes on its own authority is not.
/// </para>
/// <para>
/// Nothing else about the club moves (`OCC-5`): its squad, contracts, cash, fixtures, bids, and financial
/// commitments all continue, and the worker's daily AI pass fills in the tactics and training the club now
/// lacks. An API request never writes a squad decision (ADR-0018).
/// </para>
/// </remarks>
public sealed class AssignClubToAi
{
    private readonly IClock _clock;
    private readonly IClubRepository _clubs;
    private readonly IClubTenureRepository _tenures;
    private readonly IAdvisoryLock _locks;
    private readonly CapacityEvaluator _capacity;
    private readonly IAuditWriter _audit;
    private readonly IOperationalMetrics _metrics;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public AssignClubToAi(
        IClock clock,
        IClubRepository clubs,
        IClubTenureRepository tenures,
        IAdvisoryLock locks,
        CapacityEvaluator capacity,
        IAuditWriter audit,
        IOperationalMetrics metrics,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _clubs = clubs;
        _tenures = tenures;
        _locks = locks;
        _capacity = capacity;
        _audit = audit;
        _metrics = metrics;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Closes the club's open tenure so the AI controls it.</summary>
    /// <param name="clubId">The club to hand to the AI.</param>
    /// <param name="reason">Why the operator is doing it. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AssignClubToAiResult> ExecuteAsync(
        Guid clubId,
        string reason,
        CancellationToken cancellationToken)
    {
        AccountAdministration.ValidateReason(reason);

        var club = await _clubs.FindAsync(clubId, cancellationToken);

        if (club is null)
        {
            return new AssignClubToAiResult(AssignClubToAiOutcome.ClubNotFound, clubId);
        }

        // Read committed, and the country's lock only: a club holding an open tenure cannot be claimed
        // concurrently, so there is no manager lock to take and no lock ordering to respect (ADR-0010).
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        await _locks.AcquireAsync(AdvisoryLockKey.Country(club.CountryId), cancellationToken);

        var tenure = await _tenures.FindOpenByClubAsync(clubId, cancellationToken);

        if (tenure is null)
        {
            return new AssignClubToAiResult(AssignClubToAiOutcome.AlreadyAi, clubId);
        }

        var now = _clock.UtcNow;

        tenure.Close(ClubTenureEndReasons.AdministratorClosed, now);

        _audit.Record(new AuditEntry(
            WorldAuditActions.ClubAssignedToAi,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.ClubTenure,
            tenure.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // The closed tenure is visible to this evaluation only now that it has been saved, and it is what
        // makes the country's occupancy fall.
        await _capacity.EnsureNextTierRequestedAsync(club.CountryId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _metrics.TenureClosed(ClubTenureEndReasons.AdministratorClosed);

        return new AssignClubToAiResult(AssignClubToAiOutcome.Applied, clubId, tenure.ManagerId);
    }
}
