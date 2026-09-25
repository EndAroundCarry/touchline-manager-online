using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when a manager resigned.</summary>
public enum ResignClubOutcome
{
    /// <summary>The tenure was closed and the cooldown started.</summary>
    Resigned = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account cannot write.</summary>
    AccountUnavailable = 2,

    /// <summary>The account has no manager profile.</summary>
    ManagerProfileRequired = 3,

    /// <summary>The manager holds no club, so there is nothing to resign from.</summary>
    NoActiveTenure = 4,
}

/// <summary>The result of a resignation.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="State">Where the manager stands afterwards: a profile with no club, and a cooldown.</param>
public sealed record ResignClubResult(
    ResignClubOutcome Outcome,
    OnboardingStateResponse? State);

/// <summary>
/// Resigns from the manager's club (`OCC-4`).
/// </summary>
/// <remarks>
/// <para>
/// Closing a tenure returns the club to full AI control and does not touch club state (`OCC-5`): the squad,
/// contracts, cash, and fixtures the manager inherited stay exactly as they were, because none of them
/// belong to the tenure. What the resignation does change is the manager's own record, which gains the
/// cooldown that stops a manager cycling through clubs to shop for one.
/// </para>
/// <para>
/// PYR-1 asks for a capacity evaluation after every tenure-status change. After a resignation the country's
/// occupancy has fallen, so the evaluation can only answer "no new tier needed" — it is run anyway rather
/// than skipped, because a rule with an exception for the case where it cannot matter is a rule whose next
/// exception is the one that matters.
/// </para>
/// </remarks>
public sealed class ResignClub
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IManagerRepository _managers;
    private readonly IClubTenureRepository _tenures;
    private readonly IWorldRepository _world;
    private readonly IOnboardingQueries _queries;
    private readonly IAdvisoryLock _locks;
    private readonly CapacityEvaluator _capacity;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public ResignClub(
        IClock clock,
        IUserRepository users,
        IManagerRepository managers,
        IClubTenureRepository tenures,
        IWorldRepository world,
        IOnboardingQueries queries,
        IAdvisoryLock locks,
        CapacityEvaluator capacity,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _managers = managers;
        _tenures = tenures;
        _world = world;
        _queries = queries;
        _locks = locks;
        _capacity = capacity;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Resigns the authenticated manager from their club.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ResignClubResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return Refused(ResignClubOutcome.WorldNotSeeded);
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null || !UserStatusRules.CanWrite(user.Status))
        {
            return Refused(ResignClubOutcome.AccountUnavailable);
        }

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return Refused(ResignClubOutcome.ManagerProfileRequired);
        }

        // Read committed, for the same reason a claim uses it: the exclusivity comes from the advisory
        // locks, and a serializable snapshot taken at the lock request would be stale by the time the lock
        // was granted (ADR-0010).
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        // The same order as a claim takes them: the manager's lock, then the country's.
        await _locks.AcquireAsync(AdvisoryLockKey.Manager(manager.Id), cancellationToken);

        var tenure = await _tenures.FindOpenByManagerAsync(manager.Id, cancellationToken);

        if (tenure is null)
        {
            return Refused(ResignClubOutcome.NoActiveTenure);
        }

        var club = await _queries.GetClubDashboardAsync(
            tenure.ClubId,
            world.CurrentSeasonNumber,
            cancellationToken);

        if (club is null)
        {
            return Refused(ResignClubOutcome.NoActiveTenure);
        }

        await _locks.AcquireAsync(AdvisoryLockKey.Country(club.Country.Id), cancellationToken);

        var now = _clock.UtcNow;

        tenure.Close(ClubTenureEndReasons.Resigned, now);
        manager.BeginTakeoverCooldown(WorldRuleSet.ResignationCooldown, now);

        _audit.Record(new AuditEntry(
            WorldAuditActions.ClubResigned,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.ClubTenure,
            tenure.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // The closed tenure is visible to this evaluation only now that it has been saved, and it is what
        // makes the country's occupancy fall.
        await _capacity.EnsureNextTierRequestedAsync(club.Country.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ResignClubResult(
            ResignClubOutcome.Resigned,
            new OnboardingStateResponse(manager.ToResponse(), Tenure: null, _clock.UtcNow));
    }

    private static ResignClubResult Refused(ResignClubOutcome outcome) => new(outcome, null);
}
