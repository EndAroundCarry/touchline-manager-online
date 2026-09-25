using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>The player assigned to a slot, as the pitch renders them.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation shown on the pitch.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="SecondaryPositions">The other positions the player covers.</param>
/// <param name="IsUnavailable">Whether the player has an open injury or suspension.</param>
public sealed record TacticsAssignedPlayerRow(
    Guid Id,
    string FullName,
    string ShortName,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    bool IsUnavailable);

/// <summary>One slot of a saved plan.</summary>
/// <param name="SlotNumber">The slot number, 1–11.</param>
/// <param name="PositionFamily">The family the slot asks for.</param>
/// <param name="Role">The role the slot asks for.</param>
/// <param name="NormalizedX">The normalized depth, 0–10,000.</param>
/// <param name="NormalizedY">The normalized width, 0–10,000.</param>
/// <param name="AssignedPlayer">The player in the slot, or null when it is empty.</param>
public sealed record TacticsSlotRow(
    int SlotNumber,
    PositionFamily PositionFamily,
    PlayerRole Role,
    int NormalizedX,
    int NormalizedY,
    TacticsAssignedPlayerRow? AssignedPlayer);

/// <summary>A saved tactical plan.</summary>
/// <param name="Id">The plan identity.</param>
/// <param name="Name">The manager-facing name.</param>
/// <param name="FormationPreset">The formation preset.</param>
/// <param name="Instructions">The eight team instructions.</param>
/// <param name="IsDefault">Whether this is the club's default plan.</param>
/// <param name="Version">The plan version.</param>
/// <param name="Slots">The plan's slots, in slot order.</param>
public sealed record TacticsPlanRow(
    Guid Id,
    string Name,
    FormationPreset FormationPreset,
    TeamInstructionSet Instructions,
    bool IsDefault,
    long Version,
    IReadOnlyList<TacticsSlotRow> Slots);

/// <summary>A player who may be assigned to a slot.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation shown on the pitch.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="SecondaryPositions">The other positions the player covers.</param>
/// <param name="IsUnavailable">Whether the player has an open injury or suspension.</param>
public sealed record TacticsSelectablePlayerRow(
    Guid Id,
    string FullName,
    string ShortName,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    bool IsUnavailable);

/// <summary>Everything the tactics screen reads for one club (master plan §10.4, §11.1).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="ClubShortName">The club abbreviation.</param>
/// <param name="CountryCode">The club's country code.</param>
/// <param name="SeasonNumber">The season the plans are read against.</param>
/// <param name="Plans">The club's saved plans, the default first.</param>
/// <param name="SelectablePlayers">The players who may be assigned to a slot.</param>
public sealed record TacticsSnapshot(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string CountryCode,
    int SeasonNumber,
    IReadOnlyList<TacticsPlanRow> Plans,
    IReadOnlyList<TacticsSelectablePlayerRow> SelectablePlayers);

/// <summary>
/// The read side of the tactics screen.
/// </summary>
/// <remarks>
/// One query for one screen, like the squad reads: it gathers the plans, the squad they are picked from,
/// and the club's identity, so the tactics screen makes one round trip rather than four. Selection
/// facts come back as domain values rather than transport shapes, and the conversion `TRN-8` requires
/// stays in the application mapper.
/// </remarks>
public interface ITacticsQueries
{
    /// <summary>Reads a club's plans and selectable players, or null if the club is unknown.</summary>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TacticsSnapshot?> GetTacticsAsync(Guid clubId, CancellationToken cancellationToken);
}
