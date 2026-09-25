using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when a club was claimed.</summary>
public enum ClaimClubOutcome
{
    /// <summary>The tenure was created, or a retry of the same request found it already created.</summary>
    Claimed = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The world is frozen, so onboarding is closed.</summary>
    WorldNotAcceptingClaims = 2,

    /// <summary>The account cannot write: suspended, closing, or anonymized.</summary>
    AccountUnavailable = 3,

    /// <summary>The account has no manager profile yet.</summary>
    ManagerProfileRequired = 4,

    /// <summary>The manager already holds a club (`OCC-9`).</summary>
    ManagerHasActiveClub = 5,

    /// <summary>The manager is still serving the post-resignation cooldown (`OCC-4`).</summary>
    ManagerInCooldown = 6,

    /// <summary>The club does not exist.</summary>
    ClubNotFound = 7,

    /// <summary>The club is not in the country's lowest active tier, or is not active (`WORLD-8`).</summary>
    ClubNotClaimable = 8,

    /// <summary>Another manager already holds this club (master plan §7.6).</summary>
    ClubAlreadyClaimed = 9,

    /// <summary>Every club in the country's lowest tier is held and the next tier is being generated (`PYR-10`).</summary>
    CapacityProvisioning = 10,

    /// <summary>The idempotency key was already used for a different claim.</summary>
    IdempotencyKeyReused = 11,
}

/// <summary>The result of a takeover.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Dashboard">The inherited club, when the claim succeeded (master plan §7.6).</param>
/// <param name="Provisioning">The next tier's state, when the country is out of capacity.</param>
/// <param name="CooldownUntil">When the cooldown lapses, when one is blocking the claim.</param>
public sealed record ClaimClubResult(
    ClaimClubOutcome Outcome,
    ClubDashboardResponse? Dashboard,
    ProvisioningStatusResponse? Provisioning,
    DateTimeOffset? CooldownUntil);

/// <summary>
/// Takes over an AI-controlled club in a country's lowest active tier (master plan §7.6, `WORLD-8`).
/// </summary>
/// <remarks>
/// <para>
/// The most consequential command in the product, and the most carefully ordered one. A takeover is
/// irreversible from the claimant's point of view — the club's squad, contracts, cash, and history all
/// become theirs — so the checks happen in the order of what a manager can act on, and the write happens
/// inside a serializable transaction holding both the manager's and the country's advisory lock.
/// </para>
/// <para>
/// Nothing in the request is trusted. The manager comes from the authenticated account, and the country,
/// the tier, the club's current control, and its availability are all read from the database
/// (master plan §10.9).
/// </para>
/// <para>
/// The order of two refusals is deliberate. A club another manager holds is reported as
/// <see cref="ClaimClubOutcome.ClubAlreadyClaimed"/> when the country still has room, because "pick
/// another club" is then a true and useful answer. When the whole tier is taken the answer is
/// <see cref="ClaimClubOutcome.CapacityProvisioning"/> instead, because "pick another club" would be
/// false, and what the manager actually needs to know is that a new tier is on its way (`PYR-10`).
/// </para>
/// </remarks>
public sealed class ClaimClub
{
    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IManagerRepository _managers;
    private readonly IClubRepository _clubs;
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
    public ClaimClub(
        IClock clock,
        IUserRepository users,
        IManagerRepository managers,
        IClubRepository clubs,
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
        _clubs = clubs;
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

    /// <summary>Claims a club for the authenticated account's manager.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="request">The club to take over.</param>
    /// <param name="idempotencyKey">
    /// The caller's key for this attempt. A retry with the same key returns the first attempt's result
    /// instead of creating a second tenure (`CONC-3`).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ClaimClubResult> ExecuteAsync(
        Guid userId,
        ClaimClubRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return Refused(ClaimClubOutcome.WorldNotSeeded);
        }

