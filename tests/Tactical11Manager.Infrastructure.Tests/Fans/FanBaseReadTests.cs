using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Fans;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.World;

namespace Tactical11Manager.Infrastructure.Tests.Fans;

/// <summary>
/// The Fan Base page's read against real PostgreSQL: a manager's own club from the takeover on, the expectation for the next match
/// worked out live and then frozen at the lock, and the match played against it (`FAN-1`, `FAN-4`, `FAN-9`, `FAN-10`).
/// </summary>
/// <remarks>
/// Every club is claimed through the use case the API calls. The tests share the seeded world, so each one asks for a free club of Spain.
/// A round published here moves the fans of the clubs in it, so assertions are made on the claimed club's own rows.
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class FanBaseReadTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public FanBaseReadTests(WorldFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task A_new_manager_reads_the_tiers_defaults_and_a_next_match_worked_out_from_the_squads()
    {
        var (userId, clubId) = await ClaimAFreeClubAsync();

        var page = await ReadAsync(userId);
        var opening = FanRuleSet.OpeningFor(1);

        page.ClubId.Should().Be(clubId);
        page.TierNumber.Should().Be(1);
        page.Fans.Should().Be(opening.Fans);
        page.MoodBp.Should().Be(opening.MoodBp);
        page.MoodCode.Should().Be("calm");
        page.MoodPhrase.Should().Be("Calm");
        page.WantToCome.Should().Be(FanRuleSet.WantToComeFor(opening.Fans));
        page.HomeDemand.Should().Be(StadiumRuleSet.MatchdayDemandFor(opening.Fans, opening.MoodBp), "the crowd the stadium page quotes");
        page.FloorFans.Should().Be(FanRuleSet.FloorFansFor(1));
        page.SoftCapFans.Should().Be(FanRuleSet.SoftCapFansFor(1));
        page.SeasonTargetRank.Should().BeInRange(1, 18);
        page.LastWeekGap.Should().BeNull("judging closer or further from the target starts from the takeover");
        page.SeasonFansChange.Should().Be(0);
        page.RecentMatches.Should().BeEmpty("no round of the season has been played");

        // The history of this manager starts at the takeover.
        page.History.Should().Be(FanHistoryScopes.Tenure);
        page.TenureStartedAt.Should().NotBeNull();
        page.Events.Should().ContainSingle().Which.Kind.Should().Be("takeover");
        page.Events[0].FansAfter.Should().Be(opening.Fans);
        page.Events[0].MoodAfterBp.Should().Be(opening.MoodBp);
        page.Events[0].OccurredAt.Should().Be(page.TenureStartedAt!.Value);

        // Nothing is locked yet, so the expectation is the live one and says so.
        var next = page.NextFixture!;

        next.Frozen.Should().BeFalse();
        next.Fixture.Status.Should().Be("scheduled");
        next.Expectation.WinBp.Should().BeGreaterThan(0);
        (next.Expectation.WinBp + next.Expectation.DrawBp + next.Expectation.LossBp).Should().Be(10_000);

        var label = FanRuleSet.LabelFor(next.Expectation.ExpectedPointsCentis);

        next.Expectation.LabelCode.Should().Be(FanExpectationLabels.Code(label));
        next.Expectation.LabelPhrase.Should().Be(FanExpectationLabels.Phrase(label));
    }

    [Fact]
    public async Task The_whole_club_history_keeps_the_rows_before_the_takeover()
    {
        var (userId, _) = await ClaimAFreeClubAsync();

        var tenure = await ReadAsync(userId);
        var club = await ReadAsync(userId, FanHistoryScopes.Club);

        club.History.Should().Be(FanHistoryScopes.Club);
        // Other tests in the shared world may have played a round before this club was claimed, so only the ends are fixed.
        club.Events[0].Kind.Should().Be("opening");
        club.Events[^1].Kind.Should().Be("takeover");
        club.Events[^1].Id.Should().Be(tenure.Events[0].Id);
        club.Events.Should().HaveCountGreaterThan(tenure.Events.Count);
        club.Events.Select(fanEvent => fanEvent.OccurredAt).Should().BeInAscendingOrder();
        club.TenureStartedAt.Should().Be(tenure.TenureStartedAt, "the marker is the same takeover whichever history is asked for");
    }

    [Fact]
    public async Task A_history_the_page_does_not_serve_is_refused()
    {
        var (userId, _) = await ClaimAFreeClubAsync();

        await using var scope = Fixture.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<GetFanBase>().ExecuteAsync(userId, "forever", CancellationToken.None);

        result.InvalidHistory.Should().BeTrue();
        result.FanBase.Should().BeNull();
    }

    [Fact]
    public async Task A_manager_with_no_club_is_refused_the_way_the_other_club_reads_refuse()
    {
        await using var scope = Fixture.CreateScope();
        var userId = await CreateManagerAsync(scope, CancellationToken.None);

        var result = await scope.ServiceProvider.GetRequiredService<GetFanBase>().ExecuteAsync(userId, null, CancellationToken.None);

        result.Outcome.Should().Be(SquadReadOutcome.NoClub);
        result.FanBase.Should().BeNull();
    }

    [Fact]
    public async Task The_expectation_is_frozen_at_the_lock_and_the_match_played_against_it_is_listed()
    {
        var (userId, clubId) = await ClaimAFreeClubAsync();
        var before = await ReadAsync(userId);
        var fixtureId = before.NextFixture!.Fixture.Id;
        var matchdayId = await MatchdayOfAsync(fixtureId);

        await using (var scope = Fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);
        }

        var locked = await ReadAsync(userId);

        locked.NextFixture!.Fixture.Id.Should().Be(fixtureId);
        locked.NextFixture.Frozen.Should().BeTrue("the lock froze what the fans expect (FAN-4)");
        locked.NextFixture.Expectation.Should().NotBeNull();

        await using (var scope = Fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ResolveMatchday>().ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

            var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);

            published.Outcome.Should().Be(PublishMatchdayOutcome.Published);
        }

        var after = await ReadAsync(userId);
        var match = after.RecentMatches.Should().ContainSingle().Which;

        match.Fixture.Id.Should().Be(fixtureId);
        match.Fixture.Outcome.Should().BeOneOf("win", "draw", "loss");
        match.Expectation.Should().BeEquivalentTo(locked.NextFixture.Expectation, "the result is judged against what the fans held at the lock");
        match.PointsWon.Should().Be(match.Fixture.Outcome switch { "win" => 3, "draw" => 1, _ => 0 });
        match.FansBefore.Should().Be(before.Fans);
        match.FansAfter.Should().Be(after.Fans);
        match.MoodBeforeBp.Should().Be(before.MoodBp);
        match.MoodAfterBp.Should().Be(after.MoodBp);

        after.SeasonFansChange.Should().Be(after.Fans - before.Fans, "the season is counted from the takeover");
        after.NextFixture!.Fixture.Id.Should().NotBe(fixtureId, "the played match is no longer next");
        after.NextFixture.Frozen.Should().BeFalse();

        var matchEvent = after.Events.Should().HaveCount(2).And.Subject.Last();

        matchEvent.Kind.Should().Be("match");
        matchEvent.FixtureId.Should().Be(fixtureId);
        matchEvent.ExpectedPointsCentis.Should().Be(match.Expectation!.ExpectedPointsCentis);
        matchEvent.PointsWon.Should().Be(match.PointsWon);
        matchEvent.FansAfter.Should().Be(after.Fans);
        matchEvent.SeasonNumber.Should().Be(1);

        // The club's fans are public in summary: the word, not the number behind it.
        await using var summaryScope = Fixture.CreateScope();
        var summary = (await summaryScope.ServiceProvider.GetRequiredService<GetClubFanSummary>().ExecuteAsync(clubId, CancellationToken.None)).Summary!;

        summary.Fans.Should().Be(after.Fans);
        summary.MoodCode.Should().Be(after.MoodCode);
        summary.MoodPhrase.Should().Be(after.MoodPhrase);
    }

    [Fact]
    public async Task The_summary_of_a_club_that_does_not_exist_is_not_found()
    {
        await using var scope = Fixture.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<GetClubFanSummary>().ExecuteAsync(Guid.CreateVersion7(), CancellationToken.None);

        result.Outcome.Should().Be(SquadReadOutcome.ClubNotFound);
        result.Summary.Should().BeNull();
    }

    private async Task<(Guid UserId, Guid ClubId)> ClaimAFreeClubAsync()
    {
        await using var scope = Fixture.CreateScope();
        var clubId = await FreeClubIdAsync(scope, Fixture.Countries.Single(country => country.Code == "ESP").Id, CancellationToken.None);
        var userId = await CreateManagerAsync(scope, CancellationToken.None);

        var claimed = await scope.ServiceProvider
            .GetRequiredService<ClaimClub>()
            .ExecuteAsync(userId, new ClaimClubRequest { ClubId = clubId }, Guid.CreateVersion7().ToString(), CancellationToken.None);

        claimed.Outcome.Should().Be(ClaimClubOutcome.Claimed);

        return (userId, clubId);
    }

    private async Task<FanBaseResponse> ReadAsync(Guid userId, string? history = null)
    {
        await using var scope = Fixture.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<GetFanBase>().ExecuteAsync(userId, history, CancellationToken.None);

        result.Outcome.Should().Be(SquadReadOutcome.Found);
        result.InvalidHistory.Should().BeFalse();

        return result.FanBase!;
    }

    private async Task<Guid> MatchdayOfAsync(Guid fixtureId)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.Fixtures
            .Where(fixture => fixture.Id == fixtureId && fixture.Status == FixtureStatus.Scheduled)
            .Select(fixture => fixture.MatchdayId)
            .SingleAsync();
    }
}
