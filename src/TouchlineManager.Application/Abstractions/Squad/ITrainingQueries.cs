using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>One player as the training screen reads them.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="BirthGameYear">The game year the player was born in, which fixes their age (`TIME-3`).</param>
/// <param name="State">The player's state, in basis points (`TRN-5`…`TRN-7`).</param>
/// <param name="FocusFamily">The player's individual focus, or null when they have none (`TRN-2`).</param>
/// <param name="FocusVersion">The focus's version, or null when there is none.</param>
public sealed record TrainingPlayerRow(
    Guid Id,
    string FullName,
    string ShortName,
    PlayerPosition PrimaryPosition,
    int BirthGameYear,
    SquadStateRow State,
    AttributeFamily? FocusFamily,
    long? FocusVersion);

/// <summary>A club's training plan as the screen reads it (`TRN-1`).</summary>
/// <param name="TeamFocus">The club-wide focus.</param>
/// <param name="Intensity">How hard the club trains.</param>
/// <param name="EffectiveDate">The date the plan took effect from.</param>
/// <param name="Version">The plan version, returned as the strong entity tag.</param>
public sealed record TrainingPlanRow(
    TrainingFocus TeamFocus,
    TrainingIntensity Intensity,
    DateOnly EffectiveDate,
    long Version);

/// <summary>Everything the training screen reads for one club (master plan §10.4, §11.1).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="ClubShortName">The club abbreviation.</param>
/// <param name="CountryCode">The club's country code.</param>
/// <param name="SeasonNumber">The season the plan is read against.</param>
/// <param name="GameYear">The season's game year, which fixes each player's age.</param>
/// <param name="Plan">The club's training plan, or null when it has none.</param>
/// <param name="Players">The club's contracted players, with their individual focuses.</param>
public sealed record TrainingSnapshot(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string CountryCode,
    int SeasonNumber,
    int GameYear,
    TrainingPlanRow? Plan,
    IReadOnlyList<TrainingPlayerRow> Players);

/// <summary>
/// The read side of the training screen.
/// </summary>
/// <remarks>
/// One query for one screen, like the squad and tactics reads: the plan, the club's identity, and the
/// squad with each player's focus, so the screen makes one round trip. State comes back in basis points
/// and positions as domain values, because the conversion `TRN-8` requires belongs to the application
/// mapper.
/// </remarks>
public interface ITrainingQueries
{
    /// <summary>Reads a club's training plan and squad, or null if the club is unknown.</summary>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TrainingSnapshot?> GetTrainingAsync(Guid clubId, CancellationToken cancellationToken);
}