        if (!world.AcceptsClaims)
        {
            return Refused(ClaimClubOutcome.WorldNotAcceptingClaims);
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null || !UserStatusRules.CanWrite(user.Status))
        {
            return Refused(ClaimClubOutcome.AccountUnavailable);
        }

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return Refused(ClaimClubOutcome.ManagerProfileRequired);
        }

        // A retry of an attempt that already succeeded is answered from the row it created. Doing this
        // before taking any lock is what makes a retried claim cost one query rather than a transaction.
        var replay = await ReplayAsync(manager, request.ClubId, idempotencyKey, cancellationToken);

        if (replay is not null)
        {
            return replay;
        }

        var club = await _clubs.FindAsync(request.ClubId, cancellationToken);

        if (club is null)
        {
            return Refused(ClaimClubOutcome.ClubNotFound);
        }

        var placement = await _queries.GetClubDashboardAsync(
            club.Id,
            world.CurrentSeasonNumber,
            cancellationToken);

        if (placement is null || !await IsInLowestActiveTierAsync(club, placement, cancellationToken))
        {
            return Refused(ClaimClubOutcome.ClubNotClaimable);
        }

        return await ClaimWithinLockAsync(
            userId,
            manager,
            club,
            world.CurrentSeasonNumber,
            idempotencyKey,
            cancellationToken);
    }

    /// <summary>
    /// Performs the checks that must not race and then writes the tenure.
    /// </summary>
    /// <remarks>
    /// The manager's lock is taken before the country's, and every caller uses that order, which is what
    /// keeps holding both deadlock-free. Inside them the occupancy count and the club's control are read
    /// again: anything read before the locks was only there to fail fast.
    /// </remarks>
    private async Task<ClaimClubResult> ClaimWithinLockAsync(
        Guid userId,
        Manager manager,
        Club club,
        int seasonNumber,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Read committed, not serializable. The exclusivity this workflow needs comes from the advisory
        // locks below; a serializable transaction would take its snapshot at the first statement — the
        // lock request — and would therefore still be reading pre-lock state after the lock was granted,
        // which is exactly the stale read the lock exists to prevent. Read committed gives each statement
        // the latest committed state, so the counts read under the lock are the truth (ADR-0010).
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        await _locks.AcquireAsync(AdvisoryLockKey.Manager(manager.Id), cancellationToken);
        await _locks.AcquireAsync(AdvisoryLockKey.Country(club.CountryId), cancellationToken);

        var now = _clock.UtcNow;

        if (await _tenures.FindOpenByManagerAsync(manager.Id, cancellationToken) is not null)
        {
            return Refused(ClaimClubOutcome.ManagerHasActiveClub);
        }

        if (!manager.CanTakeOver(now))
        {
            return new ClaimClubResult(
                ClaimClubOutcome.ManagerInCooldown,
                null,
                null,
                manager.TakeoverCooldownUntil);
        }

        var capacity = await _queries.GetCountryCapacityAsync(club.CountryId, cancellationToken);

        if (capacity is null)
        {
            return Refused(ClaimClubOutcome.ClubNotClaimable);
        }

        if (capacity.ClubsInLowestTier > 0 && capacity.HumanOccupiedClubs >= capacity.ClubsInLowestTier)
        {
            // PYR-2: the tier is full, so the country's answer is a new tier rather than a refusal with no
            // way forward. PYR-10 requires the request's state to come back with the response.
            var provisioning = await _capacity.EnsureNextTierRequestedAsync(club.CountryId, cancellationToken);

            Record(WorldAuditActions.ClaimRefusedAtCapacity, userId, AuditTargetTypes.Club, club.Id, "country at capacity");

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ClaimClubResult(ClaimClubOutcome.CapacityProvisioning, null, provisioning, null);
        }

        if (await _tenures.FindOpenByClubAsync(club.Id, cancellationToken) is not null)
        {
            return Refused(ClaimClubOutcome.ClubAlreadyClaimed);
        }

        // The caller's key is recorded verbatim, because it is what a retry will present. A manager who
        // resigns and returns to the same club later presents a new key and correctly gets a new tenure.
        var tenure = ClubTenure.Start(Guid.CreateVersion7(), club.Id, manager.Id, idempotencyKey, now);

        _tenures.Add(tenure);
        manager.ClearTakeoverCooldown(now);

        Record(WorldAuditActions.ClubClaimed, userId, AuditTargetTypes.ClubTenure, tenure.Id, reason: null);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // PYR-1: this takeover may be the one that filled the tier, and the row that filled it is only
        // visible to the evaluation once it has been saved inside the transaction.
        await _capacity.EnsureNextTierRequestedAsync(club.CountryId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var dashboard = await _queries.GetClubDashboardAsync(club.Id, seasonNumber, cancellationToken);

        return new ClaimClubResult(
            ClaimClubOutcome.Claimed,
            dashboard?.ToDashboard(_clock.UtcNow),
            null,
            null);
    }

    /// <summary>Answers a retried claim from the tenure the first attempt created.</summary>
    private async Task<ClaimClubResult?> ReplayAsync(
        Manager manager,
        Guid clubId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await _tenures.FindByTakeoverKeyAsync(idempotencyKey, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.ManagerId != manager.Id || existing.ClubId != clubId || !existing.IsOpen)
        {
            // Either the key belongs to a different claim, or it belongs to one that has since ended. In
            // both cases answering with the first claim's club would hand this caller somebody else's
            // outcome — or a club they no longer hold — so the key is refused and a new one is required.
            // It cannot simply be reused either: the key is unique in the database, so a second tenure
            // under it is impossible by construction.
            return Refused(ClaimClubOutcome.IdempotencyKeyReused);
        }

        var world = await _world.FindWorldAsync(cancellationToken);
        var dashboard = world is null
            ? null
            : await _queries.GetClubDashboardAsync(clubId, world.CurrentSeasonNumber, cancellationToken);

        return new ClaimClubResult(
            ClaimClubOutcome.Claimed,
            dashboard?.ToDashboard(_clock.UtcNow),
            null,
            null);
    }

    private async Task<bool> IsInLowestActiveTierAsync(
        Club club,
        ClubDashboardSnapshot placement,
        CancellationToken cancellationToken)
    {
        if (club.Status != ClubStatus.Active || placement.Division.CountryId != club.CountryId)
        {
            return false;
        }

        var lowest = await _world.FindLowestActiveDivisionAsync(club.CountryId, cancellationToken);

        return lowest is not null && lowest.Id == placement.Division.Id;
    }

    private void Record(string action, Guid userId, string targetType, Guid targetId, string? reason) =>
        _audit.Record(new AuditEntry(
            action,
            AuditActorTypes.User,
            userId,
            targetType,
            targetId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            reason));

    private static ClaimClubResult Refused(ClaimClubOutcome outcome) => new(outcome, null, null, null);
}
