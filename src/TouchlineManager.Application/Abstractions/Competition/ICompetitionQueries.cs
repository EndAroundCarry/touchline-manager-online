using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Abstractions.Competition;

/// <summary>One club as a fixture list names it.</summary>
/// <param name="Id">The club identity.</param>
/// <param name="Name">The generated club name.</param>
/// <param name="ShortName">The abbreviation.</param>
public sealed record FixtureClubRow(Guid Id, string Name, string ShortName);

/// <summary>One fixture, as stored.</summary>
/// <param name="Id">The fixture identity.</param>
/// <param name="HomeClubId">The host.</param>
/// <param name="AwayClubId">The visitor.</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="HomeScore">The home score, once a result exists.</param>
/// <param name="AwayScore">The away score, once a result exists.</param>
/// <param name="MatchId">The simulated match, once a result is staged.</param>
/// <param name="IsBootstrap">Whether the result is generated history a provisioned tier backfilled (`PYR-7`).</param>
public sealed record FixtureRow(
    Guid Id,
    Guid HomeClubId,
    Guid AwayClubId,
    DateTimeOffset KickoffAt,
    FixtureStatus Status,
    int? HomeScore,
    int? AwayScore,
    Guid? MatchId,
    bool IsBootstrap);

/// <summary>One round of a division's season, with the fixtures it comprises (`CAL-10`).</summary>
/// <param name="Id">The matchday identity.</param>
/// <param name="RoundNumber">The round number, 1–34.</param>
/// <param name="LockAt">When team sheets lock (`CAL-3`).</param>
/// <param name="KickoffAt">The round's kickoff (`CAL-2`).</param>
/// <param name="PublicationStatus">Whether the round is pending, staged, or published (`MAT-7`).</param>
/// <param name="Fixtures">The round's fixtures, in a stable order.</param>
public sealed record FixtureMatchdayRow(
    Guid Id,
    int RoundNumber,
    DateTimeOffset LockAt,
    DateTimeOffset KickoffAt,
    MatchdayPublicationStatus PublicationStatus,
    IReadOnlyList<FixtureRow> Fixtures);

/// <summary>A division-season's whole fixture calendar (master plan §10.5).</summary>
/// <param name="DivisionId">The division identity.</param>
/// <param name="DivisionName">The generated tier name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="CountryId">The owning country.</param>
/// <param name="CountryCode">The country's stable code.</param>
/// <param name="CountryName">The country's display name.</param>
/// <param name="SeasonNumber">The season sequence number.</param>
/// <param name="SeasonLabel">The season label.</param>
/// <param name="Clubs">Every club in the division.</param>
/// <param name="Matchdays">The season's matchdays, in round order.</param>
public sealed record DivisionFixturesSnapshot(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<FixtureClubRow> Clubs,
    IReadOnlyList<FixtureMatchdayRow> Matchdays);

/// <summary>One of a club's fixtures, from that club's point of view.</summary>
/// <param name="Id">The fixture identity.</param>
/// <param name="RoundNumber">The round number, 1–34.</param>
/// <param name="IsHome">Whether the club hosts.</param>
/// <param name="OpponentClubId">The other club.</param>
/// <param name="OpponentName">The other club's name.</param>
/// <param name="OpponentShortName">The other club's abbreviation.</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="LockAt">When team sheets lock.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="HomeScore">The home score, once a result exists.</param>
/// <param name="AwayScore">The away score, once a result exists.</param>
/// <param name="MatchId">The simulated match, once a result is staged.</param>
/// <param name="IsBootstrap">Whether the result is generated history a provisioned tier backfilled (`PYR-7`).</param>
public sealed record ClubFixtureRow(
    Guid Id,
    int RoundNumber,
    bool IsHome,
    Guid OpponentClubId,
    string OpponentName,
    string OpponentShortName,
    DateTimeOffset KickoffAt,
    DateTimeOffset LockAt,
    FixtureStatus Status,
    int? HomeScore,
    int? AwayScore,
    Guid? MatchId,
    bool IsBootstrap);

