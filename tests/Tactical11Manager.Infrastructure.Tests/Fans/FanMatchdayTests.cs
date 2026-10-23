using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.Competition;

namespace Tactical11Manager.Infrastructure.Tests.Fans;

/// <summary>
/// The fans through the matchday workflow against real PostgreSQL: the opening numbers, the expectation frozen at the lock, the result
/// applied at publication, and the weekly review (`FAN-1`, `FAN-4`, `FAN-5`, `FAN-8`, `FAN-10`).
/// </summary>
/// <remarks>
/// The tests share the matchday fixture's seeded world, so each one claims an untouched round of the first division for itself and reads
/// only the rows that round wrote.
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class FanMatchdayTests
{
    /// <summary>Initializes the tests.</summary>
    public FanMatchdayTests(MatchdayFixture fixture) => Fixture = fixture;

    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task Every_club_of_a_seeded_world_opens_with_its_tiers_fans_and_one_opening_event()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var clubs = await db.Clubs.CountAsync();
        var tierOf = await TiersOfClubsAsync(db);
        var bases = await db.ClubFanBases.AsNoTracking().ToListAsync();

        clubs.Should().BeGreaterThan(0);
        bases.Should().HaveCount(clubs, "every club, human or AI, has a fan base (FAN-1)");

        // Compared with the opening events rather than the numbers: other tests in the collection play rounds in the same world.
        var opening = await db.ClubFanEvents.AsNoTracking().Where(fanEvent => fanEvent.Kind == FanEventKind.Opening).ToListAsync();

        opening.Should().HaveCount(clubs, "one opening event for each");
        opening.Should().OnlyContain(fanEvent => fanEvent.MoodAfterBp == FanRuleSet.CalmMoodBp && fanEvent.FansBefore == fanEvent.FansAfter);

        foreach (var fanEvent in opening)
        {
            fanEvent.FansAfter.Should().Be(FanRuleSet.StartFansFor(tierOf[fanEvent.ClubId]));
        }

        bases.Should().OnlyContain(fanBase => fanBase.RuleSetVersion == FanRuleSet.Version);
    }

    [Fact]
    public async Task Locking_a_round_freezes_both_sides_expectations_once()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(db);

        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);

        await using var read = Fixture.CreateScope();
        var readDb = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fixtures = await readDb.Fixtures.Where(fixture => fixture.MatchdayId == matchdayId).ToListAsync();
        var fixtureIds = fixtures.Select(fixture => fixture.Id).ToList();
        var frozen = await readDb.FixtureFanExpectations.AsNoTracking().Where(expectation => fixtureIds.Contains(expectation.FixtureId)).ToListAsync();

        frozen.Should().HaveCount(fixtures.Count * 2, "each fixture has one expectation for each side (FAN-4)");
        frozen.Should().OnlyContain(expectation => expectation.WinBp + expectation.DrawBp + expectation.LossBp == 10_000);

        foreach (var fixture in fixtures)
        {
            var home = frozen.Single(expectation => expectation.FixtureId == fixture.Id && expectation.ClubId == fixture.HomeClubId);
            var away = frozen.Single(expectation => expectation.FixtureId == fixture.Id && expectation.ClubId == fixture.AwayClubId);

            home.WinBp.Should().Be(away.LossBp, "the two sides see the same match from opposite ends");
            home.LossBp.Should().Be(away.WinBp);
            home.DrawBp.Should().Be(away.DrawBp);
        }

        // Locking again freezes nothing new and changes nothing it already held.
        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);

        await using var again = Fixture.CreateScope();
        var after = await again.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>()
            .FixtureFanExpectations.AsNoTracking()
            .Where(expectation => fixtureIds.Contains(expectation.FixtureId))
            .ToListAsync();

        after.Should().BeEquivalentTo(frozen);
    }

    [Fact]
    public async Task Publishing_a_round_applies_each_result_to_the_fans_once_and_a_republish_changes_nothing()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(db);

        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>().ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        List<Guid> clubIds;
        Dictionary<Guid, (int Fans, int MoodBp)> before;

        await using (var snapshot = Fixture.CreateScope())
        {
            var snapshotDb = snapshot.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            clubIds = await ClubsOfRoundAsync(snapshotDb, matchdayId);
            before = await FansOfAsync(snapshotDb, clubIds);
        }

        var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);

        published.Outcome.Should().Be(PublishMatchdayOutcome.Published);

        await using var read = Fixture.CreateScope();
        var readDb = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fixtures = await readDb.Fixtures.AsNoTracking().Where(fixture => fixture.MatchdayId == matchdayId).ToListAsync();
        var fixtureIds = fixtures.Select(fixture => fixture.Id).ToList();
        var events = await readDb.ClubFanEvents.AsNoTracking()
            .Where(fanEvent => fanEvent.Kind == FanEventKind.Match && fixtureIds.Contains(fanEvent.FixtureId!.Value))
            .ToListAsync();

        events.Should().HaveCount(fixtures.Count * 2, "a result moves the fans of both clubs, once each (FAN-5)");
        events.Select(fanEvent => (fanEvent.ClubId, fanEvent.FixtureId)).Should().OnlyHaveUniqueItems();

        var tier = (await TiersOfClubsAsync(readDb))[clubIds[0]];
        var firstSeason = await FirstSeasonClubsAsync(readDb, clubIds);
        var frozen = await readDb.FixtureFanExpectations.AsNoTracking()
            .Where(expectation => fixtureIds.Contains(expectation.FixtureId))
            .ToDictionaryAsync(expectation => (expectation.FixtureId, expectation.ClubId));

        foreach (var fanEvent in events)
        {
            var fixture = fixtures.Single(candidate => candidate.Id == fanEvent.FixtureId);
            var home = fanEvent.ClubId == fixture.HomeClubId;
            var expected = frozen[(fixture.Id, fanEvent.ClubId)].ExpectedPointsCentis;

            fanEvent.ExpectedPointsCentis.Should().Be(expected, "the result is judged against what the fans held at the lock (FAN-4)");

            // The stored change is exactly what the rules give for the stored result.
            var rules = FanRuleSet.AfterMatch(
                new FanBaseState(before[fanEvent.ClubId].Fans, before[fanEvent.ClubId].MoodBp),
                tier,
                expected,
                home ? fixture.HomeScore!.Value : fixture.AwayScore!.Value,
                home ? fixture.AwayScore!.Value : fixture.HomeScore!.Value,
                fixture.Id,
                fanEvent.ClubId,
                firstSeason.Contains(fanEvent.ClubId));

            fanEvent.FansBefore.Should().Be(before[fanEvent.ClubId].Fans);
            fanEvent.MoodBeforeBp.Should().Be(before[fanEvent.ClubId].MoodBp);
            fanEvent.FansAfter.Should().Be(rules.Fans);
            fanEvent.MoodAfterBp.Should().Be(rules.MoodBp);
        }

        var afterPublish = await FansOfAsync(readDb, clubIds);

        // A republish returns before it reaches the fans.
        var again = await scope.ServiceProvider.GetRequiredService<PublishMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);

        again.Outcome.Should().Be(PublishMatchdayOutcome.AlreadyPublished);

        await using var second = Fixture.CreateScope();
        var secondDb = second.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await FansOfAsync(secondDb, clubIds)).Should().BeEquivalentTo(afterPublish);
        (await secondDb.ClubFanEvents.CountAsync(fanEvent => fanEvent.Kind == FanEventKind.Match && fixtureIds.Contains(fanEvent.FixtureId!.Value)))
            .Should().Be(events.Count);
    }

    [Fact]
    public async Task The_weekly_review_judges_each_club_that_has_played_once_a_week()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var matchdayId = await ClaimRoundAsync(db);

        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>().ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<PublishMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);

        // Any Sunday will do: the review is keyed by the week it is given and reads no clock but to stamp its events.
        var week = new DateOnly(2031, 3, 2);

        RunWeeklyFanReviewResult first;

        await using (var run = Fixture.CreateScope())
        {
            first = await run.ServiceProvider.GetRequiredService<RunWeeklyFanReview>().ExecuteAsync(week, CancellationToken.None);
        }

        await using var read = Fixture.CreateScope();
        var readDb = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var played = await readDb.Standings.AsNoTracking().Where(standing => standing.Played > 0).Select(standing => standing.ClubId).ToListAsync();
        var reviews = await readDb.ClubFanEvents.AsNoTracking()
            .Where(fanEvent => fanEvent.Kind == FanEventKind.Weekly && fanEvent.WeekOf == week)
            .ToListAsync();

        first.Reviewed.Should().Be(played.Count);
        reviews.Select(review => review.ClubId).Should().BeEquivalentTo(played, "a club that has not played has no projected place");
        reviews.Should().OnlyContain(review => review.Gap != null);

        var bases = await readDb.ClubFanBases.AsNoTracking().Where(fanBase => played.Contains(fanBase.ClubId)).ToListAsync();

        bases.Should().OnlyContain(fanBase => fanBase.SeasonTargetRank != null, "the review gives a club that has no target one from its squad");
        bases.Should().OnlyContain(fanBase => fanBase.LastWeekGap != null);

        // The same week again changes nothing.
        RunWeeklyFanReviewResult second;

        await using (var run = Fixture.CreateScope())
        {
            second = await run.ServiceProvider.GetRequiredService<RunWeeklyFanReview>().ExecuteAsync(week, CancellationToken.None);
        }

        second.Reviewed.Should().Be(0);
        second.Skipped.Should().BeGreaterThanOrEqualTo(played.Count);

        await using var last = Fixture.CreateScope();
        var lastDb = last.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await lastDb.ClubFanEvents.CountAsync(fanEvent => fanEvent.Kind == FanEventKind.Weekly && fanEvent.WeekOf == week))
            .Should().Be(reviews.Count);
        (await lastDb.ClubFanBases.AsNoTracking().Where(fanBase => played.Contains(fanBase.ClubId)).ToListAsync())
            .Should().BeEquivalentTo(bases);
    }

    private static async Task<Guid> ClaimRoundAsync(Tactical11ManagerDbContext db)
    {
        var divisionSeason = await db.DivisionSeasons.OrderBy(candidate => candidate.CreatedAt).FirstAsync();

        var matchdayId = await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeason.Id
                && matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && !db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstOrDefaultAsync();

        matchdayId.Should().NotBeEmpty("the seeded world has thirty rounds and these tests use a handful");

        return matchdayId;
    }

    private static async Task<List<Guid>> ClubsOfRoundAsync(Tactical11ManagerDbContext db, Guid matchdayId) =>
        (await db.Fixtures.AsNoTracking().Where(fixture => fixture.MatchdayId == matchdayId).ToListAsync())
            .SelectMany(fixture => new[] { fixture.HomeClubId, fixture.AwayClubId })
            .Distinct()
            .ToList();

    private static async Task<Dictionary<Guid, (int Fans, int MoodBp)>> FansOfAsync(Tactical11ManagerDbContext db, List<Guid> clubIds) =>
        (await db.ClubFanBases.AsNoTracking().Where(fanBase => clubIds.Contains(fanBase.ClubId)).ToListAsync())
            .ToDictionary(fanBase => fanBase.ClubId, fanBase => (fanBase.Fans, fanBase.MoodBp));

    private static async Task<Dictionary<Guid, int>> TiersOfClubsAsync(Tactical11ManagerDbContext db) =>
        await (
            from entry in db.ClubSeasonEntries
            join divisionSeason in db.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in db.Divisions on divisionSeason.DivisionId equals division.Id
            select new { entry.ClubId, division.TierNumber })
            .ToDictionaryAsync(row => row.ClubId, row => row.TierNumber);

    private static async Task<HashSet<Guid>> FirstSeasonClubsAsync(Tactical11ManagerDbContext db, List<Guid> clubIds) =>
        (await db.ClubSeasonEntries.AsNoTracking()
            .Where(entry => clubIds.Contains(entry.ClubId))
            .GroupBy(entry => entry.ClubId)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToListAsync())
            .ToHashSet();
}
