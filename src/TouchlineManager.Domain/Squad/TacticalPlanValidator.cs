using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// Why a tactical plan or a default lineup is not valid (`TAC-7`…`TAC-9`, `INS-10`, `INS-11`, `SQ-4`).
/// </summary>
/// <remarks>
/// The code, not the message, is what a client branches on. It is an enum rather than a string so the
/// validator cannot invent a code at a call site, and it is mapped to a stable wire code by
/// <see cref="TacticalPlanIssueCodes"/>.
/// </remarks>
public enum TacticalPlanIssueCode
{
    /// <summary>The plan does not name exactly eleven slots (`SQ-4`).</summary>
    SlotCount = 0,

    /// <summary>A slot number falls outside 1–11.</summary>
    SlotNumber = 1,

    /// <summary>Two slots share a number.</summary>
    DuplicateSlotNumber = 2,

    /// <summary>A slot's coordinates fall outside the 0–10,000 pitch (`TAC-9`).</summary>
    CoordinateOutOfBounds = 3,

    /// <summary>Two slots occupy the same point on the pitch (`TAC-7`).</summary>
    OverlappingSlots = 4,

    /// <summary>A slot's role does not belong to the family it names (`TAC-8`).</summary>
    RoleFamilyMismatch = 5,

    /// <summary>One player is assigned to more than one slot (`SQ-4`).</summary>
    DuplicatePlayer = 6,

    /// <summary>An assigned player is not a selectable member of the club.</summary>
    PlayerNotEligible = 7,

    /// <summary>An assigned player has an open injury or suspension (`TRN-12`, `DIS-5`).</summary>
    PlayerUnavailable = 8,

    /// <summary>Some, but not all, of the eleven slots are assigned (`SQ-4`).</summary>
    SelectionIncomplete = 9,
}

/// <summary>Stable wire codes for <see cref="TacticalPlanIssueCode"/>.</summary>
public static class TacticalPlanIssueCodes
{
    /// <summary>Converts an issue code to the stable code a client branches on.</summary>
    /// <param name="code">The issue code.</param>
    public static string ToCode(this TacticalPlanIssueCode code) => code switch
    {
        TacticalPlanIssueCode.SlotCount => "SLOT_COUNT",
        TacticalPlanIssueCode.SlotNumber => "SLOT_NUMBER",
        TacticalPlanIssueCode.DuplicateSlotNumber => "DUPLICATE_SLOT_NUMBER",
        TacticalPlanIssueCode.CoordinateOutOfBounds => "COORDINATE_OUT_OF_BOUNDS",
        TacticalPlanIssueCode.OverlappingSlots => "OVERLAPPING_SLOTS",
        TacticalPlanIssueCode.RoleFamilyMismatch => "ROLE_FAMILY_MISMATCH",
        TacticalPlanIssueCode.DuplicatePlayer => "DUPLICATE_PLAYER",
        TacticalPlanIssueCode.PlayerNotEligible => "PLAYER_NOT_ELIGIBLE",
        TacticalPlanIssueCode.PlayerUnavailable => "PLAYER_UNAVAILABLE",
        TacticalPlanIssueCode.SelectionIncomplete => "SELECTION_INCOMPLETE",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown tactical plan issue code."),
    };
}

/// <summary>
/// A slot as it is submitted for validation, before it becomes a <see cref="TacticalSlot"/>.
/// </summary>
/// <remarks>
/// Deliberately a plain value rather than the entity: a request is validated before anything is
/// constructed, so an invalid coordinate is a reported issue rather than an
/// <see cref="ArgumentOutOfRangeException"/> surfacing as a 500.
/// </remarks>
/// <param name="SlotNumber">The slot number, 1–11.</param>
/// <param name="PositionFamily">The family the slot asks for.</param>
/// <param name="Role">The role the slot asks for.</param>
/// <param name="NormalizedX">The normalized depth, 0–10,000.</param>
/// <param name="NormalizedY">The normalized width, 0–10,000.</param>
/// <param name="AssignedPlayerId">The player the manager picked for the slot, if any.</param>
public sealed record TacticalSlotDefinition(
    int SlotNumber,
    PositionFamily PositionFamily,
    PlayerRole Role,
    int NormalizedX,
    int NormalizedY,
    Guid? AssignedPlayerId);

/// <summary>One reason a plan is not valid.</summary>
/// <param name="Code">What is wrong.</param>
/// <param name="SlotNumber">The slot the issue concerns, when it concerns one.</param>
/// <param name="PlayerId">The player the issue concerns, when it concerns one.</param>
public sealed record TacticalPlanIssue(
    TacticalPlanIssueCode Code,
    int? SlotNumber,
    Guid? PlayerId);

/// <summary>The verdict on a plan and its default lineup.</summary>
/// <remarks>
/// <see cref="AssignedCount"/> and <see cref="IsComplete"/> are reported even when the plan is valid,
/// because a manager reading a half-filled template wants to know how many places are still open. A
/// plan with no assignments at all is valid — the shape is legal before anyone is picked — but a
/// partially filled one is not: eleven slots hold either nobody or eleven players (`SQ-4`).
/// </remarks>
/// <param name="Issues">Every reason the plan is not valid, in slot order. Empty when it is.</param>
/// <param name="AssignedCount">How many of the eleven slots name a player.</param>
public sealed record TacticalPlanValidation(IReadOnlyList<TacticalPlanIssue> Issues, int AssignedCount)
{
    /// <summary>Gets whether the plan is valid and may be saved.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>Gets whether all eleven slots name a player.</summary>
    public bool IsComplete => AssignedCount == WorldRuleSet.TeamSheetStarters;
}

