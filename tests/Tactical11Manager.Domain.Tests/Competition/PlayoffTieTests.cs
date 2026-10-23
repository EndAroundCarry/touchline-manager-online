using FluentAssertions;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Domain.Tests.Competition;

/// <summary>The play-off tie's lifecycle (`PR-4`, `PR-6`). Publication is at-least-once, so repeating a step must be harmless.</summary>
public sealed class PlayoffTieTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 20, 19, 0, 0, TimeSpan.Zero);

    private static readonly Guid Parent = Guid.CreateVersion7();
    private static readonly Guid Feeder = Guid.CreateVersion7();
    private static readonly Guid Home = Guid.CreateVersion7();
    private static readonly Guid Away = Guid.CreateVersion7();

    [Fact]
    public void A_semi_final_is_drawn_with_both_clubs()
    {
        var tie = Semi();

        tie.Stage.Should().Be(PlayoffStage.SemiFinal);
        tie.HomeClubId.Should().Be(Home);
        tie.AwayClubId.Should().Be(Away);
        tie.HasBothSides.Should().BeTrue();
        tie.IsDecided.Should().BeFalse();
        tie.Version.Should().Be(1);
    }

    [Fact]
    public void A_final_is_drawn_without_its_away_side_and_fills_it_once()
    {
        var final = Final();

        final.HasBothSides.Should().BeFalse();

        final.FillAway(Away, Feeder, 3, Now);
        var version = final.Version;
        final.FillAway(Away, Feeder, 3, Now.AddMinutes(1));

        final.AwayClubId.Should().Be(Away);
        final.AwayPlace.Should().Be(3);
        final.Version.Should().Be(version, "naming the same club again changes nothing");

        var other = () => final.FillAway(Guid.CreateVersion7(), Feeder, 4, Now);
        other.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_club_cannot_play_itself()
    {
        var final = Final();

        var act = () => final.FillAway(Home, Parent, 14, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_semi_final_drawn_without_its_away_side_is_refused()
    {
        var pairing = SemiPairing() with { AwayClubId = null, AwayDivisionId = null, AwayPlace = null };

        var act = () => PlayoffTie.Draw(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), pairing, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_fixture_attaches_once_and_only_when_both_clubs_are_known()
    {
        var final = Final();
        var fixture = Guid.CreateVersion7();

        var early = () => final.AttachFixture(fixture, Now);
        early.Should().Throw<InvalidOperationException>();

        final.FillAway(Away, Feeder, 3, Now);
        final.AttachFixture(fixture, Now);
        var version = final.Version;
        final.AttachFixture(fixture, Now.AddMinutes(1));

        final.FixtureId.Should().Be(fixture);
        final.Version.Should().Be(version);

        var again = () => final.AttachFixture(Guid.CreateVersion7(), Now);
        again.Should().Throw<InvalidOperationException>("a tie is played in one match");
    }

    [Fact]
    public void A_tie_decided_on_the_score_has_a_winner_and_a_loser()
    {
        var tie = Semi();
        tie.AttachFixture(Guid.CreateVersion7(), Now);

        tie.Decide(Away, PlayoffDecision.Score, null, null, null, Now);

        tie.IsDecided.Should().BeTrue();
        tie.WinnerClubId.Should().Be(Away);
        tie.LoserClubId.Should().Be(Home);
        tie.DecidedBy.Should().Be(PlayoffDecision.Score);
        tie.ShootoutHomeScore.Should().BeNull();
    }

    [Fact]
    public void A_tie_decided_on_penalties_keeps_the_shoot_out()
    {
        var tie = Semi();
        tie.AttachFixture(Guid.CreateVersion7(), Now);

        tie.Decide(Home, PlayoffDecision.Penalties, 4, 3, "kicks", Now);

        tie.WinnerClubId.Should().Be(Home);
        tie.DecidedBy.Should().Be(PlayoffDecision.Penalties);
        tie.ShootoutHomeScore.Should().Be(4);
        tie.ShootoutAwayScore.Should().Be(3);
        tie.ShootoutKicks.Should().Be("kicks");
    }

    [Fact]
    public void Deciding_is_idempotent_and_the_winner_is_kept()
    {
        var tie = Semi();
        tie.AttachFixture(Guid.CreateVersion7(), Now);
        tie.Decide(Home, PlayoffDecision.Score, null, null, null, Now);
        var version = tie.Version;

        tie.Decide(Home, PlayoffDecision.Score, null, null, null, Now.AddMinutes(1));

        tie.Version.Should().Be(version);

        var other = () => tie.Decide(Away, PlayoffDecision.Score, null, null, null, Now);
        other.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_decision_must_be_consistent()
    {
        var undecidable = Semi();
        var early = () => undecidable.Decide(Home, PlayoffDecision.Score, null, null, null, Now);
        early.Should().Throw<InvalidOperationException>("a tie is decided by its match");

        var tie = Semi();
        tie.AttachFixture(Guid.CreateVersion7(), Now);

        var stranger = () => tie.Decide(Guid.CreateVersion7(), PlayoffDecision.Score, null, null, null, Now);
        var noScore = () => tie.Decide(Home, PlayoffDecision.Penalties, null, null, null, Now);
        var wrongWay = () => tie.Decide(Home, PlayoffDecision.Penalties, 2, 3, null, Now);
        var level = () => tie.Decide(Home, PlayoffDecision.Penalties, 3, 3, null, Now);
        var scoreWithShootout = () => tie.Decide(Home, PlayoffDecision.Score, 3, 2, null, Now);

        stranger.Should().Throw<ArgumentException>();
        noScore.Should().Throw<ArgumentException>();
        wrongWay.Should().Throw<ArgumentException>();
        level.Should().Throw<ArgumentException>();
        scoreWithShootout.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Stage_and_decision_codes_round_trip()
    {
        foreach (var stage in Enum.GetValues<PlayoffStage>())
        {
            PlayoffStageRules.FromCode(stage.ToCode()).Should().Be(stage);
            stage.Label().Should().StartWith("Play-off");
        }

        foreach (var decision in Enum.GetValues<PlayoffDecision>())
        {
            PlayoffDecisionRules.FromCode(decision.ToCode()).Should().Be(decision);
        }

        foreach (var kind in Enum.GetValues<MatchdayKind>())
        {
            MatchdayKindRules.FromCode(kind.ToCode()).Should().Be(kind);
        }

        var unknown = () => PlayoffStageRules.FromCode("nonsense");
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static PlayoffPairing SemiPairing() =>
        new(PlayoffStage.SemiFinal, 1, Parent, Feeder, Feeder, Home, Feeder, 3, Away, Feeder, 4);

    private static PlayoffTie Semi() =>
        PlayoffTie.Draw(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SemiPairing(), Now);

    private static PlayoffTie Final() =>
        PlayoffTie.Draw(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new PlayoffPairing(PlayoffStage.Final, 2, Parent, Feeder, Parent, Home, Parent, 14, null, null, null),
            Now);
}
