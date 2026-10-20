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
}
