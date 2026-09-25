using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Competition;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The projection queries the fixture calendar and the prepare-match screen read (master plan §10.5, §11.1).
/// </summary>
/// <remarks>
/// <para>
/// Every query is one trip shaped for one screen, or a small number of them, and none loads an aggregate
/// graph. A division's calendar is two queries plus its clubs; a club's list is one. The fixtures are
/// ordered by their round and then by identity — a UUIDv7, so the generation order — because a fixture list
/// that reshuffled between reads would be a calendar nobody could follow (`TBL-12`).
/// </para>
/// <para>
/// The season a read is measured against is the world's current one, resolved once by
/// <see cref="CurrentSeasonQuery"/>, so a read during the rollover window keeps answering for the season
/// being played rather than for the next one whose rows already exist (`CAL-6`).
/// </para>
/// </remarks>
internal sealed class CompetitionQueries : ICompetitionQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public CompetitionQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<DivisionFixturesSnapshot?> GetDivisionFixturesAsync(
        Guid divisionId,
        CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var header = await (
            from divisionSeason in _dbContext.DivisionSeasons
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join country in _dbContext.Countries on division.CountryId equals country.Id
            join seasonRow in _dbContext.Seasons on divisionSeason.SeasonId equals seasonRow.Id
            where divisionSeason.DivisionId == divisionId && divisionSeason.SeasonId == season.SeasonId
            select new
            {
                DivisionSeason = divisionSeason,
                Division = division,
                Country = country,
                Season = seasonRow,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var clubs = await (
            from entry in _dbContext.ClubSeasonEntries
            join club in _dbContext.Clubs on entry.ClubId equals club.Id
            where entry.DivisionSeasonId == header.DivisionSeason.Id
            orderby club.Name
            select new FixtureClubRow(club.Id, club.Name, club.ShortName))
            .ToListAsync(cancellationToken);

        var matchdays = await _dbContext.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == header.DivisionSeason.Id)
            .OrderBy(matchday => matchday.RoundNumber)
            .ToListAsync(cancellationToken);

        var matchdayIds = matchdays.Select(matchday => matchday.Id).ToList();

        var fixtures = await _dbContext.Fixtures
            .Where(fixture => matchdayIds.Contains(fixture.MatchdayId))
            .OrderBy(fixture => fixture.Id)
            .ToListAsync(cancellationToken);

        var byMatchday = fixtures
            .GroupBy(fixture => fixture.MatchdayId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var matchdayRows = matchdays
            .Select(matchday => new FixtureMatchdayRow(
                matchday.Id,
                matchday.RoundNumber,
                matchday.LockAt,
                matchday.KickoffAt,
                matchday.PublicationStatus,
                byMatchday.TryGetValue(matchday.Id, out var list)
                    ? [.. list.Select(ToRow)]
                    : []))
            .ToList();

        return new DivisionFixturesSnapshot(
            header.Division.Id,
            header.Division.DisplayName,
            header.Division.TierNumber,
            header.Country.Id,
            header.Country.Code,
            header.Country.DisplayName,
            header.Season.SequenceNumber,
            header.Season.DisplayLabel,
            clubs,
            matchdayRows);
    }

    /// <inheritdoc />
    public async Task<ClubFixturesSnapshot?> GetClubFixturesAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var club = await _dbContext.Clubs
            .Where(candidate => candidate.Id == clubId)
            .Select(candidate => new { candidate.Id, candidate.Name, candidate.ShortName })
            .FirstOrDefaultAsync(cancellationToken);

        if (club is null)
        {
            return null;
        }

        var entry = await (
            from seasonEntry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on seasonEntry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join seasonRow in _dbContext.Seasons on divisionSeason.SeasonId equals seasonRow.Id
            where seasonEntry.ClubId == clubId && divisionSeason.SeasonId == season.SeasonId
            select new
            {
                DivisionSeason = divisionSeason,
                Division = division,
                Season = seasonRow,
            })
            .FirstOrDefaultAsync(cancellationToken);

        // The club exists — this is the season it plays in — so a missing entry means it has no division
        // this season, which is a state the seeder does not produce and rollover does not leave behind.
        if (entry is null)
        {
            return null;
        }

        var rows = await (
            from fixture in _dbContext.Fixtures
            join matchday in _dbContext.Matchdays on fixture.MatchdayId equals matchday.Id
            join home in _dbContext.Clubs on fixture.HomeClubId equals home.Id
            join away in _dbContext.Clubs on fixture.AwayClubId equals away.Id
            where matchday.DivisionSeasonId == entry.DivisionSeason.Id
                && (fixture.HomeClubId == clubId || fixture.AwayClubId == clubId)
            orderby matchday.RoundNumber
            select new { Fixture = fixture, Matchday = matchday, Home = home, Away = away })
            .ToListAsync(cancellationToken);

        var fixtures = rows
            .Select(row =>
            {
                var isHome = row.Fixture.HomeClubId == clubId;
                var opponent = isHome ? row.Away : row.Home;

                return new ClubFixtureRow(
                    row.Fixture.Id,
                    row.Matchday.RoundNumber,
                    isHome,
                    opponent.Id,
                    opponent.Name,
                    opponent.ShortName,
                    row.Fixture.KickoffAt,
                    row.Matchday.LockAt,
                    row.Fixture.Status,
                    row.Fixture.HomeScore,
                    row.Fixture.AwayScore,
                    row.Fixture.MatchId);
            })
            .ToList();

        return new ClubFixturesSnapshot(
            club.Id,
            club.Name,
            club.ShortName,
            entry.Division.Id,
            entry.Division.DisplayName,
            entry.Division.TierNumber,
            entry.Season.SequenceNumber,
            entry.Season.DisplayLabel,
            fixtures);
    }

    /// <inheritdoc />
    public async Task<FixtureDetailSnapshot?> GetFixtureAsync(
        Guid fixtureId,
        CancellationToken cancellationToken)
    {
        var row = await (
            from fixture in _dbContext.Fixtures
            join matchday in _dbContext.Matchdays on fixture.MatchdayId equals matchday.Id
            join divisionSeason in _dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join country in _dbContext.Countries on division.CountryId equals country.Id
            join home in _dbContext.Clubs on fixture.HomeClubId equals home.Id
            join away in _dbContext.Clubs on fixture.AwayClubId equals away.Id
            where fixture.Id == fixtureId
            select new
            {
                Fixture = fixture,
                Matchday = matchday,
                Division = division,
                Country = country,
                Home = home,
                Away = away,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new FixtureDetailSnapshot(
            row.Fixture.Id,
            row.Matchday.Id,
            row.Division.Id,
            row.Division.DisplayName,
            row.Division.TierNumber,
            row.Country.Code,
            row.Matchday.RoundNumber,
            row.Matchday.LockAt,
            row.Fixture.KickoffAt,
            row.Fixture.Status,
            new FixtureSideRow(
                row.Home.Id,
                row.Home.Name,
                row.Home.ShortName,
                row.Home.City,
                row.Home.Region),
            new FixtureSideRow(
                row.Away.Id,
                row.Away.Name,
                row.Away.ShortName,
                row.Away.City,
                row.Away.Region),
            row.Fixture.HomeScore,
            row.Fixture.AwayScore,
            row.Fixture.MatchId);
    }

    private static FixtureRow ToRow(Domain.Competition.Fixture fixture) =>
        new(
            fixture.Id,
            fixture.HomeClubId,
            fixture.AwayClubId,
            fixture.KickoffAt,
            fixture.Status,
            fixture.HomeScore,
            fixture.AwayScore,
            fixture.MatchId);
}