/// <summary>A club's season fixture list (master plan §11.1).</summary>
/// <param name="ClubId">The managed club.</param>
/// <param name="ClubName">The managed club's name.</param>
/// <param name="ClubShortName">The managed club's abbreviation.</param>
/// <param name="DivisionId">The division the club plays in.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="SeasonNumber">The season sequence number.</param>
/// <param name="SeasonLabel">The season label.</param>
/// <param name="Fixtures">The club's fixtures, in round order.</param>
public sealed record ClubFixturesSnapshot(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<ClubFixtureRow> Fixtures);

/// <summary>One side of a fixture, with the club's generated identity.</summary>
/// <param name="ClubId">The club identity.</param>
/// <param name="Name">The generated club name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="City">The generated home city.</param>
/// <param name="Region">The generated region.</param>
public sealed record FixtureSideRow(
    Guid ClubId,
    string Name,
    string ShortName,
    string City,
    string Region);

/// <summary>Everything the prepare-match screen reads about the fixture itself (master plan §11.1).</summary>
/// <param name="Id">The fixture identity.</param>
/// <param name="MatchdayId">The round the fixture belongs to.</param>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="CountryCode">The country's stable code.</param>
/// <param name="RoundNumber">The round number, 1–34.</param>
/// <param name="LockAt">When team sheets lock (`CAL-3`).</param>
/// <param name="KickoffAt">The kickoff instant, in UTC.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="Home">The host.</param>
/// <param name="Away">The visitor.</param>
/// <param name="HomeScore">The home score, once a result exists.</param>
/// <param name="AwayScore">The away score, once a result exists.</param>
/// <param name="MatchId">The simulated match, once a result is staged.</param>
/// <param name="IsBootstrap">Whether the result is generated history a provisioned tier backfilled (`PYR-7`).</param>
public sealed record FixtureDetailSnapshot(
    Guid Id,
    Guid MatchdayId,
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    string CountryCode,
    int RoundNumber,
    DateTimeOffset LockAt,
    DateTimeOffset KickoffAt,
    FixtureStatus Status,
    FixtureSideRow Home,
    FixtureSideRow Away,
    int? HomeScore,
    int? AwayScore,
    Guid? MatchId,
    bool IsBootstrap);

/// <summary>One row of a division's table, in the order the division is ranked (`TBL-10`).</summary>
/// <param name="Rank">The 1-based position.</param>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="ClubShortName">The club's abbreviation.</param>
/// <param name="Played">Fixtures played.</param>
/// <param name="Won">Fixtures won.</param>
/// <param name="Drawn">Fixtures drawn.</param>
/// <param name="Lost">Fixtures lost.</param>
/// <param name="GoalsFor">Goals scored.</param>
/// <param name="GoalsAgainst">Goals conceded.</param>
/// <param name="Points">Points: three a win, one a draw (`TBL-1`).</param>
/// <param name="YellowCards">Yellow cards accumulated (`TBL-9`).</param>
/// <param name="RedCards">Red cards accumulated (`TBL-8`).</param>
public sealed record DivisionTableRow(
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
    int Points,
    int YellowCards,
    int RedCards);

/// <summary>A division's table for the season in progress.</summary>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's code.</param>
/// <param name="CountryName">The country's name.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="Rows">Every club's line, best first.</param>
public sealed record DivisionTableSnapshot(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<DivisionTableRow> Rows);

/// <summary>One player's season statistics in a division, most goals first (`STA-1`…`STA-4`).</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="PlayerName">The player's generated name.</param>
/// <param name="ClubId">The club the player appeared for.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="ClubShortName">The club's abbreviation.</param>
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
/// <param name="AverageRatingBasisPoints">The average match rating, or null before the player is rated.</param>
public sealed record DivisionPlayerStatRow(
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
    int? AverageRatingBasisPoints);

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
public sealed record DivisionStatisticsSnapshot(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<DivisionPlayerStatRow> Rows);

