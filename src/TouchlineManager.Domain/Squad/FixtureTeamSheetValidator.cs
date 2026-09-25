using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// Why a fixture team sheet is not valid (`SQ-4`, `SQ-9`, `TRN-12`, `DIS-5`).
/// </summary>
/// <remarks>
/// The code, not the message, is what a client branches on. It is an enum rather than a string so the
/// validator cannot invent a code at a call site, and it is mapped to a stable wire code by
/// <see cref="TeamSheetIssueCodes"/>.
/// </remarks>
public enum TeamSheetIssueCode
{
    /// <summary>A slot number falls outside the 1–18 a sheet may use (`SQ-4`).</summary>
    SlotNumber = 0,

    /// <summary>Two entries share a slot number.</summary>
    DuplicateSlotNumber = 1,

    /// <summary>One player is named in more than one slot (`SQ-4`).</summary>
    DuplicatePlayer = 2,

    /// <summary>A named player is not a selectable member of the club.</summary>
    PlayerNotEligible = 3,

    /// <summary>A named player has an open injury or suspension (`TRN-12`, `DIS-5`).</summary>
    PlayerUnavailable = 4,

    /// <summary>The eleven starting slots are not all filled (`SQ-4`).</summary>
    SelectionIncomplete = 5,
}

/// <summary>Stable wire codes for <see cref="TeamSheetIssueCode"/>.</summary>
public static class TeamSheetIssueCodes
{
    /// <summary>Converts an issue code to the stable code a client branches on.</summary>
    /// <param name="code">The issue code.</param>
    public static string ToCode(this TeamSheetIssueCode code) => code switch
    {
        TeamSheetIssueCode.SlotNumber => "TEAM_SHEET_SLOT_NUMBER",
        TeamSheetIssueCode.DuplicateSlotNumber => "TEAM_SHEET_DUPLICATE_SLOT_NUMBER",
        TeamSheetIssueCode.DuplicatePlayer => "TEAM_SHEET_DUPLICATE_PLAYER",
        TeamSheetIssueCode.PlayerNotEligible => "TEAM_SHEET_PLAYER_NOT_ELIGIBLE",
        TeamSheetIssueCode.PlayerUnavailable => "TEAM_SHEET_PLAYER_UNAVAILABLE",
        TeamSheetIssueCode.SelectionIncomplete => "TEAM_SHEET_INCOMPLETE",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown team-sheet issue code."),
    };
}

/// <summary>
/// One player's place in a submitted selection, before it becomes a <see cref="TeamSheetEntry"/>.
/// </summary>
/// <remarks>
/// Deliberately a plain value rather than the entity: a request is validated before anything is
/// constructed, so a slot number outside the legal range is a reported issue rather than an
/// <see cref="ArgumentOutOfRangeException"/> surfacing as a 500.
/// </remarks>
/// <param name="SlotNumber">The slot number, 1–18. Starters take 1–11 and substitutes 12–18.</param>
/// <param name="PlayerId">The selected player.</param>
public sealed record TeamSheetSelection(int SlotNumber, Guid PlayerId);

/// <summary>One reason a selection is not valid.</summary>
/// <param name="Code">What is wrong.</param>
/// <param name="SlotNumber">The slot the issue concerns, when it concerns one.</param>
/// <param name="PlayerId">The player the issue concerns, when it concerns one.</param>
public sealed record TeamSheetIssue(TeamSheetIssueCode Code, int? SlotNumber, Guid? PlayerId);

