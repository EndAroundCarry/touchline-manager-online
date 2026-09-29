using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Squad;

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
    public async Task<DivisionTableSnapshot?> GetDivisionTableAsync(
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
                DivisionSeasonId = divisionSeason.Id,
                division.DisplayName,
                division.TierNumber,
                CountryId = country.Id,
                country.Code,
                CountryName = country.DisplayName,
                SeasonNumber = seasonRow.SequenceNumber,
                SeasonLabel = seasonRow.DisplayLabel,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        // One query for the table and its clubs: the projection stored the rank, so the read never sorts and
        // never needs a tie-break rule (TBL-12, TBL-13).
        var rows = await (
            from standing in _dbContext.Standings
            join club in _dbContext.Clubs on standing.ClubId equals club.Id
            where standing.DivisionSeasonId == header.DivisionSeasonId
            orderby standing.Rank
            select new DivisionTableRow(
                standing.Rank,
                club.Id,
                club.Name,
                club.ShortName,
                standing.Played,
                standing.Won,
                standing.Drawn,
                standing.Lost,
                standing.GoalsFor,
                standing.GoalsAgainst,
                standing.Points,
                standing.YellowCards,
                standing.RedCards))
            .ToListAsync(cancellationToken);

        return new DivisionTableSnapshot(
            divisionId,
            header.DisplayName,
            header.TierNumber,
            header.CountryId,
            header.Code,
            header.CountryName,
            header.SeasonNumber,
            header.SeasonLabel,
            rows);
    }

    /// <inheritdoc />
    public async Task<DivisionStatisticsSnapshot?> GetDivisionStatisticsAsync(
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
                DivisionSeasonId = divisionSeason.Id,
                division.DisplayName,
                division.TierNumber,
                CountryId = country.Id,
                country.Code,
                CountryName = country.DisplayName,
                SeasonNumber = seasonRow.SequenceNumber,
                SeasonLabel = seasonRow.DisplayLabel,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        // The projection stored the totals and the order the leaderboard is read in: goals, then assists,
        // then the player's name, so a screen never sorts and two reads never disagree (TBL-12's principle).
        // The average is computed from the stored sum and count, because it is a function of two columns
        // beside it and storing it would let a row disagree with itself.
        var rows = await (
            from stat in _dbContext.PlayerSeasonStats
            join player in _dbContext.Players on stat.PlayerId equals player.Id
            join club in _dbContext.Clubs on stat.ClubId equals club.Id
            where stat.DivisionSeasonId == header.DivisionSeasonId
            orderby stat.Goals descending, stat.Assists descending, player.FullName
            select new DivisionPlayerStatRow(
                player.Id,
                player.FullName,
                club.Id,
                club.Name,
                club.ShortName,
                stat.Appearances,
                stat.Starts,
                stat.MinutesPlayed,
                stat.Goals,
                stat.Assists,
                stat.Shots,
                stat.ShotsOnTarget,
                stat.Saves,
                stat.YellowCards,
                stat.RedCards,
                stat.RatedAppearances == 0
                    ? null
                    : (int?)(stat.RatingBasisPointsTotal / stat.RatedAppearances)))
            .ToListAsync(cancellationToken);

        return new DivisionStatisticsSnapshot(
            divisionId,
            header.DisplayName,
            header.TierNumber,
            header.CountryId,
            header.Code,
            header.CountryName,
            header.SeasonNumber,
            header.SeasonLabel,
            rows);
    }

    /// <inheritdoc />
    public async Task<DivisionRulesSnapshot?> GetDivisionRulesAsync(
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
                DivisionSeasonId = divisionSeason.Id,
                divisionSeason.TieDrawSeed,
                divisionSeason.TieDrawHash,
                division.DisplayName,
                division.TierNumber,
                CountryId = country.Id,
                country.Code,
                CountryName = country.DisplayName,
                SeasonNumber = seasonRow.SequenceNumber,
                SeasonLabel = seasonRow.DisplayLabel,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        // The clubs are ordered by name so the draw list is stable between reads (TBL-12's principle), and
        // each key is derived from the stored seed rather than stored, so a key cannot disagree with the
        // draw the season committed to before it was played (TBL-11).
        var clubs = await (
            from entry in _dbContext.ClubSeasonEntries
            join club in _dbContext.Clubs on entry.ClubId equals club.Id
            where entry.DivisionSeasonId == header.DivisionSeasonId
            orderby club.Name
            select new { club.Id, club.Name, club.ShortName })
            .ToListAsync(cancellationToken);

        var rows = clubs
            .Select(club => new DivisionRulesClubRow(
                club.Id,
                club.Name,
                club.ShortName,
                StandingsCalculator.DrawKeyOf(header.TieDrawSeed, club.Id)))
            .ToList();

        return new DivisionRulesSnapshot(
            divisionId,
            header.DisplayName,
            header.TierNumber,
            header.CountryId,
            header.Code,
            header.CountryName,
            header.SeasonNumber,
            header.SeasonLabel,
            header.TieDrawSeed,
            header.TieDrawHash,
            rows);
    }

    /// <inheritdoc />
    public async Task<DivisionDisciplineSnapshot?> GetDivisionDisciplineAsync(
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
                DivisionSeasonId = divisionSeason.Id,
                division.DisplayName,
                division.TierNumber,
                CountryId = country.Id,
                country.Code,
                CountryName = country.DisplayName,
                SeasonNumber = seasonRow.SequenceNumber,
                SeasonLabel = seasonRow.DisplayLabel,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        // Every player the season's cards have touched, with the club they now play for. The record is keyed
        // on the division-season and the player, so the club is resolved through the player's active
        // contract — the same resolution the squad read makes (SQ-6).
        var recorded = await (
            from record in _dbContext.DisciplineRecords
            join player in _dbContext.Players on record.PlayerId equals player.Id
            join contract in _dbContext.PlayerContracts on record.PlayerId equals contract.PlayerId
            join club in _dbContext.Clubs on contract.ClubId equals club.Id
            where record.DivisionSeasonId == header.DivisionSeasonId
                && contract.Status == ContractStatus.Active
            select new
            {
                PlayerId = player.Id,
                PlayerName = player.FullName,
                ClubId = club.Id,
                ClubName = club.Name,
                club.ShortName,
                record.YellowCards,
                record.RedCards,
            })
            .ToListAsync(cancellationToken);

        // What a player still owes is the open suspension the publication serves (DIS-5). Several open bans
        // of one player run concurrently rather than in sequence, so the honest number of fixtures left is
        // the greatest of them, not their sum.
        var playerIds = recorded.Select(candidate => candidate.PlayerId).ToList();

        var suspensions = await _dbContext.PlayerUnavailabilities
            .Where(record => playerIds.Contains(record.PlayerId)
                && record.Type == UnavailabilityType.Suspension
                && record.ResolvedAt == null)
            .GroupBy(record => record.PlayerId)
            .Select(group => new { PlayerId = group.Key, Remaining = group.Max(record => record.RemainingFixtures) })
            .ToListAsync(cancellationToken);

        var remaining = suspensions.ToDictionary(
            suspension => suspension.PlayerId,
            suspension => suspension.Remaining);

        // The leaderboard order is the server's — most sendings-off, then most bookings, then name — so the
        // screen sorts nothing and two reads never disagree (TBL-12's principle). It is applied in memory
        // because the suspension count is joined here, and a division holds at most a few hundred players.
        var rows = recorded
            .Select(row => new DivisionDisciplineRow(
                row.PlayerId,
                row.PlayerName,
                row.ClubId,
                row.ClubName,
                row.ShortName,
                row.YellowCards,
                row.RedCards,
                remaining.GetValueOrDefault(row.PlayerId)))
            .OrderByDescending(row => row.RedCards)
            .ThenByDescending(row => row.YellowCards)
            .ThenBy(row => row.PlayerName, StringComparer.Ordinal)
            .ToList();

        return new DivisionDisciplineSnapshot(
            divisionId,
            header.DisplayName,
            header.TierNumber,
            header.CountryId,
            header.Code,
            header.CountryName,
            header.SeasonNumber,
            header.SeasonLabel,
            rows);
    }

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
                    row.Fixture.MatchId,
                    row.Fixture.IsBootstrap);
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
            row.Fixture.MatchId,
            row.Fixture.IsBootstrap);
    }

    /// <inheritdoc />
    public async Task<ClubSeasonHistorySnapshot?> GetClubSeasonHistoryAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var club = await _dbContext.Clubs
            .Where(candidate => candidate.Id == clubId)
            .Select(candidate => new { candidate.Id, candidate.WorldId, candidate.Name, candidate.ShortName })
            .FirstOrDefaultAsync(cancellationToken);

        if (club is null)
        {
            return null;
        }

        // Only closed entries: an entry acquires its final rank when the rollover closes it, so a season still
        // in progress has no line here (PR-4, PR-6). The figures are the ones the close wrote and never
        // rewrote, so the history is the same however long after the season it is read.
        var seasons = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join seasonRow in _dbContext.Seasons on divisionSeason.SeasonId equals seasonRow.Id
            where entry.ClubId == clubId && entry.FinalRank >= 1
            orderby seasonRow.SequenceNumber descending
            select new ClubSeasonHistoryRow(
                seasonRow.SequenceNumber,
                seasonRow.DisplayLabel,
                division.TierNumber,
                division.DisplayName,
                entry.FinalRank ?? 0,
                entry.IsPromoted,
                entry.IsRelegated,
                entry.ClosingCashMinor ?? 0,
                entry.ClosingReputation ?? 0))
            .ToListAsync(cancellationToken);

        var nextSeason = await ResolveNextSeasonAsync(club.Id, club.WorldId, seasons, cancellationToken);

        return new ClubSeasonHistorySnapshot(club.Id, club.Name, club.ShortName, seasons, nextSeason);
    }

    /// <summary>
    /// Reads the club's placement in the world's next season, when that season already exists.
    /// </summary>
    /// <remarks>
    /// The next season is created by the rollover's move phase and the world pointer advances at complete, so
    /// it is non-null only between the two — exactly the window in which a manager wants to know where their
    /// club is going. The movement is read from the closing season's entry, which finalize closed with its
    /// promotion/relegation flags (`PR-4`).
    /// </remarks>
    private async Task<NextSeasonSummary?> ResolveNextSeasonAsync(
        Guid clubId,
        Guid worldId,
        IReadOnlyList<ClubSeasonHistoryRow> seasons,
        CancellationToken cancellationToken)
    {
        var current = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (current is null)
        {
            return null;
        }

        var next = await _dbContext.Seasons
            .Where(season => season.WorldId == worldId && season.SequenceNumber == current.SequenceNumber + 1)
            .Select(season => new { season.Id, season.SequenceNumber, season.DisplayLabel, season.StartsAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (next is null)
        {
            return null;
        }

        var placement = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where entry.ClubId == clubId && divisionSeason.SeasonId == next.Id
            select new { division.TierNumber, division.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        // A season created but not yet populated (a rollover interrupted between move and its entries) has no
        // placement to show, so the club's next season is not yet known rather than half known.
        if (placement is null)
        {
            return null;
        }

        var closing = seasons.FirstOrDefault(row => row.SeasonNumber == current.SequenceNumber);

        var movement = closing switch
        {
            { Promoted: true } => SeasonMovements.Promoted,
            { Relegated: true } => SeasonMovements.Relegated,
            _ => SeasonMovements.None,
        };

        return new NextSeasonSummary(
            next.SequenceNumber,
            next.DisplayLabel,
            next.StartsAt,
            placement.DisplayName,
            placement.TierNumber,
            movement);
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
            fixture.MatchId,
            fixture.IsBootstrap);
}