/// <summary>
/// One player's discipline in a division-season: the cards they have been shown and any suspension they
/// still owe (`DIS-2`, `DIS-4`, `DIS-5`).
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="PlayerName">The player's generated name.</param>
/// <param name="ClubId">The club the player plays for.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="ClubShortName">The club's abbreviation.</param>
/// <param name="YellowCards">Bookings accumulated (`DIS-2`).</param>
/// <param name="RedCards">Sendings-off accumulated (`DIS-4`).</param>
/// <param name="SuspensionFixturesRemaining">
/// How many fixtures the player still misses through suspension, or zero when they owe none (`DIS-5`).
/// </param>
public sealed record DivisionDisciplineRow(
    Guid PlayerId,
    string PlayerName,
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    int YellowCards,
    int RedCards,
    int SuspensionFixturesRemaining);

/// <summary>
/// A division's discipline for the season in progress: every player carrying a card, and any suspension
/// they still owe (master plan §6.4, `DIS-2`…`DIS-5`).
/// </summary>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's code.</param>
/// <param name="CountryName">The country's name.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="Rows">Every player with a card this season, most sendings-off first.</param>
public sealed record DivisionDisciplineSnapshot(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<DivisionDisciplineRow> Rows);

/// <summary>One club's place in a season's tie-break draw (`TBL-10`, `TBL-11`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="ClubShortName">The club's abbreviation.</param>
/// <param name="DrawKey">The key the final tie-breaker compares, derived from the stored seed.</param>
public sealed record DivisionRulesClubRow(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string DrawKey);

/// <summary>A division's competition rules for the season in progress (`TBL-1`…`TBL-11`).</summary>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's stable code.</param>
/// <param name="CountryName">The country's display name.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="TieDrawSeed">The stored seed the season's final tie-break draw derives from (`TBL-11`).</param>
/// <param name="TieDrawHash">The published digest of that seed (`TBL-11`).</param>
/// <param name="Clubs">Every club in the division, with its draw key.</param>
public sealed record DivisionRulesSnapshot(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    string TieDrawSeed,
    string TieDrawHash,
    IReadOnlyList<DivisionRulesClubRow> Clubs);

/// <summary>How a club arrived in a season: the stable codes a history read reports (`PR-4`).</summary>
public static class SeasonMovements
{
    /// <summary>The club was promoted into the season.</summary>
    public const string Promoted = "promoted";

    /// <summary>The club was relegated into the season.</summary>
    public const string Relegated = "relegated";

    /// <summary>The club stayed in the same tier.</summary>
    public const string None = "none";
}

/// <summary>One season a club has finished (`PR-4`, master plan §11.1).</summary>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="TierNumber">The tier the club played in.</param>
/// <param name="DivisionName">The tier's generated name.</param>
/// <param name="FinalRank">The club's final position, 1–18.</param>
/// <param name="Promoted">Whether the club was promoted at that rollover.</param>
/// <param name="Relegated">Whether the club was relegated at that rollover.</param>
/// <param name="ClosingCashMinor">The club's cash at the season's end, in minor units.</param>
/// <param name="ClosingReputation">The club's reputation at the season's end, on the 1–100 scale.</param>
public sealed record ClubSeasonHistoryRow(
    int SeasonNumber,
    string SeasonLabel,
    int TierNumber,
    string DivisionName,
    int FinalRank,
    bool Promoted,
    bool Relegated,
    long ClosingCashMinor,
    int ClosingReputation);

/// <summary>The next season a club is already placed in (`PR-5`).</summary>
/// <param name="SeasonNumber">The next season's ordinal in the world.</param>
/// <param name="SeasonLabel">The next season's display label.</param>
/// <param name="StartsAt">When the next season's first matchday is.</param>
/// <param name="DivisionName">The tier the club is placed in.</param>
/// <param name="TierNumber">The tier number.</param>
/// <param name="Movement">How the club arrived: <c>promoted</c>, <c>relegated</c>, or <c>none</c>.</param>
public sealed record NextSeasonSummary(
    int SeasonNumber,
    string SeasonLabel,
    DateTimeOffset StartsAt,
    string DivisionName,
    int TierNumber,
    string Movement);

/// <summary>A club's finished seasons, newest first, with the next season when one is known.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="ClubShortName">The club's abbreviation.</param>
/// <param name="Seasons">Every season the club has finished, most recent first.</param>
/// <param name="NextSeason">Where the club is going next, or null when no next season exists yet.</param>
public sealed record ClubSeasonHistorySnapshot(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    IReadOnlyList<ClubSeasonHistoryRow> Seasons,
    NextSeasonSummary? NextSeason);

