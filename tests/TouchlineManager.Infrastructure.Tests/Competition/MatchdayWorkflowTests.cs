using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// The matchday workflow against real PostgreSQL: lock, resolve, publish, and the table they move
/// (`MAT-1`, `MAT-7`, `MAT-9`, `TBL-13`, master plan §7.3, §7.4).
/// </summary>
/// <remarks>
/// <para>
/// These are the properties no unit test can reach. A snapshot's immutability, a result's idempotency, and a
/// publication that either moves the table or does nothing are all facts about what the database holds after
/// a workflow that may be run twice — so they are asserted by running it twice and reading the rows back,
/// against a real seeded world.
/// </para>
/// <para>
/// Every test claims its own untouched round of the same division rather than sharing one, because this
/// fixture's world is shared with the other tests in its collection and their order is not something a test
/// may depend on. Reads that are compared before and after an operation are taken through separate scopes, so
/// the comparison is between two database reads rather than between the same tracked instance twice.
/// </para>
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class MatchdayWorkflowTests
{
    /// <summary>Initializes the tests.</summary>
    public MatchdayWorkflowTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests play rounds in.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task A_seeded_division_opens_with_a_table_of_eighteen_clubs()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        // The last division in the world, which no other test in this class plays a round in.
        var divisionSeason = await db.DivisionSeasons
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstAsync();

        var standings = await db.Standings
            .Where(standing => standing.DivisionSeasonId == divisionSeason.Id)
            .OrderBy(standing => standing.Rank)
            .ToListAsync();

        standings.Should().HaveCount(WorldRuleSet.ClubsPerDivision, "a table exists before the first ball is kicked");
        standings.Select(standing => standing.Rank).Should().Equal(Enumerable.Range(1, WorldRuleSet.ClubsPerDivision));
        standings.Should().OnlyContain(standing => standing.Played == 0 && standing.Points == 0);
    }

    [Fact]
    public async Task Locking_a_round_freezes_every_fixture_once()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(scope);
        var lockMatchday = scope.ServiceProvider.GetRequiredService<LockMatchday>();

        var first = await lockMatchday.ExecuteAsync(matchdayId, CancellationToken.None);

        first.Outcome.Should().Be(LockMatchdayOutcome.Locked);
        first.FrozenFixtures.Should().Be(9, "a round is nine fixtures (CAL-10)");

        var fixtures = await FixturesOfAsync(db, matchdayId);

        fixtures.Should().OnlyContain(fixture => fixture.Status == FixtureStatus.Locked);

        var fixtureIds = fixtures.Select(fixture => fixture.Id).ToList();

        // Neither club prepared a side, so every slot of both was decided by the builder and recorded in the
        // frozen document: eleven starters and seven substitutes a side (DIS-7).
        var repairsPerFixture = new List<int>();

        foreach (var fixtureId in fixtureIds)
        {
            var stored = await db.InputSnapshots.SingleAsync(snapshot => snapshot.FixtureId == fixtureId);

            repairsPerFixture.Add(MatchSnapshotDocument.Read(stored.SnapshotJson).Repairs.Count);
        }

        repairsPerFixture.Should().Equal(Enumerable.Repeat(36, 9));
        var snapshots = await db.InputSnapshots
            .Where(snapshot => fixtureIds.Contains(snapshot.FixtureId))
            .ToListAsync();

        snapshots.Should().HaveCount(9, "one snapshot per fixture, and no more (MAT-1)");
        snapshots.Select(snapshot => snapshot.FixtureId).Should().OnlyHaveUniqueItems();
        snapshots.Should().OnlyContain(snapshot => snapshot.EngineVersion == EngineVersions.EngineLabel);
        snapshots.Should().OnlyContain(snapshot => snapshot.SnapshotHash.Length == 64);
        (await db.InputSnapshots.CountAsync()).Should().BeGreaterThanOrEqualTo(9);

        // The same job may run again — after a lease expiry, a redeploy, or an operator retry — and must find
        // the round already frozen rather than freeze it a second time (§7.1, ADR-0003).
        var second = await lockMatchday.ExecuteAsync(matchdayId, CancellationToken.None);

        second.Outcome.Should().Be(LockMatchdayOutcome.AlreadyLocked);
        second.FrozenFixtures.Should().Be(0);

        (await db.InputSnapshots.CountAsync(snapshot => fixtureIds.Contains(snapshot.FixtureId))).Should().Be(9);
    }

    [Fact]
    public async Task Resolving_a_round_simulates_every_fixture_and_stages_the_results()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(scope);
        var lockMatchday = scope.ServiceProvider.GetRequiredService<LockMatchday>();
        var resolveMatchday = scope.ServiceProvider.GetRequiredService<ResolveMatchday>();

        await lockMatchday.ExecuteAsync(matchdayId, CancellationToken.None);

        var resolved = await resolveMatchday.ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        resolved.Outcome.Should().Be(ResolveMatchdayOutcome.Resolved);
        resolved.Simulated.Should().Be(9);

        var fixtures = await FixturesOfAsync(db, matchdayId);

        fixtures.Should().OnlyContain(fixture => fixture.Status == FixtureStatus.Staged);
        fixtures.Should().OnlyContain(fixture => fixture.HomeScore != null && fixture.AwayScore != null);

        var matchIds = fixtures.Select(fixture => fixture.MatchId!.Value).ToList();
        var fixtureIds = fixtures.Select(fixture => fixture.Id).ToList();

        (await db.Matches.CountAsync(match => matchIds.Contains(match.Id))).Should().Be(9, "one match per fixture (MAT-9)");

        (await db.MatchEvents.CountAsync(matchEvent => matchIds.Contains(matchEvent.MatchId)))
            .Should()
            .BeGreaterThan(9 * 4, "a simulated match emits a stream of events (MAT-8)");

        (await db.SimulationAttempts.CountAsync(
            attempt => attempt.Status == SimulationAttemptStatus.Succeeded && fixtureIds.Contains(attempt.FixtureId)))
            .Should()
            .Be(9, "every attempt is kept as the evidence that the result is reproducible (MAT-9)");

        (await db.Matchdays.SingleAsync(candidate => candidate.Id == matchdayId)).PublicationStatus
            .Should()
            .Be(MatchdayPublicationStatus.Staged, "the round stages itself once every fixture has (MAT-7)");

        // A retried resolution finds every fixture staged and leaves the round alone: no second match, no
        // second event stream, and the same staged scores.
        var again = await resolveMatchday.ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        again.Outcome.Should().Be(ResolveMatchdayOutcome.AlreadyResolved);
        again.Simulated.Should().Be(0);

        (await db.Matches.CountAsync(match => matchIds.Contains(match.Id))).Should().Be(9, "a completed attempt is not run again");
    }

    [Fact]
    public async Task A_round_interrupted_mid_simulation_resumes_without_losing_or_duplicating_a_result()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(scope);

        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);

        // The state a worker killed between marking a fixture and staging it leaves behind: the fixture says
        // it is being simulated, and no result exists for it (MAT-7, §7.4).
        var interrupted = (await FixturesOfAsync(db, matchdayId))[0];

        interrupted.BeginSimulation(Fixture.Clock.UtcNow);
        await db.SaveChangesAsync(CancellationToken.None);

        var resolved = await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        resolved.Outcome.Should().Be(ResolveMatchdayOutcome.Resolved);
        resolved.Simulated.Should().Be(9, "the interrupted fixture is simulated again, not skipped");

        var fixtures = await FixturesOfAsync(db, matchdayId);

        fixtures.Should().OnlyContain(fixture => fixture.Status == FixtureStatus.Staged, "no fixture is left behind");
        fixtures.Select(fixture => fixture.MatchId).Should().OnlyHaveUniqueItems("no fixture is simulated twice");

        // The resumed result is the one the frozen input produces, so a crash costs time rather than
        // changing a scoreline (MAT-9).
        var snapshot = await db.InputSnapshots.SingleAsync(candidate => candidate.FixtureId == interrupted.Id);
        var stored = await db.Matches.SingleAsync(match => match.FixtureId == interrupted.Id);

        MatchSimulator
            .Simulate(MatchSnapshotFactory.ReadVerified(snapshot), EngineRulesV1.Default)
            .OutputHash.Should()
            .Be(stored.OutputHash);

        var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);

        published.Outcome.Should().Be(PublishMatchdayOutcome.Published);
        published.Published.Should().Be(9);
    }

    [Fact]
    public async Task A_stored_snapshot_re_simulates_to_the_same_output_hash()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(scope);

        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        var fixture = (await FixturesOfAsync(db, matchdayId))[0];
        var snapshot = await db.InputSnapshots.SingleAsync(candidate => candidate.FixtureId == fixture.Id);
        var stored = await db.Matches.SingleAsync(match => match.FixtureId == fixture.Id);

        // The snapshot round-trips to the hash it was stored with, which is the check the workflow itself
        // makes before it simulates anything.
        var input = MatchSnapshotFactory.ReadVerified(snapshot);
        var reSimulated = MatchSimulator.Simulate(input, EngineRulesV1.Default);

        reSimulated.OutputHash
            .Should()
            .Be(stored.OutputHash, "MAT-9: the same snapshot, seed, and engine reproduce the result");

        reSimulated.HomeGoals.Should().Be(stored.HomeGoals);
        reSimulated.AwayGoals.Should().Be(stored.AwayGoals);
        reSimulated.InputHash.Should().Be(snapshot.SnapshotHash);
    }

    [Fact]
    public async Task Publishing_a_round_that_is_not_staged_publishes_nothing()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(scope);
        var divisionSeasonId = await DivisionSeasonOfAsync(db, matchdayId);

        List<Standing> before;

        await using (var beforeScope = Fixture.CreateScope())
        {
            before = await TableOfAsync(beforeScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>(), divisionSeasonId);
        }

        var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);

        published.Outcome.Should().Be(PublishMatchdayOutcome.NotFullyStaged);
        published.Published.Should().Be(0);

        await using var afterScope = Fixture.CreateScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var after = await TableOfAsync(afterDb, divisionSeasonId);

        after.Should().BeEquivalentTo(before, "a partial round must not move the table (MAT-7)");
        (await FixturesOfAsync(afterDb, matchdayId))
            .Should()
            .OnlyContain(fixture => fixture.Status == FixtureStatus.Scheduled);
    }

    [Fact]
    public async Task Publishing_a_staged_round_makes_it_public_and_moves_the_table()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(scope);
        var divisionSeasonId = await DivisionSeasonOfAsync(db, matchdayId);

        var before = await TableOfAsync(db, divisionSeasonId);
        var playedBefore = before.Sum(standing => standing.Played);

        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        var publish = scope.ServiceProvider.GetRequiredService<PublishMatchday>();
        var result = await publish.ExecuteAsync(matchdayId, CancellationToken.None);

        result.Outcome.Should().Be(PublishMatchdayOutcome.Published);
        result.Published.Should().Be(9);
        result.TableRows.Should().Be(WorldRuleSet.ClubsPerDivision);

        var fixtures = await FixturesOfAsync(db, matchdayId);

        fixtures.Should().OnlyContain(fixture => fixture.Status == FixtureStatus.Published);
        fixtures.Should().OnlyContain(fixture => fixture.PublishedAt != null);

        (await db.Matchdays.SingleAsync(candidate => candidate.Id == matchdayId)).PublicationStatus
            .Should()
            .Be(MatchdayPublicationStatus.Published);

        await using var readScope = Fixture.CreateScope();
        var after = await TableOfAsync(
            readScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>(),
            divisionSeasonId);

        after.Sum(standing => standing.Played).Should().Be(playedBefore + 18, "every club played exactly once");
        after.Select(standing => standing.Rank).Should().Equal(Enumerable.Range(1, WorldRuleSet.ClubsPerDivision));
        after.Should().OnlyContain(standing => standing.Points == (standing.Won * 3) + standing.Drawn);
        after.Should().OnlyContain(standing => standing.Played == standing.Won + standing.Drawn + standing.Lost);

        // The stored table is the ranking computed from the division's published results, so the projection is
        // rebuildable rather than merely plausible (TBL-13).
        var repository = scope.ServiceProvider.GetRequiredService<IMatchdayRepository>();
        var source = await repository.LoadTableSourceAsync(divisionSeasonId, CancellationToken.None);

        var expected = StandingsCalculator.Rank(
            source!.ClubIds,
            source.Outcomes,
            clubId => StandingsCalculator.DrawKeyOf(source.TieDrawSeed, clubId));

        after.Select(standing => (standing.ClubId, standing.Played, standing.Points, standing.GoalsFor, standing.Rank))
            .Should()
            .Equal(expected.Select(line => (line.ClubId, line.Played, line.Points, line.GoalsFor, line.Rank)));

        // Publishing again is a no-op rather than an error, so a retried publication job is harmless.
        var again = await publish.ExecuteAsync(matchdayId, CancellationToken.None);

        again.Outcome.Should().Be(PublishMatchdayOutcome.AlreadyPublished);

        await using var secondRead = Fixture.CreateScope();

        (await TableOfAsync(
            secondRead.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>(),
            divisionSeasonId))
            .Should()
            .BeEquivalentTo(after);
    }

    /// <summary>
    /// Claims the lowest round of the first division whose fixtures nobody has touched, so a test never
    /// depends on the order the collection's tests ran in.
    /// </summary>
    private static async Task<Guid> ClaimRoundAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var divisionSeason = await db.DivisionSeasons.OrderBy(divisionSeasonRow => divisionSeasonRow.CreatedAt).FirstAsync();

        var matchdayId = await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeason.Id
                && matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && !db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id
                    && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstOrDefaultAsync();

        matchdayId.Should().NotBeEmpty("the seeded world has thirty-four rounds and these tests use a handful");

        return matchdayId;
    }

    private static async Task<Guid> DivisionSeasonOfAsync(TouchlineManagerDbContext db, Guid matchdayId) =>
        await db.Matchdays
            .Where(matchday => matchday.Id == matchdayId)
            .Select(matchday => matchday.DivisionSeasonId)
            .SingleAsync();

    private static async Task<List<Fixture>> FixturesOfAsync(TouchlineManagerDbContext db, Guid matchdayId) =>
        await db.Fixtures
            .Where(fixture => fixture.MatchdayId == matchdayId)
            .OrderBy(fixture => fixture.Id)
            .ToListAsync();

    private static async Task<List<Standing>> TableOfAsync(TouchlineManagerDbContext db, Guid divisionSeasonId) =>
        await db.Standings
            .Where(standing => standing.DivisionSeasonId == divisionSeasonId)
            .OrderBy(standing => standing.Rank)
            .ToListAsync();
}
