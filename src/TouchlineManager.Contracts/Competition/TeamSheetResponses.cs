using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Contracts.Competition;

/// <summary>
/// One slot of a fixture team sheet, filled or empty (`SQ-4`).
/// </summary>
/// <remarks>
/// The family and role come from the club's plan for the eleven starting slots, which is what ties a
/// prepared side to the shape the engine will hash. A substitute has no slot of its own in the plan, so
/// both are null there and the bench renders the player's own position instead of inventing one.
/// </remarks>
/// <param name="SlotNumber">The slot number, 1–18.</param>
/// <param name="Designation">The stable code of the designation: <c>starter</c> or <c>substitute</c>.</param>
/// <param name="PositionFamily">The slot's position family code, or null for a substitute slot.</param>
/// <param name="Role">The slot's role code, or null for a substitute slot.</param>
/// <param name="Player">The player in the slot, or null when it is empty.</param>
public sealed record TeamSheetSlotResponse(
    int SlotNumber,
    string Designation,
    string? PositionFamily,
    string? Role,
    AssignedPlayerResponse? Player);

/// <summary>
/// Everything the prepare-match screen reads for one fixture and one club (master plan §11.1).
/// </summary>
/// <remarks>
/// One response for the whole screen: the opponent and deadlines, the plan the sheet is prepared from,
/// the club's current selection across all eighteen slots, and the squad it may pick from. The club is the
/// caller's own, resolved from their tenure rather than from the request. When the club has no default
/// plan, <see cref="PlanId"/> is null and the screen offers the tactics screen instead of a selection.
/// </remarks>
/// <param name="FixtureId">The fixture.</param>
/// <param name="ClubId">The caller's club.</param>
/// <param name="ClubName">The caller's club name.</param>
/// <param name="ClubShortName">The caller's club abbreviation.</param>
/// <param name="OpponentClubId">The other club.</param>
/// <param name="OpponentName">The other club's name.</param>
/// <param name="OpponentShortName">The other club's abbreviation.</param>
/// <param name="Venue">The caller's side: <c>home</c> or <c>away</c>.</param>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="RoundNumber">The round number, 1–34.</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="LockAt">When the sheet locks, thirty minutes before kickoff (`CAL-3`).</param>
/// <param name="FixtureStatus">The fixture's lifecycle state.</param>
/// <param name="IsLocked">Whether the sheet can no longer be changed (`SQ-7`).</param>
/// <param name="PlanId">The default plan the sheet is prepared from, or null when the club has none.</param>
/// <param name="PlanName">The default plan's name.</param>
/// <param name="FormationPreset">The default plan's formation preset code.</param>
/// <param name="PlanVersion">The default plan's version.</param>
/// <param name="SheetVersion">
/// The saved sheet's version, returned as the strong entity tag. Null when no sheet has been saved yet,
/// in which case the first save carries no <c>If-Match</c> (`CONC-1`, ADR-0009).
/// </param>
/// <param name="SheetStatus">The saved sheet's status: <c>draft</c>, <c>locked</c>, or <c>draft</c> when unsaved.</param>
/// <param name="Slots">All eighteen slots, filled or empty, in slot order.</param>
/// <param name="SelectablePlayers">The players who may be selected, with their availability.</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record FixtureTeamSheetResponse(
    Guid FixtureId,
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    Guid OpponentClubId,
    string OpponentName,
    string OpponentShortName,
    string Venue,
    Guid DivisionId,
    string DivisionName,
    int RoundNumber,
    DateTimeOffset KickoffAt,
    DateTimeOffset LockAt,
    string FixtureStatus,
    bool IsLocked,
    Guid? PlanId,
    string? PlanName,
    string? FormationPreset,
    long? PlanVersion,
    long? SheetVersion,
    string SheetStatus,
    IReadOnlyList<TeamSheetSlotResponse> Slots,
    IReadOnlyList<SelectablePlayerResponse> SelectablePlayers,
    DateTimeOffset ServerTime);

/// <summary>
/// Why a selection was refused, as a validation preview (master plan §10.4).
/// </summary>
/// <remarks>
/// A save that breaks a team-sheet rule answers with this rather than a bare field-error map, because the
/// problems are about the shape of a side: a slot number, a player, or the starting eleven as a whole. The
/// client draws each issue on the slot it concerns.
/// </remarks>
/// <param name="IsValid">Whether the selection is valid.</param>
/// <param name="StarterCount">How many of the eleven starting slots name a player.</param>
/// <param name="SubstituteCount">How many substitutes are named, 0–7.</param>
/// <param name="Issues">Every reason the selection is not valid, in slot order.</param>
public sealed record TeamSheetValidationResponse(
    bool IsValid,
    int StarterCount,
    int SubstituteCount,
    IReadOnlyList<TeamSheetIssueResponse> Issues);

/// <summary>One reason a selection is not valid, in the stable-code vocabulary of the validator.</summary>
/// <param name="Code">The stable issue code, e.g. <c>TEAM_SHEET_INCOMPLETE</c>.</param>
/// <param name="SlotNumber">The slot the issue concerns, when it concerns one.</param>
/// <param name="PlayerId">The player the issue concerns, when it concerns one.</param>
public sealed record TeamSheetIssueResponse(string Code, int? SlotNumber, Guid? PlayerId);
