namespace TouchlineManager.Contracts.Competition;

/// <summary>One player's season statistics in a division (master plan §10.5, §6.4).</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="PlayerName">The player's generated name.</param>
/// <param name="ClubId">The club the player appeared for.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="ClubShortName">The club's abbreviation, for a narrow screen.</param>
/// <param name="Appearances">Matches the player took the pitch in.</param>
/// <param name="Starts">Matches the player started.</param>
/// <param name="MinutesPlayed">Total minutes played.</param>
/// <param name="Goals">Goals scored.</param>
/// <param name="Assists">Goals set up.</param>
/// <param name="Shots">Shots taken.</param>
/// <param name="ShotsOnTarget">Shots on target.</param>
/// <param name="Saves">Saves made.</param>
/// <param name="YellowCards">Bookings accumulated.</param>
/// <param name="RedCards">Sendings-off accumulated.</param>
/// <param name="AverageRating">
/// The average match rating on a 0.0–10.0 scale, or null before the player has been rated. Converted from
/// the stored basis points in one place (`TRN-8`), so no storage unit reaches a client.
/// </param>
public sealed record DivisionPlayerStatRowResponse(
    Guid PlayerId,
    string PlayerName,
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    int Appearances,
    int Starts,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int Saves,
    int YellowCards,
    int RedCards,
    decimal? AverageRating);

/// <summary>A division's player statistics for the season in progress (master plan §10.5).</summary>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's code.</param>
/// <param name="CountryName">The country's name.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="Rows">Every player with an appearance, most goals first.</param>
/// <param name="ServerTime">
/// The server's current instant, so a client with a wrong clock shows the right "as of" (`TIME-5`).
/// </param>
public sealed record DivisionStatisticsResponse(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<DivisionPlayerStatRowResponse> Rows,
    DateTimeOffset ServerTime);
