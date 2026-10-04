using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.Squad.Training;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>One player as the training screen reads them.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="PrimaryPosition">The player's position.</param>
/// <param name="BirthGameYear">The game year the player was born in, which fixes their age (`TIME-3`).</param>
/// <param name="State">The player's state, in basis points (`TRN-5`…`TRN-7`).</param>
/// <param name="Attributes">The player's twenty-eight attributes (`TRN-4`).</param>
/// <param name="Programme">The programme the manager chose, or null when the player trains the position default (`TRN-1`).</param>
/// <param name="FocusVersion">The override's version, or null when there is none.</param>
public sealed record TrainingPlayerRow(
    Guid Id,
    string FullName,
    string ShortName,
    PlayerPosition PrimaryPosition,
    int BirthGameYear,
    SquadStateRow State,
    PlayerAttributeSet Attributes,
    TrainingProgramme? Programme,
    long? FocusVersion);

/// <summary>A club's training plan as the screen reads it (`TRN-1`).</summary>
/// <param name="Intensity">How hard the club trains.</param>
/// <param name="EffectiveDate">The date the plan took effect from.</param>
/// <param name="Version">The plan version, returned as the strong entity tag.</param>
public sealed record TrainingPlanRow(
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
/// <param name="Players">The club's contracted players, with their attributes and programme overrides.</param>
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
/// squad with each player's attributes and programme override, so the screen makes one round trip. State comes back in basis points
/// and positions as domain values, because the conversion `TRN-8` requires belongs to the application
/// mapper.
/// </remarks>
public interface ITrainingQueries
{
    /// <summary>Reads a club's training plan and squad, or null if the club is unknown.</summary>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TrainingSnapshot?> GetTrainingAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one player's training regime and their most recent progression days, or null when the player
    /// holds no active contract (`TRN-17`).
    /// </summary>
    /// <param name="playerId">The player to read.</param>
    /// <param name="days">How many of the most recent progression days to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlayerTrainingSnapshot?> GetPlayerTrainingAsync(
        Guid playerId,
        int days,
        CancellationToken cancellationToken);
}

/// <summary>One progression day of a player's training history.</summary>
/// <param name="Day">The progression day.</param>
/// <param name="Programme">The programme trained that day.</param>
/// <param name="Intensity">The club intensity that day.</param>
/// <param name="DevelopmentMilli">The development the day earned, in thousandths of a point.</param>
/// <param name="DeclineMilli">The decline the day incurred, in thousandths of a point.</param>
/// <param name="PointsGained">The whole points gained that day.</param>
/// <param name="PointsLost">The whole points lost that day.</param>
/// <param name="AttributeChanges">The per-attribute changes, in canonical attribute order.</param>
/// <param name="ProgressChanges">
/// How much each attribute's progress moved, in millionths of a point and canonical order; empty for a day
/// recorded before progress was tracked per attribute.
/// </param>
public sealed record PlayerTrainingDayRow(
    DateOnly Day,
    TrainingProgramme Programme,
    TrainingIntensity Intensity,
    int DevelopmentMilli,
    int DeclineMilli,
    int PointsGained,
    int PointsLost,
    IReadOnlyList<AttributeChange> AttributeChanges,
    IReadOnlyList<AttributeProgressChange> ProgressChanges);

/// <summary>One player's training regime and recent history as the player page reads it.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="ClubId">The club the player holds an active contract with, which the read is authorized against.</param>
/// <param name="PrimaryPosition">The player's position, which fixes their default programme.</param>
/// <param name="Programme">The programme the manager chose, or null when the player trains the position default.</param>
/// <param name="Intensity">The club's intensity, or the implicit default when it has no plan.</param>
/// <param name="Days">The most recent progression days, oldest first.</param>
public sealed record PlayerTrainingSnapshot(
    Guid PlayerId,
    Guid ClubId,
    PlayerPosition PrimaryPosition,
    TrainingProgramme? Programme,
    TrainingIntensity Intensity,
    IReadOnlyList<PlayerTrainingDayRow> Days);
