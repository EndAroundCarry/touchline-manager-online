using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Competition;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>
/// Maps the prepare-match snapshot and the team-sheet validator's verdict to their transport projections.
/// </summary>
/// <remarks>
/// <para>
/// One place, so the read and the save cannot shape a selection differently. All eighteen slots are always
/// returned, filled or empty: the screen renders a pitch and a bench from position rather than from
/// presence, and a response that omitted the empty slots would make "nobody picked yet" and "the slot is
/// gone" the same thing.
/// </para>
/// <para>
/// The starting slots take their family and role from the club's plan, which is what ties a prepared side
/// to the shape the engine will hash. The bench has no slot of its own in a plan, so those two are null
/// there rather than invented from the player's position — the substitute's own position travels on the
/// player, and inventing a second answer would be a second source of truth.
/// </para>
/// </remarks>
public static class TeamSheetMapping
{
    /// <summary>Projects the prepare-match screen's data for one club.</summary>
    /// <param name="snapshot">The stored snapshot.</param>
    /// <param name="clubId">The club whose side is being described.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static FixtureTeamSheetResponse ToResponse(
        this TeamSheetSnapshot snapshot,
        Guid clubId,
        DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var fixture = snapshot.Fixture;
        var isHome = fixture.HomeClubId == clubId;

        var opponentId = isHome ? fixture.AwayClubId : fixture.HomeClubId;
        var opponentName = isHome ? fixture.AwayClubName : fixture.HomeClubName;
        var opponentShortName = isHome ? fixture.AwayClubShortName : fixture.HomeClubShortName;

        return new FixtureTeamSheetResponse(
            fixture.FixtureId,
            clubId,
            isHome ? fixture.HomeClubName : fixture.AwayClubName,
            isHome ? fixture.HomeClubShortName : fixture.AwayClubShortName,
            opponentId,
            opponentName,
            opponentShortName,
            isHome ? "home" : "away",
            fixture.DivisionId,
            fixture.DivisionName,
            fixture.RoundNumber,
            fixture.KickoffAt,
            fixture.LockAt,
            fixture.Status.ToCode(),
            FixtureMapping.IsLocked(fixture.Status, fixture.LockAt, serverTime),
            snapshot.Plan?.PlanId,
            snapshot.Plan?.Name,
            snapshot.Plan?.FormationPreset.ToCode(),
            snapshot.Plan?.Version,
            snapshot.Sheet?.Version,
            (snapshot.Sheet?.Status ?? TeamSheetStatus.Draft).ToCode(),
            BuildSlots(snapshot),
            [.. snapshot.SelectablePlayers.Select(player => player.ToResponse())],
            serverTime);
    }

    /// <summary>Projects the validator's verdict.</summary>
    /// <param name="validation">The verdict.</param>
    public static TeamSheetValidationResponse ToResponse(this TeamSheetValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new TeamSheetValidationResponse(
            validation.IsValid,
            validation.StarterCount,
            validation.SubstituteCount,
            [.. validation.Issues.Select(issue => issue.ToResponse())]);
    }

    /// <summary>Projects one validator issue.</summary>
    /// <param name="issue">The issue.</param>
    public static TeamSheetIssueResponse ToResponse(this TeamSheetIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new TeamSheetIssueResponse(issue.Code.ToCode(), issue.SlotNumber, issue.PlayerId);
    }

    /// <summary>
    /// Lays out the eighteen slots, taking the eleven starting shapes from the plan and the occupancy from
    /// the saved sheet.
    /// </summary>
    private static List<TeamSheetSlotResponse> BuildSlots(TeamSheetSnapshot snapshot)
    {
        if (snapshot.Plan is not { } plan)
        {
            return [];
        }

        var entries = snapshot.Sheet?.Entries.ToDictionary(entry => entry.SlotNumber)
            ?? new Dictionary<int, TeamSheetEntryRow>();

        var slots = new List<TeamSheetSlotResponse>(TeamSheetEntry.LastSlotNumber);

        foreach (var slot in plan.Slots.OrderBy(slot => slot.SlotNumber))
        {
            entries.TryGetValue(slot.SlotNumber, out var entry);

            slots.Add(new TeamSheetSlotResponse(
                slot.SlotNumber,
                TeamSheetDesignation.Starter.ToCode(),
                slot.PositionFamily.ToCode(),
                slot.Role.ToCode(),
                Describe(entry)));
        }

        for (var number = WorldRuleSet.TeamSheetStarters + 1; number <= TeamSheetEntry.LastSlotNumber; number++)
        {
            entries.TryGetValue(number, out var entry);

            slots.Add(new TeamSheetSlotResponse(
                number,
                TeamSheetDesignation.Substitute.ToCode(),
                null,
                null,
                Describe(entry)));
        }

        return slots;
    }

    private static AssignedPlayerResponse? Describe(TeamSheetEntryRow? entry) =>
        entry is null
            ? null
            : new AssignedPlayerResponse(
                entry.PlayerId,
                entry.PlayerName,
                entry.PlayerShortName,
                entry.PrimaryPosition.ToCode(),
                entry.IsUnavailable);
}
