using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>What one AI evaluation did.</summary>
/// <param name="Clubs">How many AI-controlled clubs were considered.</param>
/// <param name="TacticalPlansCreated">How many default tactical plans were written.</param>
/// <param name="TrainingPlansCreated">How many training plans were written.</param>
/// <param name="Skipped">How many clubs produced a plan the validator refused. Expected to be zero.</param>
public sealed record EvaluateAiClubsResult(
    int Clubs,
    int TacticalPlansCreated,
    int TrainingPlansCreated,
    int Skipped);

/// <summary>
/// Gives every club no human holds the tactics, side, and training a manager would have set (`INS-12`,
/// master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The AI's decisions are made by the pure <see cref="AiClubPolicy"/> and held to the same
/// <see cref="TacticalPlanValidator"/> a manager's save is, so an AI club fields a side the identical
/// rules accept. Nothing here reads a clock into the policy, draws from a global source, or grants the AI
/// a value a human could not enter.
/// </para>
/// <para>
/// It fills gaps and never overwrites. A club that already has a default plan or a training plan keeps it,
/// whether a manager set it or a previous run did — so a takeover inherits a working side and a
/// resignation does not reset the club (`WORLD-9`, `OCC-5`), and a repeated run is a no-op. That is what
/// makes the job safe to run at least once: the second pass for the same world writes nothing.
/// </para>
/// <para>
/// Only clubs with no open tenure are considered. An <em>inactive</em> tenure still occupies its club
/// (`OCC-8`), and making safe decisions on a manager's behalf while they are away is the inactivity
/// ladder's own work (`OCC-2`), which Stage 11 owns — leaving it here would silently overwrite a manager
/// who is merely on holiday.
/// </para>
/// </remarks>
public sealed class EvaluateAiClubs
{
    private readonly IAiClubRepository _repository;
    private readonly ITacticsRepository _tactics;
    private readonly ITrainingRepository _training;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public EvaluateAiClubs(
        IAiClubRepository repository,
        ITacticsRepository tactics,
        ITrainingRepository training,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tactics = tactics;
        _training = training;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Evaluates every AI-controlled club.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EvaluateAiClubsResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var clubs = await _repository.LoadAiClubsAsync(cancellationToken);
        var now = _clock.UtcNow;
        var effectiveDate = DateOnly.FromDateTime(now.UtcDateTime);

        var plans = 0;
        var trainingPlans = 0;
        var skipped = 0;

        // Club identity order makes a run reproducible when two clubs are written in one pass; the loads
        // already order by identity, and the sort here keeps that true if a repository ever does not.
        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            var decision = AiClubPolicy.Decide(club.ClubId, [.. club.Players.Select(ToPolicyPlayer)]);

            if (!club.HasDefaultTacticalPlan)
            {
                if (TryBuildPlan(club, decision, now, out var plan))
                {
                    _tactics.AddPlan(plan.Plan);

                    foreach (var slot in plan.Slots)
                    {
                        _tactics.AddSlot(slot);
                    }

                    plans++;
                }
                else
                {
                    // The policy builds a legal shape by construction, so this is a defect rather than a
                    // normal state; the count is carried out so the job can report it.
                    skipped++;
                }
            }

            if (!club.HasTrainingPlan)
            {
                _training.AddTrainingPlan(TrainingPlan.Set(
                    Guid.CreateVersion7(),
                    club.ClubId,
                    decision.TrainingFocus,
                    decision.TrainingIntensity,
                    effectiveDate,
                    now));

                trainingPlans++;
            }
        }

        if (plans > 0 || trainingPlans > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new EvaluateAiClubsResult(clubs.Count, plans, trainingPlans, skipped);
    }

    /// <summary>Turns the policy's decision into a default plan the validator has accepted.</summary>
    private static bool TryBuildPlan(
        AiClubRecord club,
        AiClubPlan decision,
        DateTimeOffset now,
        out TacticalPlanRecord record)
    {
        var definitions = decision.Slots
            .Select(slot => new TacticalSlotDefinition(
                slot.SlotNumber,
                slot.PositionFamily,
                slot.Role,
                slot.NormalizedX,
                slot.NormalizedY,
                slot.AssignedPlayerId))
            .ToList();

        var validation = TacticalPlanValidator.Validate(
            definitions,
            [.. club.Players.Select(player => player.PlayerId)],
            [.. club.Players.Where(player => !player.IsAvailable).Select(player => player.PlayerId)]);

        if (!validation.IsValid)
        {
            record = null!;

            return false;
        }

        var plan = TacticalPlan.Create(
            Guid.CreateVersion7(),
            club.ClubId,
            AiClubPolicy.PlanName,
            decision.Formation,
            decision.Instructions,
            isDefault: true,
            now);

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

        record = new TacticalPlanRecord(plan, slots);

        return true;
    }

    private static AiSquadPlayer ToPolicyPlayer(AiClubPlayerRow row) => new(
        row.PlayerId,
        row.PrimaryPosition,
        row.SecondaryPositions,
        row.Attributes,
        row.ConditionBp,
        row.IsAvailable);
}
