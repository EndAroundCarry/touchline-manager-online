using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>A plan and its slots, loaded together because neither is meaningful alone.</summary>
/// <param name="Plan">The plan.</param>
/// <param name="Slots">The plan's eleven slots.</param>
public sealed record TacticalPlanRecord(TacticalPlan Plan, IReadOnlyList<TacticalSlot> Slots);

/// <summary>
/// Persistence for the squad module's tactical plans.
/// </summary>
/// <remarks>
/// <para>
/// A staging port like <see cref="ISquadRepository"/>: it adds and mutates rows in the current unit of
/// work and never saves, so a save that revises a plan and re-lays its slots commits once.
/// </para>
/// <para>
/// Slots are not a navigation property on <see cref="TacticalPlan"/>, because the plan does not police
/// its own layout — the validator does — so a load returns the two together rather than the plan
/// reaching into a child collection.
/// </para>
/// </remarks>
public interface ITacticsRepository
{
    /// <summary>Stages a new plan.</summary>
    /// <param name="plan">The plan.</param>
    void AddPlan(TacticalPlan plan);

    /// <summary>Stages a new slot.</summary>
    /// <param name="slot">The slot.</param>
    void AddSlot(TacticalSlot slot);

    /// <summary>Loads a plan and its slots, or returns null if no such plan exists.</summary>
    /// <param name="planId">The plan to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TacticalPlanRecord?> FindAsync(Guid planId, CancellationToken cancellationToken);

    /// <summary>Loads a club's default plan and its slots, or returns null when the club has none.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TacticalPlanRecord?> FindDefaultAsync(Guid clubId, CancellationToken cancellationToken);
}
