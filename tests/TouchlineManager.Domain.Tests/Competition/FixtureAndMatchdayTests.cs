using FluentAssertions;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Tests.Competition;

/// <summary>
/// The fixture and matchday lifecycles (`CAL-3`, `CAL-10`, `MAT-7`).
/// </summary>
/// <remarks>
/// The lock, resolve, and publish workflow runs from a durable job that may be retried at any boundary
/// (§7.4, ADR-0003), so the interesting property is not only that a transition is refused out of order but
/// that repeating a transition that has already happened is harmless rather than an error.
/// </remarks>
public sealed class FixtureAndMatchdayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fixture_is_scheduled_between_two_different_clubs()
    {
        var home = Guid.CreateVersion7();
        var away = Guid.CreateVersion7();

        var fixture = Fixture.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), home, away, Now, Now);

        fixture.HomeClubId.Should().Be(home);
        fixture.AwayClubId.Should().Be(away);
        fixture.KickoffAt.Should().Be(Now);
        fixture.Status.Should().Be(FixtureStatus.Scheduled);
        fixture.HomeScore.Should().BeNull("a fixture carries no score before it is staged (MAT-7)");
        fixture.MatchId.Should().BeNull();
        fixture.Version.Should().Be(1);
    }

    [Fact]
    public void A_club_cannot_play_itself()
    {
        var club = Guid.CreateVersion7();

        var act = () => Fixture.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), club, club, Now, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_fixture_locks_once_and_only_from_scheduled()
    {
        var fixture = Scheduled();

        fixture.Lock(Now);

        fixture.Status.Should().Be(FixtureStatus.Locked);
        fixture.Version.Should().Be(2);

        var again = () => fixture.Lock(Now);
        again.Should().Throw<InvalidOperationException>("a locked fixture is frozen (CAL-3)");
    }

    [Fact]
    public void A_simulation_may_only_start_from_locked()
    {
        var fixture = Scheduled();

        var tooEarly = () => fixture.BeginSimulation(Now);
        tooEarly.Should().Throw<InvalidOperationException>("an unlocked fixture has no snapshot to simulate (MAT-1)");

        fixture.Lock(Now);
        fixture.BeginSimulation(Now);

        fixture.Status.Should().Be(FixtureStatus.Simulating);
    }

    [Fact]
    public void A_result_stages_without_publishing_and_carries_no_score_until_it_does()
    {
        var fixture = Locked();
        var match = Guid.CreateVersion7();

        fixture.Stage(homeScore: 2, awayScore: 1, match, Now);

        fixture.Status.Should().Be(FixtureStatus.Staged);
        fixture.HomeScore.Should().Be(2);
        fixture.AwayScore.Should().Be(1);
        fixture.MatchId.Should().Be(match);
        fixture.PublishedAt.Should().BeNull("staging is not publication (MAT-7)");

        FixtureStatusRules.CarriesScore(fixture.Status).Should().BeTrue();
    }

    [Fact]
    public void Staging_is_idempotent_so_a_retried_simulation_is_not_an_error()
    {
        var fixture = Locked();
        var match = Guid.CreateVersion7();

        fixture.Stage(1, 1, match, Now);
        var version = fixture.Version;

        fixture.Stage(1, 1, match, Now.AddMinutes(1));

        fixture.Version.Should().Be(version, "the same result staged again changes nothing");
    }

    [Fact]
    public void A_negative_score_or_a_missing_match_is_refused()
    {
        var fixture = Locked();

        var negative = () => fixture.Stage(-1, 0, Guid.CreateVersion7(), Now);
        var noMatch = () => fixture.Stage(0, 0, Guid.Empty, Now);

        negative.Should().Throw<ArgumentOutOfRangeException>();
        noMatch.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_fixture_publishes_only_after_it_stages_and_publication_is_terminal()
    {
        var fixture = Locked();

        var tooEarly = () => fixture.Publish(Now);
        tooEarly.Should().Throw<InvalidOperationException>("publication waits for a staged result (MAT-7)");

        fixture.Stage(0, 0, Guid.CreateVersion7(), Now);
        fixture.Publish(Now);

        fixture.Status.Should().Be(FixtureStatus.Published);
        fixture.PublishedAt.Should().Be(Now);

        var version = fixture.Version;
        fixture.Publish(Now.AddMinutes(1));
        fixture.Version.Should().Be(version, "a retried publication is a no-op");
    }

    [Fact]
    public void Voiding_clears_a_staged_result_but_a_published_fixture_is_history()
    {
        var staged = Locked();
        staged.Stage(3, 0, Guid.CreateVersion7(), Now);

        staged.Void("abandoned", Now);

        staged.Status.Should().Be(FixtureStatus.Void);
        staged.HomeScore.Should().BeNull();
        staged.MatchId.Should().BeNull();

        var published = Locked();
        published.Stage(1, 0, Guid.CreateVersion7(), Now);
        published.Publish(Now);

        var act = () => published.Void("too late", Now);
        act.Should().Throw<InvalidOperationException>("a published fixture is history (MAT-10)");
    }

    [Fact]
    public void A_matchday_derives_its_lock_from_its_kickoff()
    {
        var matchday = Matchday.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Now, Now);

        matchday.KickoffAt.Should().Be(Now);
        matchday.LockAt.Should().Be(Now.AddMinutes(-WorldRuleSet.TeamSheetLockMinutes), "CAL-3 is thirty minutes");
        matchday.PublicationStatus.Should().Be(MatchdayPublicationStatus.Pending);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    public void A_matchday_round_must_be_within_the_season(int round)
    {
        var act = () => Matchday.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), round, Now, Now);

        act.Should().Throw<ArgumentOutOfRangeException>("CAL-1 fixes the season at thirty-four rounds");
    }

    [Fact]
    public void A_matchday_publishes_only_once_every_fixture_has_staged()
    {
        var matchday = Matchday.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Now, Now);

        var tooEarly = () => matchday.Publish(Now);
        tooEarly.Should().Throw<InvalidOperationException>("a round publishes as a unit (MAT-7)");

        matchday.MarkStaged(Now);
        matchday.MarkStaged(Now.AddMinutes(1));

        matchday.PublicationStatus.Should().Be(MatchdayPublicationStatus.Staged, "staging twice is harmless");

        matchday.Publish(Now);
        var version = matchday.Version;

        matchday.Publish(Now.AddMinutes(1));

        matchday.PublicationStatus.Should().Be(MatchdayPublicationStatus.Published);
        matchday.Version.Should().Be(version, "a retried publication is a no-op");
    }

    [Fact]
    public void Status_codes_round_trip()
    {
        foreach (var status in Enum.GetValues<FixtureStatus>())
        {
            FixtureStatusRules.FromCode(status.ToCode()).Should().Be(status);
        }

        foreach (var status in Enum.GetValues<MatchdayPublicationStatus>())
        {
            MatchdayPublicationStatusRules.FromCode(status.ToCode()).Should().Be(status);
        }

        var unknown = () => FixtureStatusRules.FromCode("nonsense");

        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Fixture Scheduled() =>
        Fixture.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Now);

    private static Fixture Locked()
    {
        var fixture = Scheduled();
        fixture.Lock(Now);

        return fixture;
    }
}