/// <summary>The verdict on a submitted selection.</summary>
/// <param name="Issues">Every reason the selection is not valid, in slot order. Empty when it is.</param>
/// <param name="StarterCount">How many of the eleven starting slots name a player.</param>
/// <param name="SubstituteCount">How many substitutes are named.</param>
public sealed record TeamSheetValidation(
    IReadOnlyList<TeamSheetIssue> Issues,
    int StarterCount,
    int SubstituteCount)
{
    /// <summary>Gets whether the selection is valid and may be saved.</summary>
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// The fixture team-sheet validator (`SQ-4`, `SQ-9`, `TRN-12`).
/// </summary>
/// <remarks>
/// <para>
/// One pure function so humans and AI are held to the same rules (`INS-12`): the snapshot builder in
/// Stage 8 repairs and validates a locked sheet through the same code, and an AI's selection can be
/// refused for exactly the reasons a manager's can. It takes facts rather than repositories, so it can be
/// tested without a database and cannot itself decide who is selectable.
/// </para>
/// <para>
/// It deliberately does not police the bench's slot numbering beyond the 12–18 range. `SQ-4` asks for
/// "up to seven substitutes" and nothing about which numbers they take, and inventing a contiguity rule
/// would refuse a legal side for a reason the rules do not give. The bench's size needs no rule of its own:
/// slots 12–18 are exactly seven, so a set of distinct slot numbers cannot name more than seven.
/// </para>
/// <para>
/// Out-of-position is not an issue here, exactly as in <see cref="TacticalPlanValidator"/>: `INS-10` makes
/// familiarity a penalty the engine applies, not a reason to refuse a side.
/// </para>
/// </remarks>
public static class FixtureTeamSheetValidator
{
    /// <summary>Validates a submitted selection against the club's selectable and unavailable players.</summary>
    /// <param name="selection">The submitted entries.</param>
    /// <param name="selectablePlayerIds">
    /// The players who may be selected: active, contracted, and registered members of the club.
    /// </param>
    /// <param name="unavailablePlayerIds">
    /// The subset of those with an open injury or suspension (`TRN-12`).
    /// </param>
    public static TeamSheetValidation Validate(
        IEnumerable<TeamSheetSelection> selection,
        IReadOnlyCollection<Guid> selectablePlayerIds,
        IReadOnlyCollection<Guid> unavailablePlayerIds)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(selectablePlayerIds);
        ArgumentNullException.ThrowIfNull(unavailablePlayerIds);

        var entries = selection.ToList();
        var issues = new List<TeamSheetIssue>();
        var selectable = selectablePlayerIds as ISet<Guid> ?? new HashSet<Guid>(selectablePlayerIds);
        var unavailable = unavailablePlayerIds as ISet<Guid> ?? new HashSet<Guid>(unavailablePlayerIds);

        var usedSlots = new HashSet<int>();
        var usedPlayers = new HashSet<Guid>();
        var starterSlots = new HashSet<int>();
        var substitutes = 0;

        foreach (var entry in entries.OrderBy(entry => entry.SlotNumber))
        {
            if (entry.SlotNumber is < TeamSheetEntry.FirstSlotNumber or > TeamSheetEntry.LastSlotNumber)
            {
                issues.Add(new TeamSheetIssue(TeamSheetIssueCode.SlotNumber, entry.SlotNumber, entry.PlayerId));

                continue;
            }

            if (!usedSlots.Add(entry.SlotNumber))
            {
                issues.Add(new TeamSheetIssue(
                    TeamSheetIssueCode.DuplicateSlotNumber,
                    entry.SlotNumber,
                    entry.PlayerId));
            }

            if (entry.SlotNumber <= WorldRuleSet.TeamSheetStarters)
            {
                starterSlots.Add(entry.SlotNumber);
            }
            else
            {
                substitutes++;
            }

            if (!usedPlayers.Add(entry.PlayerId))
            {
                issues.Add(new TeamSheetIssue(
                    TeamSheetIssueCode.DuplicatePlayer,
                    entry.SlotNumber,
                    entry.PlayerId));
            }
            else if (!selectable.Contains(entry.PlayerId))
            {
                issues.Add(new TeamSheetIssue(
                    TeamSheetIssueCode.PlayerNotEligible,
                    entry.SlotNumber,
                    entry.PlayerId));
            }
            else if (unavailable.Contains(entry.PlayerId))
            {
                issues.Add(new TeamSheetIssue(
                    TeamSheetIssueCode.PlayerUnavailable,
                    entry.SlotNumber,
                    entry.PlayerId));
            }
        }

        if (starterSlots.Count != WorldRuleSet.TeamSheetStarters)
        {
            issues.Add(new TeamSheetIssue(TeamSheetIssueCode.SelectionIncomplete, null, null));
        }

        issues.Sort(static (left, right) =>
        {
            var bySlot = Comparer<int?>.Default.Compare(left.SlotNumber, right.SlotNumber);

            return bySlot != 0 ? bySlot : left.Code.CompareTo(right.Code);
        });

        return new TeamSheetValidation(issues, starterSlots.Count, substitutes);
    }
}
