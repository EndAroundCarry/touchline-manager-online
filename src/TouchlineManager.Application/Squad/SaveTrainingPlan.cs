using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>What happened when a training plan was saved.</summary>
public enum SaveTrainingPlanOutcome
{
    /// <summary>The club's first training plan was created.</summary>
    Created = 0,

    /// <summary>An existing plan was revised.</summary>
    Updated = 1,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 2,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 3,

    /// <summary>The manager holds no club.</summary>
    NoClub = 4,

    /// <summary>The command is an update and no <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 5,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 6,

    /// <summary>The club no longer exists.</summary>
    ClubNotFound = 7,
}

/// <summary>The result of saving a training plan.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Training">The plan and squad as they now stand, when the save succeeded.</param>
public sealed record SaveTrainingPlanResult(SaveTrainingPlanOutcome Outcome, TrainingResponse? Training);

/// <summary>
/// Creates or revises the club's training plan (master plan §10.4, §11.1; `TRN-1`, `TRN-3`).
/// </summary>
/// <remarks>
/// <para>
/// One use case for create and update, as the tactics save is, because they differ only in whether a plan
/// is loaded first. The client's <c>If-Match</c> version is mandatory on the update, so two devices editing
/// a club's training do not silently overwrite each other (`CONC-1`, ADR-0009).
/// </para>
/// <para>
/// The plan takes effect from the day it is saved rather than from a date the client supplies: the manager
/// is setting what the club does now, and a future-dated plan would need the job to honour several at once
/// for no MVP benefit. The effective date is still stored, because the row records when the choice was made
/// (`TRN-1`).
/// </para>
/// </remarks>
public sealed class SaveTrainingPlan
{
    private readonly ResolveOwnedClub _access;
    private readonly ITrainingQueries _queries;
    private readonly ITrainingRepository _repository;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public SaveTrainingPlan(
        ResolveOwnedClub access,
        ITrainingQueries queries,
        ITrainingRepository repository,
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

    /// <summary>Creates or revises the club's training plan.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>, for an update.</param>
    /// <param name="request">The plan to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SaveTrainingPlanResult> ExecuteAsync(
        Guid userId,
        long? expectedVersion,
        SaveTrainingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new SaveTrainingPlanResult(FromAccess(access.Outcome), null);
        }

        var snapshot = await _queries.GetTrainingAsync(access.ClubId, cancellationToken);

        if (snapshot is null)
        {
            return new SaveTrainingPlanResult(SaveTrainingPlanOutcome.ClubNotFound, null);
        }

        var existing = await _repository.FindPlanAsync(access.ClubId, cancellationToken);

        if (existing is not null)
        {
            if (expectedVersion is null)
            {
                return new SaveTrainingPlanResult(SaveTrainingPlanOutcome.PreconditionRequired, null);
            }

            if (existing.Version != expectedVersion.Value)
            {
                return new SaveTrainingPlanResult(SaveTrainingPlanOutcome.PreconditionFailed, null);
            }
        }

        var now = _clock.UtcNow;
        var teamFocus = TrainingPlans.FromCode(request.TeamFocus);
        var intensity = TrainingPlans.IntensityFromCode(request.Intensity);
        var effectiveDate = DateOnly.FromDateTime(now.UtcDateTime);

        var plan = existing is null
            ? TrainingPlan.Set(
                Guid.CreateVersion7(), access.ClubId, teamFocus, intensity, effectiveDate, now)
            : Revise(existing, teamFocus, intensity, effectiveDate, now);

        if (existing is null)
        {
            _repository.AddTrainingPlan(plan);
        }

        Record(
            existing is null ? SquadAuditActions.TrainingPlanCreated : SquadAuditActions.TrainingPlanUpdated,
            userId,
            plan.Id);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The plan moved between the read and the write. Report a stale precondition and let the manager
            // reload, rather than overwriting the change that won the race.
            return new SaveTrainingPlanResult(SaveTrainingPlanOutcome.PreconditionFailed, null);
        }

        return new SaveTrainingPlanResult(
            existing is null ? SaveTrainingPlanOutcome.Created : SaveTrainingPlanOutcome.Updated,
            snapshot.ToResponse(plan, now));
    }

    private static TrainingPlan Revise(
        TrainingPlan plan,
        TrainingFocus teamFocus,
        TrainingIntensity intensity,
        DateOnly effectiveDate,
        DateTimeOffset now)
    {
        plan.Revise(teamFocus, intensity, effectiveDate, now);

        return plan;
    }

    private static SaveTrainingPlanOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => SaveTrainingPlanOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => SaveTrainingPlanOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => SaveTrainingPlanOutcome.NoClub,
        _ => SaveTrainingPlanOutcome.ClubNotFound,
    };

    private void Record(string action, Guid userId, Guid planId) =>
        _audit.Record(new AuditEntry(
            action,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.TrainingPlan,
            planId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));
}
