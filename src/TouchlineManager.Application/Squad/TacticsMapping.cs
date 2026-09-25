using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>
/// Maps the tactics read and write snapshots to their transport projections.
/// </summary>
/// <remarks>
/// <para>
/// One place, so a plan cannot be shaped differently by the read and the save. The two paths differ only
/// in where a slot's occupant comes from — the read projects it with the slot, the save resolves it
/// against the squad it was just validated against — so both end in the same builder.
/// </para>
/// <para>
/// Out-of-position is computed here rather than stored (`INS-10`): it is a function of the player's
/// positions and the slot's family, and computing it on read keeps a squad change from leaving a stale
/// flag on a saved plan.
/// </para>
/// </remarks>
public static class TacticsMapping
{
    /// <summary>Projects the tactics screen's data.</summary>
    /// <param name="snapshot">The stored snapshot.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static TacticsResponse ToResponse(this TacticsSnapshot snapshot, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new TacticsResponse(
            snapshot.ClubId,
            snapshot.ClubName,
            snapshot.ClubShortName,
            snapshot.CountryCode,
            snapshot.SeasonNumber,
            [.. snapshot.Plans.Select(plan => plan.ToResponse())],
            [.. snapshot.SelectablePlayers.Select(ToResponse)],
            [.. FormationPresets.All.Select(preset => preset.ToResponse())],
            serverTime);
    }

    /// <summary>Projects a saved plan as the read side returned it.</summary>
    /// <param name="plan">The stored plan.</param>
    public static TacticalPlanResponse ToResponse(this TacticsPlanRow plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var slots = plan.Slots
            .OrderBy(slot => slot.SlotNumber)
            .Select(slot => new TacticalSlotResponse(
                slot.SlotNumber,
                slot.PositionFamily.ToCode(),
                slot.Role.ToCode(),
                slot.NormalizedX,
                slot.NormalizedY,
                slot.AssignedPlayer is { } player
                    ? new AssignedPlayerResponse(
                        player.Id,
                        player.FullName,
                        player.ShortName,
                        player.PrimaryPosition.ToCode(),
                        player.IsUnavailable)
                    : null,
                slot.AssignedPlayer is { } assigned
                    && IsOutOfPosition(assigned.PrimaryPosition, assigned.SecondaryPositions, slot.PositionFamily)))
            .ToList();

        return Build(plan.Id, plan.Name, plan.FormationPreset, plan.Instructions, plan.IsDefault, plan.Version, slots);
    }

    /// <summary>
    /// Projects a plan that was just saved.
    /// </summary>
    /// <param name="record">The saved plan and its slots.</param>
    /// <param name="selectablePlayers">
    /// The squad the plan was validated against, keyed by player, used to name each slot's occupant.
    /// </param>
    public static TacticalPlanResponse ToResponse(
        this TacticalPlanRecord record,
        IReadOnlyDictionary<Guid, TacticsSelectablePlayerRow> selectablePlayers)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(selectablePlayers);

        var slots = record.Slots
            .OrderBy(slot => slot.SlotNumber)
            .Select(slot => new TacticalSlotResponse(
                slot.SlotNumber,
                slot.PositionFamily.ToCode(),
                slot.Role.ToCode(),
                slot.NormalizedX,
                slot.NormalizedY,
                DescribeAssigned(slot.AssignedPlayerId, selectablePlayers),
                slot.AssignedPlayerId is { } id
                    && selectablePlayers.TryGetValue(id, out var player)
                    && IsOutOfPosition(player.PrimaryPosition, player.SecondaryPositions, slot.PositionFamily)))
            .ToList();

        return Build(
            record.Plan.Id,
            record.Plan.Name,
            record.Plan.FormationPreset,
            record.Plan.Instructions,
            record.Plan.IsDefault,
            record.Plan.Version,
            slots);
    }

    /// <summary>Projects the validator's verdict.</summary>
    /// <param name="validation">The verdict.</param>
    public static TacticalPlanValidationResponse ToResponse(this TacticalPlanValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new TacticalPlanValidationResponse(
            validation.IsValid,
            validation.AssignedCount,
            validation.IsComplete,
            [.. validation.Issues.Select(issue => issue.ToResponse())]);
    }

    /// <summary>Projects one validator issue.</summary>
    /// <param name="issue">The issue.</param>
    public static TacticalPlanIssueResponse ToResponse(this TacticalPlanIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new TacticalPlanIssueResponse(issue.Code.ToCode(), issue.SlotNumber, issue.PlayerId);
    }

    /// <summary>Projects a formation preset's default arrangement.</summary>
    /// <param name="preset">The preset.</param>
    public static FormationPresetResponse ToResponse(this FormationPreset preset) =>
        new(
            preset.ToCode(),
            [.. FormationLayouts.DefaultSlots(preset).Select(slot => slot.ToResponse())]);

    /// <summary>Projects one slot of a formation preset's default arrangement.</summary>
    /// <param name="slot">The preset slot.</param>
    public static FormationSlotResponse ToResponse(this FormationSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return new FormationSlotResponse(
            slot.SlotNumber,
            slot.PositionFamily.ToCode(),
            slot.Role.ToCode(),
            slot.NormalizedX,
            slot.NormalizedY);
    }

    /// <summary>Gets a player's family, for the filter the screen groups by.</summary>
    /// <param name="player">The selectable player.</param>
    public static SelectablePlayerResponse ToResponse(this TacticsSelectablePlayerRow player)
    {
        ArgumentNullException.ThrowIfNull(player);

        return new SelectablePlayerResponse(
            player.Id,
            player.FullName,
            player.ShortName,
            player.PrimaryPosition.ToCode(),
            PlayerPositions.FamilyOf(player.PrimaryPosition).ToCode(),
            player.IsUnavailable);
    }

    /// <summary>
    /// Whether a player is not at home in a slot's family, primary or secondary (`INS-10`).
    /// </summary>
    /// <param name="primary">The player's primary position.</param>
    /// <param name="secondary">The player's other positions.</param>
    /// <param name="family">The slot's family.</param>
    public static bool IsOutOfPosition(
        PlayerPosition primary,
        IReadOnlyList<PlayerPosition> secondary,
        PositionFamily family) =>
        PlayerPositions.FamilyOf(primary) != family
        && !secondary.Any(position => PlayerPositions.FamilyOf(position) == family);

    private static AssignedPlayerResponse? DescribeAssigned(
        Guid? playerId,
        IReadOnlyDictionary<Guid, TacticsSelectablePlayerRow> selectablePlayers)
    {
        if (playerId is not { } id)
        {
            return null;
        }

        // A plan that saved successfully can only name selectable players, so the lookup is the branch
        // that always runs; the fallback names the player by identity only, so a future caller that maps
        // an unvalidated record still gets a slot rather than a null.
        return selectablePlayers.TryGetValue(id, out var player)
            ? new AssignedPlayerResponse(
                id,
                player.FullName,
                player.ShortName,
                player.PrimaryPosition.ToCode(),
                player.IsUnavailable)
            : new AssignedPlayerResponse(id, string.Empty, string.Empty, string.Empty, false);
    }

    private static TacticalPlanResponse Build(
        Guid id,
        string name,
        FormationPreset formationPreset,
        TeamInstructionSet instructions,
        bool isDefault,
        long version,
        List<TacticalSlotResponse> slots)
    {
        var assignedCount = slots.Count(slot => slot.AssignedPlayer is not null);

        return new TacticalPlanResponse(
            id,
            name,
            formationPreset.ToCode(),
            isDefault,
            new TeamInstructionsResponse(
                instructions.Mentality.ToCode(),
                instructions.Tempo.ToCode(),
                instructions.Passing.ToCode(),
                instructions.Width.ToCode(),
                instructions.Pressing.ToCode(),
                instructions.DefensiveLine.ToCode(),
                instructions.Tackling.ToCode(),
                instructions.TimeWasting.ToCode()),
            version,
            assignedCount,
            assignedCount == slots.Count,
            slots);
    }
}