/// <summary>
/// The tactics validator (`TAC-7`…`TAC-9`, `INS-10`, `INS-12`, `SQ-4`).
/// </summary>
/// <remarks>
/// <para>
/// One pure function so humans and AI are held to the same rules (`INS-12`): the AI's default lineup in
/// Stage 8 is planned through this and can be refused for exactly the reasons a manager's is. It takes
/// facts rather than repositories, so it can be tested without a database and cannot itself decide who
/// is selectable.
/// </para>
/// <para>
/// An out-of-position player is <em>not</em> an issue. `INS-10` makes familiarity a penalty the engine
/// applies, not a refusal — the manager may field a makeshift side — so the caller renders it as a
/// warning while this reports only the things that stop a plan being saved.
/// </para>
/// </remarks>
public static class TacticalPlanValidator
{
    /// <summary>Validates a submitted plan and its default lineup.</summary>
    /// <param name="slots">The submitted slots.</param>
    /// <param name="selectablePlayerIds">
    /// The players who may be selected: active, contracted, and registered members of the club.
    /// </param>
    /// <param name="unavailablePlayerIds">
    /// The subset of those with an open injury or suspension (`TRN-12`).
    /// </param>
    public static TacticalPlanValidation Validate(
        IEnumerable<TacticalSlotDefinition> slots,
        IReadOnlyCollection<Guid> selectablePlayerIds,
        IReadOnlyCollection<Guid> unavailablePlayerIds)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(selectablePlayerIds);
        ArgumentNullException.ThrowIfNull(unavailablePlayerIds);

        var materialized = slots.ToList();
        var issues = new List<TacticalPlanIssue>();
        var selectable = selectablePlayerIds as ISet<Guid> ?? new HashSet<Guid>(selectablePlayerIds);
        var unavailable = unavailablePlayerIds as ISet<Guid> ?? new HashSet<Guid>(unavailablePlayerIds);

        if (materialized.Count != FormationLayouts.SlotCount)
        {
            issues.Add(new TacticalPlanIssue(TacticalPlanIssueCode.SlotCount, null, null));
        }

        var usedNumbers = new HashSet<int>();
        var occupied = new HashSet<(int X, int Y)>();
        var assignedPlayers = new HashSet<Guid>();
        var assignedCount = 0;

        foreach (var slot in materialized.OrderBy(slot => slot.SlotNumber))
        {
            if (slot.SlotNumber is < TacticalSlot.FirstSlotNumber or > TacticalSlot.LastSlotNumber)
            {
                issues.Add(new TacticalPlanIssue(TacticalPlanIssueCode.SlotNumber, slot.SlotNumber, null));
            }
            else if (!usedNumbers.Add(slot.SlotNumber))
            {
                issues.Add(new TacticalPlanIssue(
                    TacticalPlanIssueCode.DuplicateSlotNumber,
                    slot.SlotNumber,
                    null));
            }

            if (slot.NormalizedX is < WorldRuleSet.SlotCoordinateMin or > WorldRuleSet.SlotCoordinateMax
                || slot.NormalizedY is < WorldRuleSet.SlotCoordinateMin or > WorldRuleSet.SlotCoordinateMax)
            {
                issues.Add(new TacticalPlanIssue(
                    TacticalPlanIssueCode.CoordinateOutOfBounds,
                    slot.SlotNumber,
                    null));
            }

            if (PlayerRoles.FamilyOf(slot.Role) != slot.PositionFamily)
            {
                issues.Add(new TacticalPlanIssue(
                    TacticalPlanIssueCode.RoleFamilyMismatch,
                    slot.SlotNumber,
                    null));
            }

            // TAC-7 forbids overlapping positions. Without a zone map the only unambiguous overlap is two
            // slots on the same point, which is also the one a drag-and-drop mistake actually produces.
            if (!occupied.Add((slot.NormalizedX, slot.NormalizedY)))
            {
                issues.Add(new TacticalPlanIssue(
                    TacticalPlanIssueCode.OverlappingSlots,
                    slot.SlotNumber,
                    null));
            }

            if (slot.AssignedPlayerId is not { } playerId)
            {
                continue;
            }

            assignedCount++;

            if (!assignedPlayers.Add(playerId))
            {
                issues.Add(new TacticalPlanIssue(TacticalPlanIssueCode.DuplicatePlayer, slot.SlotNumber, playerId));
            }
            else if (!selectable.Contains(playerId))
            {
                issues.Add(new TacticalPlanIssue(TacticalPlanIssueCode.PlayerNotEligible, slot.SlotNumber, playerId));
            }
            else if (unavailable.Contains(playerId))
            {
                issues.Add(new TacticalPlanIssue(TacticalPlanIssueCode.PlayerUnavailable, slot.SlotNumber, playerId));
            }
        }

        // Eleven slots hold nobody or eleven players. A half-filled sheet would field a side short of men,
        // which is why SQ-9 refuses to field a club below minimum rather than filling the gaps silently.
        if (assignedCount is > 0 and < WorldRuleSet.TeamSheetStarters)
        {
            issues.Add(new TacticalPlanIssue(TacticalPlanIssueCode.SelectionIncomplete, null, null));
        }

        issues.Sort(static (left, right) =>
        {
            var bySlot = Comparer<int?>.Default.Compare(left.SlotNumber, right.SlotNumber);

            return bySlot != 0 ? bySlot : left.Code.CompareTo(right.Code);
        });

        return new TacticalPlanValidation(issues, assignedCount);
    }
}
