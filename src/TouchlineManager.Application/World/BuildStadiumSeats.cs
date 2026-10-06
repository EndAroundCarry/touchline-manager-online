using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when a manager ordered new places for the stadium.</summary>
public enum BuildStadiumSeatsOutcome
{
    /// <summary>The places were built and paid for.</summary>
    Built = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 2,

    /// <summary>The manager holds no club.</summary>
    NoClub = 3,

    /// <summary>The club no longer exists, or has no ground or account to build on.</summary>
    ClubNotFound = 4,

    /// <summary>No <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 5,

    /// <summary>The supplied version was stale. The client must reload and decide again.</summary>
    PreconditionFailed = 6,

    /// <summary>The order would take the ground past its largest size (`STAD-1`).</summary>
    StadiumFull = 7,

    /// <summary>The club cannot pay for the places after what it has committed to bids (`FIN-10`).</summary>
    InsufficientFunds = 8,

    /// <summary>The order named a stand that does not exist or a count outside the allowed range.</summary>
    InvalidOrder = 9,
}

/// <summary>The result of ordering new places.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Stadium">The ground as it now stands, when the order succeeded.</param>
public sealed record BuildStadiumSeatsResult(BuildStadiumSeatsOutcome Outcome, StadiumResponse? Stadium);

/// <summary>
/// Adds places to the stadium and charges the club for them (`STAD-4`, `STAD-6`).
/// </summary>
/// <remarks>
/// <para>
/// A manager may add as many places as the club can pay for, of any kind, up to the ground's largest size.
/// The order is priced for the club's own tier and paid from its available cash — what is left after the bids
/// it has committed to (`FIN-10`) — through a ledger posting, so the money leaves by the one door every other
/// spend uses (`FIN-11`).
/// </para>
/// <para>
/// The ground's version is the strong entity tag and an order must carry it back. That is what makes a
/// double-click or a retried request a refusal rather than a second payment: the first order moves the
/// version on, so the second arrives holding a version that no longer exists (`CONC-1`). The ledger entry's
/// correlation key names the version the order produced, which is the same guarantee at the money's own
/// layer (`FIN-17`).
/// </para>
/// </remarks>
public sealed class BuildStadiumSeats
{
    private readonly ResolveOwnedClub _access;
    private readonly IStadiumRepository _stadiums;
    private readonly IStadiumQueries _queries;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public BuildStadiumSeats(
        ResolveOwnedClub access,
        IStadiumRepository stadiums,
        IStadiumQueries queries,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IClock clock,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _stadiums = stadiums;
        _queries = queries;
        _accounts = accounts;
        _ledger = ledger;
        _clock = clock;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Builds the places, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>.</param>
    /// <param name="request">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BuildStadiumSeatsResult> ExecuteAsync(
        Guid userId,
        long? expectedVersion,
        BuildSeatsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return Refused(FromAccess(access.Outcome));
        }

        if (!StadiumStands.TryFromCode(request.Stand, out var stand)
            || request.Count is < 1 or > StadiumRuleSet.MaxSeatsPerOrder)
        {
            return Refused(BuildStadiumSeatsOutcome.InvalidOrder);
        }

        var stadium = await _stadiums.FindByClubAsync(access.ClubId, cancellationToken);
        var context = await _queries.GetContextAsync(access.ClubId, cancellationToken);
        var account = await _accounts.FindByClubAsync(access.ClubId, cancellationToken);

        if (stadium is null || context is null || account is null)
        {
            return Refused(BuildStadiumSeatsOutcome.ClubNotFound);
        }

        if (expectedVersion is null)
        {
            return Refused(BuildStadiumSeatsOutcome.PreconditionRequired);
        }

        if (stadium.Version != expectedVersion.Value)
        {
            return Refused(BuildStadiumSeatsOutcome.PreconditionFailed);
        }

        if (request.Count > stadium.RemainingRoom)
        {
            return Refused(BuildStadiumSeatsOutcome.StadiumFull);
        }

        var cost = StadiumRuleSet.BuildCostMinorFor(stand, context.TierNumber) * request.Count;

        if (cost > account.AvailableMinor)
        {
            return Refused(BuildStadiumSeatsOutcome.InsufficientFunds);
        }

        var now = _clock.UtcNow;

        stadium.AddSeats(stand, request.Count, now);

        _ledger.Add(account.Post(
            LedgerPostings.StadiumConstruction(
                Guid.CreateVersion7(),
                access.ClubId,
                stadium.Id,
                stadium.Version,
                stand,
                request.Count,
                cost),
            now));

        _audit.Record(new AuditEntry(
            WorldAuditActions.StadiumSeatsBuilt,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.ClubStadium,
            stadium.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: $"{stand.ToCode()} x{request.Count}"));

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Refused(BuildStadiumSeatsOutcome.PreconditionFailed);
        }

        var paid = context with { CashMinor = account.CashMinor, ReservedMinor = account.ReservedMinor };

        return new BuildStadiumSeatsResult(BuildStadiumSeatsOutcome.Built, stadium.ToResponse(paid, now));
    }

    private static BuildStadiumSeatsResult Refused(BuildStadiumSeatsOutcome outcome) => new(outcome, null);

    private static BuildStadiumSeatsOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => BuildStadiumSeatsOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => BuildStadiumSeatsOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => BuildStadiumSeatsOutcome.NoClub,
        _ => BuildStadiumSeatsOutcome.ClubNotFound,
    };
}
