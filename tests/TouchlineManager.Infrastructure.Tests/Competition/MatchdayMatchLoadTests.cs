using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// The load a published result places on the players who appeared: condition consumed, fatigue accumulated,
/// and morale moved by the result and their minutes (`TRN-11`, `TRN-13`).
/// </summary>
/// <remarks>
/// The deltas are asserted as <em>movement</em> rather than against the calculator's exact numbers. The
/// shared world is played in by the other tests in this collection, so a player's condition may already have
/// been touched by a round somebody else published; what this suite proves is the wiring — the real
/// publication, over a real seeded world, moves the state of the players who played and only those.
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class MatchdayMatchLoadTests
{
    /// <summary>Initializes the tests.</summary>
    public MatchdayMatchLoadTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests play rounds in.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task Publishing_a_round_loads_the_players_who_appeared_and_only_them()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var round = await ClaimRoundAsync(scope);

        await LockAndResolveAsync(scope, round);

        var fixture = await FirstStagedFixtureAsync(db, round);
        var content = await LoadLinesAsync(db, fixture.Id);

        var appeared = content.Where(line => line.MinutesPlayed > 0).ToList();
        var unused = content.Where(line => line.MinutesPlayed == 0).ToList();

        appeared.Should().NotBeEmpty("a match leaves most of a squad on the pitch");
        unused.Should().NotBeEmpty("a bench of seven cannot all be used by five substitutions");

        var playerIds = content.Select(line => line.ParticipantId).ToList();
        var before = await SnapshotStatesAsync(db, playerIds);

        var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(round, CancellationToken.None);

        published.Outcome.Should().Be(PublishMatchdayOutcome.Published);

        await using var readScope = Fixture.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var after = await SnapshotStatesAsync(readDb, playerIds);

        var scores = await readDb.Fixtures
            .Where(candidate => candidate.Id == fixture.Id)
            .Select(candidate => new { candidate.HomeScore, candidate.AwayScore })
            .SingleAsync();

        foreach (var line in appeared)
        {
            var was = before[line.ParticipantId];
            var now = after[line.ParticipantId];

            now.Condition.Should().BeLessThan(
                was.Condition,
                "a match consumes condition (TRN-11)");
            now.Fatigue.Should().BeGreaterThan(
                was.Fatigue,
                "a match adds fatigue (TRN-11)");
            now.Sharpness.Should().Be(
                was.Sharpness,
                "a match does not move match sharpness, which is training's measure");

            AssertMoraleMovesWithTheResult(line, scores.HomeScore, scores.AwayScore, was.Morale, now.Morale);
        }

        foreach (var line in unused)
        {
            after[line.ParticipantId].Should().Be(
                before[line.ParticipantId],
                "a player who never left the bench carries no load (TRN-13)");
        }
    }

    [Fact]
    public async Task Publishing_the_same_round_twice_applies_the_load_once()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var round = await ClaimRoundAsync(scope);

        await LockAndResolveAsync(scope, round);

        var fixture = await FirstStagedFixtureAsync(db, round);
        var playerIds = (await LoadLinesAsync(db, fixture.Id))
            .Select(line => line.ParticipantId)
            .ToList();

        var publish = scope.ServiceProvider.GetRequiredService<PublishMatchday>();

        await publish.ExecuteAsync(round, CancellationToken.None);

        await using var betweenScope = Fixture.CreateScope();
        var afterFirst = await SnapshotStatesAsync(
            betweenScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>(),
            playerIds);

        var second = await publish.ExecuteAsync(round, CancellationToken.None);

        second.Outcome.Should().Be(PublishMatchdayOutcome.AlreadyPublished);

        await using var finalScope = Fixture.CreateScope();
        var afterSecond = await SnapshotStatesAsync(
            finalScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>(),
            playerIds);

        afterSecond.Should().BeEquivalentTo(afterFirst, "a republished round must not load a player twice");
    }

    /// <summary>Asserts a player's morale moved the way their side's result implies, allowing for clamping.</summary>
    private static void AssertMoraleMovesWithTheResult(
        MatchPlayerLineV1 line,
        int? homeScore,
        int? awayScore,
        int before,
        int after)
    {
        var home = line.Side == MatchSide.Home;
        var won = home ? homeScore > awayScore : awayScore > homeScore;
        var lost = home ? homeScore < awayScore : awayScore < homeScore;

        if (won)
        {
            after.Should().BeGreaterThanOrEqualTo(before, "a win never lowers morale (TRN-13)");
        }
        else if (lost)
        {
            after.Should().BeLessThanOrEqualTo(before, "a defeat never raises it");
        }
    }

    /// <summary>Reads the player lines a stored result carries.</summary>
    private static async Task<IReadOnlyList<MatchPlayerLineV1>> LoadLinesAsync(
        TouchlineManagerDbContext db,
        Guid fixtureId)
    {
        var statistics = await db.Matches
            .Where(match => match.FixtureId == fixtureId)
            .Select(match => match.StatisticsJson)
            .SingleAsync();

        return MatchStatisticsDocument.Read(statistics).PlayerLines;
    }

    /// <summary>Reads the primitive state values, so a later mutation of the tracked row cannot rewrite them.</summary>
    private static async Task<Dictionary<Guid, (int Condition, int Fatigue, int Morale, int Sharpness)>> SnapshotStatesAsync(
        TouchlineManagerDbContext db,
        IReadOnlyCollection<Guid> playerIds) =>
        await db.PlayerStates
            .Where(state => playerIds.Contains(state.PlayerId))
            .ToDictionaryAsync(
                state => state.PlayerId,
                state => (state.ConditionBp, state.FatigueBp, state.MoraleBp, state.MatchSharpnessBp));

    /// <summary>Locks and resolves a round, leaving its fixtures staged and its snapshots frozen.</summary>
    private static async Task LockAndResolveAsync(AsyncServiceScope scope, Guid matchdayId)
    {
        await scope.ServiceProvider.GetRequiredService<LockMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);
    }

    /// <summary>Claims one untouched round of a division.</summary>
    private static async Task<Guid> ClaimRoundAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var divisionSeasonId = await db.DivisionSeasons
            .OrderBy(divisionSeason => divisionSeason.CreatedAt)
            .Select(divisionSeason => divisionSeason.Id)
            .FirstAsync();

        return await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId
                && matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && !db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id
                    && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstAsync();
    }

    private static async Task<Fixture> FirstStagedFixtureAsync(TouchlineManagerDbContext db, Guid matchdayId) =>
        await db.Fixtures
            .Where(fixture => fixture.MatchdayId == matchdayId && fixture.Status == FixtureStatus.Staged)
            .OrderBy(fixture => fixture.Id)
            .FirstAsync();
}
