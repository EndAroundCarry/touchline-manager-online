using Tactical11Manager.Domain.Fans;

namespace Tactical11Manager.Application.Abstractions.Fans;

/// <summary>One club's place in the season being played, as the weekly review and a takeover read it.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="DivisionSeasonId">The division-season it plays in.</param>
/// <param name="SeasonId">The season.</param>
/// <param name="TierNumber">The tier of the division, starting at 1.</param>
/// <param name="Played">The league games it has played, from the published table; 0 before the first round.</param>
/// <param name="Points">The points it has taken.</param>
/// <param name="Rank">Its place in the published table, or 0 before the first round.</param>
public sealed record FanSeasonClubRow(
    Guid ClubId,
    Guid DivisionSeasonId,
    Guid SeasonId,
    int TierNumber,
    int Played,
    int Points,
    int Rank);

/// <summary>A club's fans as the Fan Base page reads them.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="Name">The club's name.</param>
/// <param name="TierNumber">The tier it plays in this season, or 1 when it has no entry in the season.</param>
/// <param name="Fans">The fans it has. A club whose fans were never opened reads as a new club of its tier.</param>
/// <param name="MoodBp">How satisfied they are, in basis points.</param>
/// <param name="SeasonTargetRank">The place the fans expect this season, or null before it is set.</param>
/// <param name="LastWeekGap">The gap at the last weekly review, or null before the first.</param>
/// <param name="CurrentSeasonId">The world's current season, or null before a world exists.</param>
/// <param name="CurrentSeasonNumber">The current season's sequence number, from 1, or 0 when there is none.</param>
public sealed record FanClubRow(
    Guid ClubId,
    string Name,
    int TierNumber,
    int Fans,
    int MoodBp,
    int? SeasonTargetRank,
    int? LastWeekGap,
    Guid? CurrentSeasonId,
    int CurrentSeasonNumber);

/// <summary>One entry of a club's fan history, with the season it belongs to named by number.</summary>
/// <param name="Id">The event identity.</param>
/// <param name="Kind">What moved the fans.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="SeasonId">The season.</param>
/// <param name="SeasonNumber">The season's sequence number, from 1.</param>
/// <param name="FixtureId">The match, on a match event.</param>
/// <param name="FansBefore">The fans before the change.</param>
/// <param name="FansAfter">The fans after the change.</param>
/// <param name="MoodBeforeBp">The mood before the change.</param>
/// <param name="MoodAfterBp">The mood after the change.</param>
/// <param name="ExpectedPointsCentis">The points expected, on a match event.</param>
/// <param name="PointsWon">The points taken, on a match event.</param>
/// <param name="Gap">The gap to the target, on a review or a season reset.</param>
public sealed record FanEventRow(
    Guid Id,
    FanEventKind Kind,
    DateTimeOffset OccurredAt,
    Guid SeasonId,
    int SeasonNumber,
    Guid? FixtureId,
    int FansBefore,
    int FansAfter,
    int MoodBeforeBp,
    int MoodAfterBp,
    int? ExpectedPointsCentis,
    int? PointsWon,
    int? Gap);

/// <summary>
/// The reads the fan module is built from: where each club stands, how strong its squad is, and how its season has gone (`FAN-3`, `FAN-8`).
/// </summary>
public interface IFanQueries
{
    /// <summary>Lists every club in the world's current season, with its place in the table.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<FanSeasonClubRow>> ListCurrentSeasonAsync(CancellationToken cancellationToken);

    /// <summary>Lists the clubs that play in the same division as a club in the world's current season.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The division's clubs, including the club itself, or none when the club has no entry in the season.</returns>
    Task<IReadOnlyList<FanSeasonClubRow>> ListDivisionOfClubAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Reads the squad strength of some clubs (`FAN-3`).</summary>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Each club's <c>TeamStrength</c>, in hundredths of an attribute point. A club with no players reads 0.</returns>
    Task<IReadOnlyDictionary<Guid, int>> GetSquadStrengthsAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken);

    /// <summary>Reads how each club in a division-season has done so far, from its published table (`FAN-3`).</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, FanForm>> GetFormAsync(Guid divisionSeasonId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads how some clubs have done so far in their own leagues this season, from their published tables. A promotion play-off match
    /// is played by clubs of two leagues, so each side is read in its own (`FAN-3`).
    /// </summary>
    /// <param name="seasonId">The season.</param>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, FanForm>> GetSeasonFormAsync(
        Guid seasonId,
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken);

    /// <summary>Reads the tier of a division.</summary>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tier number, starting at 1, or null when the division does not exist.</returns>
    Task<int?> GetDivisionTierAsync(Guid divisionId, CancellationToken cancellationToken);

    /// <summary>Finds the clubs among some that are in the first season they have ever played, which earns the opening boost (`FAN-6`).</summary>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlySet<Guid>> GetFirstSeasonClubIdsAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken);

    /// <summary>Reads a club's fans for the Fan Base page and the public summary (`FAN-1`, `FAN-8`).</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The club's fans, or null when no such club exists.</returns>
    Task<FanClubRow?> GetClubAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Lists a club's whole fan history, oldest first (`FAN-10`).</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<FanEventRow>> ListEventsAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Reads what a club's fans expected from some fixtures when the team sheets locked (`FAN-4`).</summary>
    /// <param name="clubId">The club whose fans held the expectation.</param>
    /// <param name="fixtureIds">The fixtures.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The expectations that were frozen, by fixture. A fixture locked before fans existed has none.</returns>
    Task<IReadOnlyDictionary<Guid, FanExpectation>> GetFrozenExpectationsAsync(
        Guid clubId,
        IReadOnlyCollection<Guid> fixtureIds,
        CancellationToken cancellationToken);
}
