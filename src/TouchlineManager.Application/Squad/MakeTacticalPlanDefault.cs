using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>What happened when a plan was promoted to the club's default.</summary>
public enum MakeTacticalPlanDefaultOutcome
{
    /// <summary>The plan is the club's default.</summary>
    MadeDefault = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 2,

    /// <summary>The manager holds no club.</summary>
    NoClub = 3,

    /// <summary>No plan with that identity belongs to the caller's club.</summary>
    PlanNotFound = 4,

    /// <summary>No <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 5,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 6,

    /// <summary>The club no longer exists.</summary>
    ClubNotFound = 7,
}

/// <summary>The result of promoting a plan.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Plan">The promoted plan, when the command succeeded.</param>
public sealed record MakeTacticalPlanDefaultResult(
    MakeTacticalPlanDefaultOutcome Outcome,
    TacticalPlanResponse? Plan);

/// <summary>
/// Makes one of the club's plans its default (`INS-11`).
/// </summary>
/// <remarks>
/// <para>
/// A club has at most one default plan, so promoting one demotes another. The two writes happen in a
/// single transaction with the demotion committed first: the partial unique index on <c>is_default</c>
/// is checked per statement, and promoting before demoting would transiently put two rows in it.
/// </para>
/// <para>
/// The command is idempotent. Promoting the plan that is already the default succeeds without writing,
/// so a client that retries a lost response does not turn a success into a conflict.
/// </para>
/// </remarks>
public sealed class MakeTacticalPlanDefault
{
    private readonly ResolveOwnedClub _access;
    private readonly ITacticsQueries _queries;
    private readonly ITacticsRepository _repository;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public MakeTacticalPlanDefault(
        ResolveOwnedClub access,
        ITacticsQueries queries,
        ITacticsRepository repository,
        IClock clock,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _queries = queries;
        _repository = repository;
        _clock = clock;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Promotes a plan, demoting the club's previous default.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="planId">The plan to promote.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c> header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MakeTacticalPlanDefaultResult> ExecuteAsync(
        Guid userId,
        Guid planId,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new MakeTacticalPlanDefaultResult(FromAccess(access.Outcome), null);
        }

        if (expectedVersion is null)
        {
            return new MakeTacticalPlanDefaultResult(MakeTacticalPlanDefaultOutcome.PreconditionRequired, null);
        }

        var snapshot = await _queries.GetTacticsAsync(access.ClubId, cancellationToken);

        if (snapshot is null)
        {
            return new MakeTacticalPlanDefaultResult(MakeTacticalPlanDefaultOutcome.ClubNotFound, null);
        }

        var record = await _repository.FindAsync(planId, cancellationToken);

        if (record is null || record.Plan.ClubId != access.ClubId)
        {
            return new MakeTacticalPlanDefaultResult(MakeTacticalPlanDefaultOutcome.PlanNotFound, null);
        }

        if (record.Plan.Version != expectedVersion.Value)
        {
            return new MakeTacticalPlanDefaultResult(MakeTacticalPlanDefaultOutcome.PreconditionFailed, null);
        }

        var selectable = snapshot.SelectablePlayers.ToDictionary(player => player.Id);

        if (record.Plan.IsDefault)
        {
            return new MakeTacticalPlanDefaultResult(
                MakeTacticalPlanDefaultOutcome.MadeDefault,
                record.ToResponse(selectable));
        }

        try
        {
            await PromoteAsync(record, access.ClubId, userId, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return new MakeTacticalPlanDefaultResult(MakeTacticalPlanDefaultOutcome.PreconditionFailed, null);
        }

        return new MakeTacticalPlanDefaultResult(
            MakeTacticalPlanDefaultOutcome.MadeDefault,
            record.ToResponse(selectable));
    }

    private async Task PromoteAsync(
        TacticalPlanRecord record,
        Guid clubId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        var now = _clock.UtcNow;
        var current = await _repository.FindDefaultAsync(clubId, cancellationToken);

        if (current is not null && current.Plan.Id != record.Plan.Id)
        {
            current.Plan.RemoveDefault(now);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        record.Plan.MakeDefault(now);

        _audit.Record(new AuditEntry(
            SquadAuditActions.TacticalPlanMadeDefault,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.TacticalPlan,
            record.Plan.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static MakeTacticalPlanDefaultOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => MakeTacticalPlanDefaultOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => MakeTacticalPlanDefaultOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => MakeTacticalPlanDefaultOutcome.NoClub,
        _ => MakeTacticalPlanDefaultOutcome.ClubNotFound,
    };
}
