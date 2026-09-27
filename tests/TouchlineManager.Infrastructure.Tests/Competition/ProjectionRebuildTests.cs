using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// The projection rebuild against real PostgreSQL: a division's table and season statistics reconciled and
/// repaired from its published results (`TBL-13`, `STA-1`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// These are the properties no unit test can reach. That a rebuild reproduces the live projection exactly,
/// that it repairs a drifted row, and that it removes a line no result supports are all facts about what the
/// database holds after a workflow that reads a whole division — so they are asserted by publishing a real
/// round, corrupting a projection, and reading the rows back.
/// </para>
/// <para>
/// Every test plays in the German division-season, which no other test in the collection touches: the other
/// workflow tests use the first division-season and the seeded-table test reads the last, so a repair here
/// cannot disturb an assertion there. Each test claims its own untouched round, because the order the
/// collection's tests run in is not something a test may depend on.
/// </para>
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class ProjectionRebuildTests
{
    /// <summary>Initializes the tests.</summary>
    public ProjectionRebuildTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests play rounds in.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task A_published_division_reconciles_without_drift()
    {
        Guid divisionSeasonId;

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            divisionSeasonId = await GermanDivisionSeasonIdAsync(db);

            await PublishRoundAsync(scope, await ClaimRoundAsync(db, divisionSeasonId));
        }

        await using (var scope = Fixture.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
                .ExecuteAsync(divisionSeasonId, apply: false, CancellationToken.None);

            result.Outcome.Should().Be(ProjectionRebuildOutcome.Reconciled);
            result.HasDrift.Should().BeFalse();
            result.Applied.Should().BeFalse();
            result.StandingsChecked.Should().Be(WorldRuleSet.ClubsPerDivision);
            result.PlayerStatsChecked.Should().BeGreaterThan(0, "a published round produced player lines");
        }
    }

    [Fact]
    public async Task A_corrupted_table_row_is_reported_and_rebuilt()
    {
        Guid divisionSeasonId;
        Guid standingId;

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            divisionSeasonId = await GermanDivisionSeasonIdAsync(db);

            await PublishRoundAsync(scope, await ClaimRoundAsync(db, divisionSeasonId));

            standingId = await db.Standings
                .Where(standing => standing.DivisionSeasonId == divisionSeasonId)
                .OrderBy(standing => standing.Rank)
                .Select(standing => standing.Id)
                .FirstAsync();
        }

        await CorruptAsync("update competition.standings set goals_for = goals_for + 5 where id = {0}", standingId);

        await using (var scope = Fixture.CreateScope())
        {
            var rebuild = scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>();

            var dry = await rebuild.ExecuteAsync(divisionSeasonId, apply: false, CancellationToken.None);

            dry.Outcome.Should().Be(ProjectionRebuildOutcome.DriftDetected);
            dry.StandingsDrifted.Should().BeGreaterThan(0);
            dry.Applied.Should().BeFalse();
        }

        await using (var scope = Fixture.CreateScope())
        {
            var applied = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
                .ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);

            applied.Outcome.Should().Be(ProjectionRebuildOutcome.Rebuilt);
            applied.Applied.Should().BeTrue();
        }

        await AssertTableMatchesSourceAsync(divisionSeasonId);

        // A repair is idempotent: the second rebuild finds nothing left to correct.
        await using (var scope = Fixture.CreateScope())
        {
            var again = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
                .ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);

            again.Outcome.Should().Be(ProjectionRebuildOutcome.Reconciled);
            again.Applied.Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_corrupted_player_line_is_reported_and_rebuilt()
    {
        Guid divisionSeasonId;
        Guid statId;

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            divisionSeasonId = await GermanDivisionSeasonIdAsync(db);

            await PublishRoundAsync(scope, await ClaimRoundAsync(db, divisionSeasonId));

            statId = await db.PlayerSeasonStats
                .Where(stat => stat.DivisionSeasonId == divisionSeasonId)
                .OrderBy(stat => stat.Id)
                .Select(stat => stat.Id)
                .FirstAsync();
        }

        await CorruptAsync(
            "update competition.player_season_stats set goals = goals + 5 where id = {0}",
            statId);

        await using (var scope = Fixture.CreateScope())
        {
            var rebuild = scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>();

            var dry = await rebuild.ExecuteAsync(divisionSeasonId, apply: false, CancellationToken.None);

            dry.Outcome.Should().Be(ProjectionRebuildOutcome.DriftDetected);
            dry.PlayerStatsDrifted.Should().BeGreaterThan(0);
        }

        await using (var scope = Fixture.CreateScope())
        {
            var applied = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
                .ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);

            applied.Outcome.Should().Be(ProjectionRebuildOutcome.Rebuilt);
        }

        await AssertStatsMatchSourceAsync(divisionSeasonId);
    }

    [Fact]
    public async Task A_stored_line_no_published_result_supports_is_removed()
    {
        Guid divisionSeasonId;
        Guid statId;
        Guid orphanPlayerId;

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            divisionSeasonId = await GermanDivisionSeasonIdAsync(db);

            await PublishRoundAsync(scope, await ClaimRoundAsync(db, divisionSeasonId));

            statId = await db.PlayerSeasonStats
                .Where(stat => stat.DivisionSeasonId == divisionSeasonId)
                .OrderBy(stat => stat.Id)
                .Select(stat => stat.Id)
                .FirstAsync();

            var divisionPlayerIds = await db.PlayerSeasonStats
                .Where(stat => stat.DivisionSeasonId == divisionSeasonId)
                .Select(stat => stat.PlayerId)
                .ToListAsync();

            // A player who exists but played no part in this division-season, so the row names no result the
            // projection can be derived from.
            orphanPlayerId = await db.Players
                .Where(player => !divisionPlayerIds.Contains(player.Id))
                .OrderBy(player => player.Id)
                .Select(player => player.Id)
                .FirstAsync();
        }

        await CorruptAsync(
            "update competition.player_season_stats set player_id = {0} where id = {1}",
            orphanPlayerId,
            statId);

        await using (var scope = Fixture.CreateScope())
        {
            var applied = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
                .ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);

            applied.Outcome.Should().Be(ProjectionRebuildOutcome.Rebuilt);
            applied.PlayerStatsDrifted.Should().BeGreaterThan(0);
        }

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (await db.PlayerSeasonStats.AnyAsync(stat => stat.Id == statId))
                .Should().BeFalse("a line no published result supports is removed, not kept");
        }

        await AssertStatsMatchSourceAsync(divisionSeasonId);
    }

    [Fact]
    public async Task An_unknown_division_season_is_not_found()
    {
        await using var scope = Fixture.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
            .ExecuteAsync(Guid.CreateVersion7(), apply: true, CancellationToken.None);

        result.Outcome.Should().Be(ProjectionRebuildOutcome.DivisionSeasonNotFound);
        result.Applied.Should().BeFalse();
    }

    /// <summary>Finds the German division-season, which no other test in the collection plays in.</summary>
    private static async Task<Guid> GermanDivisionSeasonIdAsync(TouchlineManagerDbContext db) =>
        await (
            from divisionSeason in db.DivisionSeasons
            join division in db.Divisions on divisionSeason.DivisionId equals division.Id
            join country in db.Countries on division.CountryId equals country.Id
            where country.Code == LaunchCountries.GermanyCode
            select divisionSeason.Id)
            .SingleAsync();

    /// <summary>Claims the lowest round of the German division whose fixtures nobody has touched.</summary>
    private static async Task<Guid> ClaimRoundAsync(TouchlineManagerDbContext db, Guid divisionSeasonId)
    {
        var matchdayId = await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId
                && matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && !db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id
                    && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstOrDefaultAsync();

        matchdayId.Should().NotBeEmpty("the seeded world has thirty-four rounds and these tests use a handful");

        return matchdayId;
    }

    /// <summary>Locks, resolves, and publishes a round through the real workflow.</summary>
    private static async Task PublishRoundAsync(AsyncServiceScope scope, Guid matchdayId)
    {
        await scope.ServiceProvider.GetRequiredService<LockMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);

        published.Outcome.Should().Be(PublishMatchdayOutcome.Published);
    }

    /// <summary>Applies a raw update in its own scope, the way an out-of-band write would.</summary>
    private async Task CorruptAsync(string sql, params object[] parameters)
    {
        await using var scope = Fixture.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<TouchlineManagerDbContext>()
            .Database
            .ExecuteSqlRawAsync(sql, parameters);
    }

    private async Task AssertTableMatchesSourceAsync(Guid divisionSeasonId)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IMatchdayRepository>();

        var source = await repository.LoadTableSourceAsync(divisionSeasonId, CancellationToken.None);

        source.Should().NotBeNull();

        var expected = StandingsCalculator
            .Rank(source!.ClubIds, source.Outcomes, clubId => StandingsCalculator.DrawKeyOf(source.TieDrawSeed, clubId))
            .OrderBy(line => line.ClubId);

        var stored = await db.Standings
            .Where(standing => standing.DivisionSeasonId == divisionSeasonId)
            .ToListAsync();

        stored
            .Select(standing => (
                standing.ClubId,
                standing.Played,
                standing.Won,
                standing.Drawn,
                standing.Lost,
                standing.GoalsFor,
                standing.GoalsAgainst,
                standing.Points,
                standing.YellowCards,
                standing.RedCards,
                standing.Rank))
            .Should()
            .BeEquivalentTo(expected.Select(line => (
                line.ClubId,
                line.Played,
                line.Won,
                line.Drawn,
                line.Lost,
                line.GoalsFor,
                line.GoalsAgainst,
                line.Points,
                line.YellowCards,
                line.RedCards,
                line.Rank)));
    }

    private async Task AssertStatsMatchSourceAsync(Guid divisionSeasonId)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IMatchdayRepository>();

        var expected = SeasonStatisticsCalculator.Aggregate(
            SeasonStatisticsCalculator.Calculate(
                await repository.LoadDivisionMatchLoadsAsync(divisionSeasonId, CancellationToken.None),
                await repository.LoadDivisionStatEventsAsync(divisionSeasonId, CancellationToken.None)));

        var stored = await db.PlayerSeasonStats
            .Where(stat => stat.DivisionSeasonId == divisionSeasonId)
            .ToListAsync();

        stored
            .Select(stat => (
                stat.PlayerId,
                stat.ClubId,
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
                stat.RatingBasisPointsTotal,
                stat.RatedAppearances))
            .Should()
            .BeEquivalentTo(expected.Select(line => (
                line.PlayerId,
                line.ClubId,
                line.Appearances,
                line.Starts,
                line.MinutesPlayed,
                line.Goals,
                line.Assists,
                line.Shots,
                line.ShotsOnTarget,
                line.Saves,
                line.YellowCards,
                line.RedCards,
                line.RatingBasisPointsTotal,
                line.RatedAppearances)));
    }
}
