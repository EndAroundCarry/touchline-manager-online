using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Auth;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.World;

/// <summary>What happened when a manager ordered an upgrade of the snack bar or the merchandise shop.</summary>
public enum UpgradeStructureOutcome
{
    /// <summary>The work was paid for and has begun.</summary>
    Started = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 2,

    /// <summary>The manager holds no club.</summary>
    NoClub = 3,

    /// <summary>The club no longer exists, or has no ground, account or building.</summary>
    ClubNotFound = 4,

    /// <summary>The order named a building that does not exist.</summary>
    UnknownKind = 5,

    /// <summary>No <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 6,

    /// <summary>The supplied version was stale. The client must reload and decide again.</summary>
    PreconditionFailed = 7,

    /// <summary>The building is already at its highest level (`STRC-1`).</summary>
    MaxLevel = 8,

    /// <summary>The building is already being upgraded; one upgrade runs at a time (`STRC-3`).</summary>
    Building = 9,

    /// <summary>The club cannot pay for the upgrade after what it has committed to bids (`FIN-10`).</summary>
    InsufficientFunds = 10,

    /// <summary>The club is in the red or in administration, so it may not build (`DEBT-3`).</summary>
    Embargo = 11,
}

/// <summary>The result of ordering an upgrade.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Structures">Both buildings as they now stand, when the order succeeded.</param>
public sealed record UpgradeStructureResult(UpgradeStructureOutcome Outcome, StructuresResponse? Structures);

/// <summary>
/// Orders the upgrade of a building and charges the club for it (`STRC-2`, `STRC-3`).
/// </summary>
/// <remarks>
/// <para>
/// Only a human manager can order one: the club is resolved from the caller's tenure, and a club run by the
/// computer has none (`STRC-2`). The upgrade is paid in full when ordered, from the cash left after the bids
/// the club has committed to (`FIN-10`), through a ledger posting, so the money leaves by the one door every
/// other spend uses (`FIN-11`).
/// </para>
/// <para>
/// The building keeps trading at its old level until the work finishes. The job that finishes it is enqueued
/// in the same transaction as the payment, so the club is never charged for work that will not complete, and
/// never given work it did not pay for.
/// </para>
/// <para>
/// The building's version is the strong entity tag and an order must carry it back. That is what makes a
/// double-click or a retried request a refusal rather than a second payment: the first order moves the
/// version on, so the second arrives holding a version that no longer exists (`CONC-1`). The ledger entry's
/// correlation key names the version the order produced, which is the same guarantee at the money's own
/// layer (`FIN-17`).
/// </para>
/// </remarks>
public sealed class UpgradeStructure
{
    private readonly ResolveOwnedClub _access;
    private readonly IStructureRepository _structures;
    private readonly IStadiumQueries _context;
    private readonly IClubAccountRepository _accounts;
    private readonly ClubBuilding _building;
    private readonly StructuresComposer _composer;
    private readonly IClock _clock;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public UpgradeStructure(
        ResolveOwnedClub access,
        IStructureRepository structures,
        IStadiumQueries context,
        IClubAccountRepository accounts,
        ClubBuilding building,
        StructuresComposer composer,
        IClock clock,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _structures = structures;
        _context = context;
        _accounts = accounts;
        _building = building;
        _composer = composer;
        _clock = clock;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Starts the upgrade, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="kindCode">The building's stable code, from the route.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UpgradeStructureResult> ExecuteAsync(
        Guid userId,
        string kindCode,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return Refused(FromAccess(access.Outcome));
        }

        if (!StructureKinds.TryFromCode(kindCode, out var kind))
        {
            return Refused(UpgradeStructureOutcome.UnknownKind);
        }

        var building = await _structures.FindByClubAsync(access.ClubId, kind, cancellationToken);
        var context = await _context.GetContextAsync(access.ClubId, cancellationToken);
        var account = await _accounts.FindByClubAsync(access.ClubId, cancellationToken);

        if (building is null || context is null || account is null)
        {
            return Refused(UpgradeStructureOutcome.ClubNotFound);
        }

        if (expectedVersion is null)
        {
            return Refused(UpgradeStructureOutcome.PreconditionRequired);
        }

        if (building.Version != expectedVersion.Value)
        {
            return Refused(UpgradeStructureOutcome.PreconditionFailed);
        }

        var check = ClubBuilding.CheckUpgrade(building, account, context.TierNumber);

        if (check != ClubBuildCheck.Allowed)
        {
            return Refused(check switch
            {
                ClubBuildCheck.Building => UpgradeStructureOutcome.Building,
                ClubBuildCheck.MaxLevel => UpgradeStructureOutcome.MaxLevel,
                ClubBuildCheck.Embargo => UpgradeStructureOutcome.Embargo,
                _ => UpgradeStructureOutcome.InsufficientFunds,
            });
        }

        var now = _clock.UtcNow;

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        await _building.StageUpgradeAsync(
            building,
            account,
            context.TierNumber,
            MarketActor.ForUser(userId, _requestContext, _secureTokens),
            now,
            cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Refused(UpgradeStructureOutcome.PreconditionFailed);
        }

        await transaction.CommitAsync(cancellationToken);

        var structures = await _composer.ComposeAsync(access.ClubId, cancellationToken);

        return structures is null
            ? Refused(UpgradeStructureOutcome.ClubNotFound)
            : new UpgradeStructureResult(UpgradeStructureOutcome.Started, structures);
    }

    private static UpgradeStructureResult Refused(UpgradeStructureOutcome outcome) => new(outcome, null);

    private static UpgradeStructureOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => UpgradeStructureOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => UpgradeStructureOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => UpgradeStructureOutcome.NoClub,
        _ => UpgradeStructureOutcome.ClubNotFound,
    };
}
