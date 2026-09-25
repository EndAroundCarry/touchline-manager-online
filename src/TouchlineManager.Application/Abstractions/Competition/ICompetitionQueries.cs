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
public sealed record FixtureRow(
    Guid Id,
    Guid HomeClubId,
    Guid AwayClubId,
    DateTimeOffset KickoffAt,
    FixtureStatus Status,
    int? HomeScore,
    int? AwayScore,
    Guid? MatchId);

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
    Guid? MatchId);

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
    Guid? MatchId);

/// <summary>
/// The read side of the competition module's fixture calendar (master plan §10.5, §11.1).
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
    /// <summary>Reads a division's whole fixture calendar, or null if the division is unknown.</summary>
    /// <param name="divisionId">The division to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DivisionFixturesSnapshot?> GetDivisionFixturesAsync(
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
}
