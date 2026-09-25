using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>The fixture a team sheet is prepared for, with both clubs' generated identities.</summary>
/// <param name="FixtureId">The fixture.</param>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="RoundNumber">The round number, 1–34.</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="LockAt">When the sheet locks (`CAL-3`).</param>
/// <param name="Status">The fixture's lifecycle state.</param>
/// <param name="HomeClubId">The host.</param>
/// <param name="HomeClubName">The host's name.</param>
/// <param name="HomeClubShortName">The host's abbreviation.</param>
/// <param name="AwayClubId">The visitor.</param>
/// <param name="AwayClubName">The visitor's name.</param>
/// <param name="AwayClubShortName">The visitor's abbreviation.</param>
public sealed record TeamSheetFixtureRow(
    Guid FixtureId,
    Guid DivisionId,
    string DivisionName,
    int RoundNumber,
    DateTimeOffset KickoffAt,
    DateTimeOffset LockAt,
    FixtureStatus Status,
    Guid HomeClubId,
    string HomeClubName,
    string HomeClubShortName,
    Guid AwayClubId,
    string AwayClubName,
    string AwayClubShortName);

/// <summary>One slot of the plan a sheet is prepared from (`TAC-7`…`TAC-9`).</summary>
/// <param name="SlotNumber">The slot number, 1–11.</param>
/// <param name="PositionFamily">The family the slot asks for.</param>
/// <param name="Role">The role the slot asks for.</param>
public sealed record TeamSheetPlanSlotRow(int SlotNumber, PositionFamily PositionFamily, PlayerRole Role);

/// <summary>The club's default plan, which gives the eleven starting slots their shape (`INS-11`).</summary>
/// <param name="PlanId">The plan identity.</param>
/// <param name="Name">The manager-facing plan name.</param>
/// <param name="FormationPreset">The formation preset.</param>
/// <param name="Version">The plan version the sheet references.</param>
/// <param name="Slots">The plan's eleven slots, in slot order.</param>
public sealed record TeamSheetPlanRow(
    Guid PlanId,
    string Name,
    FormationPreset FormationPreset,
    long Version,
    IReadOnlyList<TeamSheetPlanSlotRow> Slots);

/// <summary>One player named in a saved sheet, with the details the bench renders.</summary>
/// <param name="SlotNumber">The slot number, 1–18.</param>
/// <param name="PlayerId">The selected player.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="PlayerShortName">The player's abbreviation.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="IsUnavailable">Whether the player has an open injury or suspension.</param>
public sealed record TeamSheetEntryRow(
    int SlotNumber,
    Guid PlayerId,
    string PlayerName,
    string PlayerShortName,
    PlayerPosition PrimaryPosition,
    bool IsUnavailable);

/// <summary>A club's saved selection for one fixture (`SQ-4`).</summary>
/// <param name="SheetId">The sheet identity.</param>
/// <param name="PlanId">The plan the sheet references.</param>
/// <param name="PlanVersion">The plan version the sheet was prepared against.</param>
/// <param name="Status">The sheet's lifecycle state.</param>
/// <param name="Version">The sheet version, returned as the strong entity tag.</param>
/// <param name="Entries">The named players, in slot order.</param>
public sealed record TeamSheetRow(
    Guid SheetId,
    Guid PlanId,
    long PlanVersion,
    TeamSheetStatus Status,
    long Version,
    IReadOnlyList<TeamSheetEntryRow> Entries);

/// <summary>Everything the prepare-match screen reads for one fixture and one club (master plan §11.1).</summary>
/// <param name="Fixture">The fixture, with both clubs.</param>
/// <param name="Plan">The club's default plan, or null when it has none.</param>
/// <param name="Sheet">The club's saved sheet, or null when it has not prepared one yet.</param>
/// <param name="SelectablePlayers">The players who may be selected, with their availability.</param>
public sealed record TeamSheetSnapshot(
    TeamSheetFixtureRow Fixture,
    TeamSheetPlanRow? Plan,
    TeamSheetRow? Sheet,
    IReadOnlyList<TacticsSelectablePlayerRow> SelectablePlayers);

/// <summary>
/// The read side of a fixture team sheet (master plan §10.4, §11.1).
/// </summary>
/// <remarks>
/// One query for the whole prepare-match screen: the fixture with both clubs, the club's default plan,
/// the sheet it has saved, and the squad it may pick from. It answers for the club it is given without
/// deciding whether the caller may see it — the use case establishes that from the caller's tenure, as the
/// squad reads do.
/// </remarks>
public interface ITeamSheetQueries
{
    /// <summary>Reads a club's prepared side for a fixture, or null if the fixture is unknown.</summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="clubId">The club whose side is being read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TeamSheetSnapshot?> GetTeamSheetAsync(
        Guid fixtureId,
        Guid clubId,
        CancellationToken cancellationToken);
}
