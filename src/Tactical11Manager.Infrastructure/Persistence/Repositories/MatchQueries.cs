using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Match;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The match module's read projection: one played match, for the summary and the replay (master plan §9.5).
/// </summary>
/// <remarks>
/// <para>
/// One query, shaped for the match center screen, and none loading a tracked graph. The read is measured
/// against the match's own season rather than the world's current one, so a result stays readable across a
/// rollover — a played match is history, and the round, the division, and the season it belongs to are the
/// ones it was played in (`MAT-9`).
/// </para>
/// <para>
/// The fixture's publication state is part of the query rather than a check a caller may forget: only a
/// <see cref="FixtureStatus.Published"/> fixture produces a match, so a staged result cannot leak from the
/// viewer (`MAT-7`).
/// </para>
/// </remarks>
internal sealed class MatchQueries : IMatchQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public MatchQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<MatchReadSnapshot?> GetMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var row = await (
            from match in _dbContext.Matches
            join fixture in _dbContext.Fixtures on match.FixtureId equals fixture.Id
            join matchday in _dbContext.Matchdays on fixture.MatchdayId equals matchday.Id
            join divisionSeason in _dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join country in _dbContext.Countries on division.CountryId equals country.Id
            join seasonRow in _dbContext.Seasons on divisionSeason.SeasonId equals seasonRow.Id
            join home in _dbContext.Clubs on fixture.HomeClubId equals home.Id
            join away in _dbContext.Clubs on fixture.AwayClubId equals away.Id
            where match.Id == matchId && fixture.Status == FixtureStatus.Published
            select new
            {
                Match = match,
                Fixture = fixture,
                Matchday = matchday,
                Division = division,
                Country = country,
                Season = seasonRow,
                Home = home,
                Away = away,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // A published fixture always has a snapshot — the lock froze it before kickoff — so a missing one is
        // a data-integrity failure, and answering "not found" would hide it rather than surface it.
        var snapshot = await _dbContext.InputSnapshots
            .FirstOrDefaultAsync(candidate => candidate.FixtureId == row.Fixture.Id, cancellationToken);

        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"The published fixture {row.Fixture.Id:D} has no frozen input snapshot.");
        }

        return new MatchReadSnapshot(
            row.Match.Id,
            row.Fixture.Id,
            row.Division.Id,
            row.Division.DisplayName,
            row.Division.TierNumber,
            row.Country.Id,
            row.Country.Code,
            row.Country.DisplayName,
            row.Season.SequenceNumber,
            row.Season.DisplayLabel,
            row.Matchday.RoundNumber,
            row.Fixture.KickoffAt,
            row.Match.EngineVersion,
            row.Match.RuleSetVersion,
            row.Match.HomeGoals,
            row.Match.AwayGoals,
            row.Match.StatisticsJson,
            row.Match.OutputHash,
            ClubRowOf(row.Home),
            ClubRowOf(row.Away),
            snapshot,
            await PlayoffReads.ForFixtureAsync(_dbContext, row.Fixture.Id, cancellationToken));
    }

    /// <summary>
    /// Reads a club's identity as the match sees it. The crest is the club's own today, not the one frozen with
    /// the match, so a manager who redesigns it sees the new one on the old matches too.
    /// </summary>
    private static MatchClubRow ClubRowOf(Club club) =>
        new(
            club.Id,
            club.Name,
            club.ShortName,
            CrestDesigns.ResolveShape(club.BadgeSeed, club.CrestShape),
            CrestDesigns.ResolvePattern(club.BadgeSeed, club.CrestPattern));
}