/// <summary>
/// The read side of the competition module's fixture calendar and tables (master plan §10.5, §11.1).
/// </summary>
/// <remarks>
/// <para>
/// Projections, not aggregates: one query per screen, none used to make a decision, and none returning a
/// tracked graph. Keeping them off the write repository means a screen can change without widening what a
/// command can reach (`MOD-3`).
/// </para>
/// <para>
/// The order of a matchday's fixtures and of the matchdays themselves is fixed here rather than left to
/// the database, because a fixture list that reshuffles between reads would be a calendar nobody could
/// follow — and `TBL-12` forbids letting row order decide anything that matters.
/// </para>
/// </remarks>
public interface ICompetitionQueries
{
    /// <summary>Reads a division's whole calendar, or null if the division is unknown.</summary>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DivisionFixturesSnapshot?> GetDivisionFixturesAsync(
        Guid divisionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a division's stored table for the season in progress, or null if the division is unknown.
    /// </summary>
    /// <remarks>
    /// The table is read as the projection it is rather than recomputed per request: it is written once per
    /// published matchday, inside the same transaction that publishes the round, so a read can never see a
    /// table that has the round's results only half applied (`MAT-7`, `TBL-13`).
    /// </remarks>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DivisionTableSnapshot?> GetDivisionTableAsync(
        Guid divisionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a division's player season statistics for the season in progress, or null if the division is
    /// unknown.
    /// </summary>
    /// <remarks>
    /// Like the table, the rows are read as the projection they are rather than recomputed per request:
    /// they are advanced once per published matchday, inside the same transaction that publishes the round,
    /// so a read can never see totals with the round's results only half applied (`MAT-7`).
    /// </remarks>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DivisionStatisticsSnapshot?> GetDivisionStatisticsAsync(
        Guid divisionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a division's competition rules for the season in progress, or null if the division is unknown.
    /// </summary>
    /// <remarks>
    /// The points and the ordering are the server's, but the stored draw is the reason this read exists: a
    /// season commits to a tie-break draw before it is played, and a manager must be able to see it, because
    /// an ordering whose final criterion cannot be inspected is indistinguishable from one chosen after the
    /// fact (`TBL-11`). The per-club keys are derived from the stored seed here rather than stored, so they
    /// cannot disagree with it.
    /// </remarks>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DivisionRulesSnapshot?> GetDivisionRulesAsync(
        Guid divisionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a division's discipline for the season in progress, or null if the division is unknown.
    /// </summary>
    /// <remarks>
    /// Every player the season's cards have touched, with what they still owe. The card counts are the
    /// season's accumulation (`DIS-2`, `DIS-4`) and the suspension figure is read from the open absences the
    /// publication serves (`DIS-5`), so the page and the side a manager may actually name cannot disagree
    /// about who is suspended. It is public game data, like the table and the statistics: a card is shown in
    /// front of everybody.
    /// </remarks>
    /// <param name="divisionId">The division.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DivisionDisciplineSnapshot?> GetDivisionDisciplineAsync(
        Guid divisionId,
        CancellationToken cancellationToken);

    /// <summary>Reads a club's season fixtures, or null if the club is unknown.</summary>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClubFixturesSnapshot?> GetClubFixturesAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Reads one fixture in full, or null if it is unknown.</summary>
    /// <param name="fixtureId">The fixture to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FixtureDetailSnapshot?> GetFixtureAsync(Guid fixtureId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a club's finished seasons and, when it is already known, the next season it is placed in, or
    /// null if the club is unknown.
    /// </summary>
    /// <remarks>
    /// History, read from the entries the rollover closed and never rewrote (`PR-6`), so it is the same
    /// figure however long after the season it is read. The next season is the world's current season plus
    /// one: it exists only in the window between the rollover's move phase and the pointer advancing, which
    /// is exactly when a manager wants to see where their club is going.
    /// </remarks>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClubSeasonHistorySnapshot?> GetClubSeasonHistoryAsync(
        Guid clubId,
        CancellationToken cancellationToken);
}
