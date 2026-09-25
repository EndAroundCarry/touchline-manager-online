using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>What happened when a tactical plan was saved.</summary>
public enum SaveTacticalPlanOutcome
{
    /// <summary>A new plan was created.</summary>
    Created = 0,

    /// <summary>An existing plan was revised.</summary>
    Updated = 1,

    /// <summary>The plan breaks a tactics rule. The result carries the issues.</summary>
    Invalid = 2,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 3,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 4,

    /// <summary>The manager holds no club.</summary>
    NoClub = 5,

    /// <summary>No plan with that identity belongs to the caller's club.</summary>
    PlanNotFound = 6,

    /// <summary>The command is an update and no <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 7,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 8,

    /// <summary>The club no longer exists.</summary>
    ClubNotFound = 9,
}

/// <summary>The result of saving a plan.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Plan">The saved plan, when the save succeeded.</param>
/// <param name="Validation">The validation preview, present when the plan was refused.</param>
public sealed record SaveTacticalPlanResult(
    SaveTacticalPlanOutcome Outcome,
    TacticalPlanResponse? Plan,
    TacticalPlanValidationResponse? Validation);

/// <summary>
/// Creates or revises the club's tactical plan (master plan §10.4, §11.1; `TAC-7`…`TAC-9`, `INS-11`).
/// </summary>
/// <remarks>
/// <para>
/// One use case for the create and the update, because they differ only in whether a plan is loaded
/// first; the shape of a valid plan and the rules that decide it are identical. The client's
/// <c>If-Match</c> version is mandatory on the update: two devices editing a formation must not silently
/// overwrite each other, and the loser is told to reapply (`CONC-1`, ADR-0009).
/// </para>
/// <para>
/// Nothing about the club is taken from the request. The plan is created against the club the caller
/// actually holds, and an update only succeeds against a plan that already belongs to it, so naming
/// somebody else's plan is a refusal rather than a write.
/// </para>
/// <para>
/// The first plan a club saves becomes its default (`INS-11`). That is a convenience, not a rule the
/// schema enforces: the partial unique index allows a club to have no default, and the manager moves it
/// explicitly afterwards.
/// </para>
/// </remarks>
public sealed class SaveTacticalPlan
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
    public SaveTacticalPlan(
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

    /// <summary>Creates or revises a plan.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="planId">The plan to revise, or null to create one.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>, for an update.</param>
    /// <param name="request">The plan to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SaveTacticalPlanResult> ExecuteAsync(
        Guid userId,
        Guid? planId,
        long? expectedVersion,
        SaveTacticalPlanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new SaveTacticalPlanResult(FromAccess(access.Outcome), null, null);
        }

        var snapshot = await _queries.GetTacticsAsync(access.ClubId, cancellationToken);

        if (snapshot is null)
        {
            return new SaveTacticalPlanResult(SaveTacticalPlanOutcome.ClubNotFound, null, null);
        }

        TacticalPlanRecord? existing = null;

        if (planId is { } id)
        {
            if (expectedVersion is null)
            {
                return new SaveTacticalPlanResult(SaveTacticalPlanOutcome.PreconditionRequired, null, null);
            }

            existing = await _repository.FindAsync(id, cancellationToken);

            // A plan that does not exist and a plan that belongs to somebody else are the same answer, so
            // a manager cannot probe for another club's plan identities (master plan §10.9).
            if (existing is null || existing.Plan.ClubId != access.ClubId)
            {
                return new SaveTacticalPlanResult(SaveTacticalPlanOutcome.PlanNotFound, null, null);
            }

            if (existing.Plan.Version != expectedVersion.Value)
            {
                return new SaveTacticalPlanResult(SaveTacticalPlanOutcome.PreconditionFailed, null, null);
            }
        }

        var definitions = BuildDefinitions(request);
        var validation = TacticalPlanValidator.Validate(
            definitions,
            [.. snapshot.SelectablePlayers.Select(player => player.Id)],
            [.. snapshot.SelectablePlayers.Where(player => player.IsUnavailable).Select(player => player.Id)]);

        if (!validation.IsValid)
        {
            return new SaveTacticalPlanResult(
                SaveTacticalPlanOutcome.Invalid,
                null,
                validation.ToResponse());
        }

        var now = _clock.UtcNow;
        var record = existing is null
            ? Create(request, snapshot, access.ClubId, definitions, now)
            : Revise(existing, request, definitions, now);

        var action = existing is null
            ? SquadAuditActions.TacticalPlanCreated
            : SquadAuditActions.TacticalPlanUpdated;

        Record(action, userId, record.Plan.Id);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The plan moved between the read and the write. Report a stale precondition and let the
            // manager reload, rather than overwriting the change that won the race.
            return new SaveTacticalPlanResult(SaveTacticalPlanOutcome.PreconditionFailed, null, null);
        }

        var selectable = snapshot.SelectablePlayers.ToDictionary(player => player.Id);

        return new SaveTacticalPlanResult(
            existing is null ? SaveTacticalPlanOutcome.Created : SaveTacticalPlanOutcome.Updated,
            record.ToResponse(selectable),
            null);
    }

    /// <summary>
    /// Turns a request into the slots it describes, laying them out from the presets when no layout is
    /// given and applying the lineup on top.
    /// </summary>
    /// <remarks>
    /// The codes are parsed with the throwing conversions because the HTTP layer has already validated
    /// them; a use case is only ever reached with a request that passed its validator, and a worker that
    /// builds one is responsible for the same shape.
    /// </remarks>
    private static List<TacticalSlotDefinition> BuildDefinitions(SaveTacticalPlanRequest request)
    {
        var preset = FormationPresets.FromCode(request.FormationPreset);

        var layout = request.Slots is not null
            ? request.Slots
                .Select(slot => new TacticalSlotDefinition(
                    slot.SlotNumber,
                    PositionFamilies.FromCode(slot.PositionFamily),
                    PlayerRoles.FromCode(slot.Role),
                    slot.NormalizedX,
                    slot.NormalizedY,
                    null))
                .ToList()
            : FormationLayouts.DefaultSlots(preset)
                .Select(slot => new TacticalSlotDefinition(
                    slot.SlotNumber,
                    slot.PositionFamily,
                    slot.Role,
                    slot.NormalizedX,
                    slot.NormalizedY,
                    null))
                .ToList();

        if (request.Lineup is null)
        {
            return layout;
        }

        var lineup = request.Lineup.ToDictionary(entry => entry.SlotNumber, entry => (Guid?)entry.PlayerId);

        return [.. layout.Select(slot => slot with { AssignedPlayerId = lineup.GetValueOrDefault(slot.SlotNumber) })];
    }

    /// <summary>Lays out a new plan and its slots.</summary>
    /// <remarks>
    /// The default is decided from the read snapshot rather than a second query: the plans that exist are
    /// already known, and a club's first plan has no default to defer to (`INS-11`).
    /// </remarks>
    private TacticalPlanRecord Create(
        SaveTacticalPlanRequest request,
        TacticsSnapshot snapshot,
        Guid clubId,
        IReadOnlyList<TacticalSlotDefinition> definitions,
        DateTimeOffset now)
    {
        var hasDefault = snapshot.Plans.Any(plan => plan.IsDefault);

        var plan = TacticalPlan.Create(
            Guid.CreateVersion7(),
            clubId,
            request.Name,
            FormationPresets.FromCode(request.FormationPreset),
            ReadInstructions(request),
            isDefault: !hasDefault,
            now);

        _repository.AddPlan(plan);

        var slots = definitions
            .Select(definition => TacticalSlot.Place(
                Guid.CreateVersion7(),
                plan.Id,
                definition.SlotNumber,
                definition.PositionFamily,
                definition.Role,
                definition.NormalizedX,
                definition.NormalizedY,
                definition.AssignedPlayerId,
                now))
            .ToList();

        foreach (var slot in slots)
        {
            _repository.AddSlot(slot);
        }

        return new TacticalPlanRecord(plan, slots);
    }

    /// <summary>Revises an existing plan in place and re-lays its slots.</summary>
    private static TacticalPlanRecord Revise(
        TacticalPlanRecord existing,
        SaveTacticalPlanRequest request,
        IReadOnlyList<TacticalSlotDefinition> definitions,
        DateTimeOffset now)
    {
        existing.Plan.Revise(
            request.Name,
            FormationPresets.FromCode(request.FormationPreset),
            ReadInstructions(request),
            now);

        var byNumber = existing.Slots.ToDictionary(slot => slot.SlotNumber);

        foreach (var definition in definitions)
        {
            // Eleven slots always exist, so the lookup succeeds; reshaping in place keeps the slot
            // identities stable across a formation change.
            var slot = byNumber[definition.SlotNumber];

            slot.Reshape(
                definition.PositionFamily,
                definition.Role,
                definition.NormalizedX,
                definition.NormalizedY,
                now);
            slot.Assign(definition.AssignedPlayerId, now);
        }

        return existing;
    }

    private static TeamInstructionSet ReadInstructions(SaveTacticalPlanRequest request) => new()
    {
        Mentality = TeamInstructions.MentalityFromCode(request.Mentality),
        Tempo = TeamInstructions.TempoFromCode(request.Tempo),
        Passing = TeamInstructions.PassingFromCode(request.Passing),
        Width = TeamInstructions.WidthFromCode(request.Width),
        Pressing = TeamInstructions.PressingFromCode(request.Pressing),
        DefensiveLine = TeamInstructions.LineFromCode(request.DefensiveLine),
        Tackling = TeamInstructions.TacklingFromCode(request.Tackling),
        TimeWasting = TeamInstructions.TimeWastingFromCode(request.TimeWasting),
    };

    private static SaveTacticalPlanOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => SaveTacticalPlanOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => SaveTacticalPlanOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => SaveTacticalPlanOutcome.NoClub,
        _ => SaveTacticalPlanOutcome.ClubNotFound,
    };

    private void Record(string action, Guid userId, Guid planId) =>
        _audit.Record(new AuditEntry(
            action,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.TacticalPlan,
            planId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));
}
