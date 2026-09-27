using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// The season statistics a published round advances: one line per player who appeared, grown by the match
/// (`STA-*`, master plan §6.4).
/// </summary>
/// <remarks>
/// The shared world is played in by the other tests in this collection, so the totals may already include
/// rounds somebody else published; what this suite proves is the wiring — the real publication, over a real
/// seeded world, writes a line for the players who appeared and counts a round exactly once.
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class MatchdaySeasonStatisticsTests
{
    /// <summary>Initializes the tests.</summary>
    public MatchdaySeasonStatisticsTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests play rounds in.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task Publishing_a_round_writes_a_line_for_every_player_who_appeared()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var round = await ClaimRoundAsync(scope);

        await LockAndResolveAsync(scope, round);

        var fixture = await FirstStagedFixtureAsync(db, round);
        var divisionSeasonId = await DivisionSeasonOfAsync(db, round);
        var lines = await LoadLinesAsync(db, fixture.Id);

        var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(round, CancellationToken.None);

        published.Outcome.Should().Be(PublishMatchdayOutcome.Published);

        await using var readScope = Fixture.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var stats = await readDb.PlayerSeasonStats
            .Where(stat => stat.DivisionSeasonId == divisionSeasonId)
            .ToDictionaryAsync(stat => (stat.PlayerId, stat.ClubId));

        foreach (var line in lines.Where(line => line.MinutesPlayed > 0))
        {
            stats.Should().ContainKey(
                (line.ParticipantId, line.ClubId),
                "a player who took the pitch has a season line");

            var stat = stats[(line.ParticipantId, line.ClubId)];

            stat.Appearances.Should().BeGreaterThanOrEqualTo(1);
            stat.Goals.Should().BeGreaterThanOrEqualTo(line.Goals);
        }
    }

    [Fact]
    public async Task Publishing_the_same_round_twice_does_not_count_a_player_twice()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var round = await ClaimRoundAsync(scope);

        await LockAndResolveAsync(scope, round);

        var fixture = await FirstStagedFixtureAsync(db, round);
        var divisionSeasonId = await DivisionSeasonOfAsync(db, round);
        var playerIds = (await LoadLinesAsync(db, fixture.Id))
            .Select(line => line.ParticipantId)
            .ToList();

        var publish = scope.ServiceProvider.GetRequiredService<PublishMatchday>();

        await publish.ExecuteAsync(round, CancellationToken.None);

        await using var betweenScope = Fixture.CreateScope();
        var afterFirst = await SnapshotAsync(
            betweenScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>(),
            divisionSeasonId,
            playerIds);

        var second = await publish.ExecuteAsync(round, CancellationToken.None);

        second.Outcome.Should().Be(PublishMatchdayOutcome.AlreadyPublished);

        await using var finalScope = Fixture.CreateScope();
        var afterSecond = await SnapshotAsync(
            finalScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>(),
            divisionSeasonId,
            playerIds);

        afterSecond.Should().BeEquivalentTo(
            afterFirst,
            "a republished round must not count a player's statistics twice");
    }

    /// <summary>Reads the primitive statistics values, so a later mutation of a tracked row cannot rewrite them.</summary>
    private static async Task<Dictionary<Guid, (int Appearances, int Goals, int Assists, long RatingTotal)>> SnapshotAsync(
        TouchlineManagerDbContext db,
        Guid divisionSeasonId,
        IReadOnlyCollection<Guid> playerIds) =>
        await db.PlayerSeasonStats
            .Where(stat => stat.DivisionSeasonId == divisionSeasonId && playerIds.Contains(stat.PlayerId))
            .ToDictionaryAsync(
                stat => stat.PlayerId,
                stat => (stat.Appearances, stat.Goals, stat.Assists, stat.RatingBasisPointsTotal));

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

    private static async Task<Guid> DivisionSeasonOfAsync(TouchlineManagerDbContext db, Guid matchdayId) =>
        await db.Matchdays
            .Where(matchday => matchday.Id == matchdayId)
            .Select(matchday => matchday.DivisionSeasonId)
            .SingleAsync();

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
