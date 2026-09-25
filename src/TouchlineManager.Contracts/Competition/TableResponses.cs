namespace TouchlineManager.Contracts.Competition;

/// <summary>One club's line in a division's table (master plan §10.5, `TBL-1`…`TBL-10`).</summary>
/// <param name="Rank">The 1-based position, which is the order the rows arrive in.</param>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's name.</param>
/// <param name="ClubShortName">The club's abbreviation, for a narrow screen.</param>
/// <param name="Played">Fixtures played.</param>
/// <param name="Won">Fixtures won.</param>
/// <param name="Drawn">Fixtures drawn.</param>
/// <param name="Lost">Fixtures lost.</param>
/// <param name="GoalsFor">Goals scored.</param>
/// <param name="GoalsAgainst">Goals conceded.</param>
/// <param name="GoalDifference">Goals scored minus goals conceded (`TBL-3`).</param>
/// <param name="Points">Points: three a win, one a draw (`TBL-1`).</param>
/// <param name="YellowCards">Yellow cards accumulated (`TBL-9`).</param>
/// <param name="RedCards">Red cards accumulated (`TBL-8`).</param>
public sealed record DivisionTableRowResponse(
    int Rank,
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points,
    int YellowCards,
    int RedCards);

/// <summary>A division's table for the season in progress.</summary>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's name.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's code.</param>
/// <param name="CountryName">The country's name.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="Rows">Every club's line, best first.</param>
/// <param name="ServerTime">
/// The server's current instant, so a client with a wrong clock shows the right "as of" (`TIME-5`).
/// </param>
public sealed record DivisionTableResponse(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<DivisionTableRowResponse> Rows,
    DateTimeOffset ServerTime);
