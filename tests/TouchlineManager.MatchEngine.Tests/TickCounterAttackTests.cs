using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the counter-attack in the tick engine (`weaker-side-v1`, Milestone 1): when a break opens and shuts, and how a carrier on a
/// break plays.
/// </summary>
public sealed class TickCounterAttackTests
{
    [Fact]
    public void A_break_opens_only_for_a_side_that_counter_attacks_and_won_the_ball_in_its_own_sixty_per_cent()
    {
        var sixty = SpatialPitch.PitchLength * 6 / 10;

        TickMatchLoop.OpensBreak(counterAttack: true, isHome: true, ballX: sixty).Should().BeTrue();
        TickMatchLoop.OpensBreak(counterAttack: true, isHome: true, ballX: sixty + 1).Should().BeFalse("the ball was won too far up the pitch to be a break");
        TickMatchLoop.OpensBreak(counterAttack: true, isHome: true, ballX: 0).Should().BeTrue();
        TickMatchLoop.OpensBreak(counterAttack: false, isHome: true, ballX: 2_000).Should().BeFalse("a side that does not counter-attack has no break");

        // The away side defends the high end: its own 60% is the other way round.
        TickMatchLoop.OpensBreak(counterAttack: true, isHome: false, ballX: SpatialPitch.PitchLength - sixty).Should().BeTrue();
        TickMatchLoop.OpensBreak(counterAttack: true, isHome: false, ballX: SpatialPitch.PitchLength - sixty - 1).Should().BeFalse();
        TickMatchLoop.OpensBreak(counterAttack: true, isHome: false, ballX: SpatialPitch.PitchLength - 500).Should().BeTrue();
    }

    [Fact]
    public void A_break_ends_with_a_shot_a_ball_played_back_or_a_hoof_and_not_with_a_ball_played_forward()
    {
        foreach (var action in new[] { TickCarrierAction.Shoot, TickCarrierAction.Recycle, TickCarrierAction.Clear })
        {
            TickMatchLoop.EndsBreak(action).Should().BeTrue($"{action} ends it");
        }

        foreach (var action in new[] { TickCarrierAction.Pass, TickCarrierAction.ThroughBall, TickCarrierAction.Cross, TickCarrierAction.Dribble })
        {
            TickMatchLoop.EndsBreak(action).Should().BeFalse($"{action} goes on");
        }
    }

    [Fact]
    public void A_side_on_a_break_decides_a_tick_sooner_and_looks_up_for_less_time()
    {
        var skills = Skills(10);

        TickBallCarrierBrain.DecisionInterval(skills, MatchTempo.Normal).Should().Be(2);
        TickBallCarrierBrain.DecisionInterval(skills, MatchTempo.Normal, breaking: true).Should().Be(1);

        var pass = new TickCarrierDecision(TickCarrierAction.Pass, 9, new SpatialPoint(7_000, 3_000), 1_000, 10_000, 0);
        var ordinary = TickMatchLoop.HoldTicks(pass, default, MatchTempo.Normal, keeper: false);
        var quick = TickMatchLoop.HoldTicks(pass, default, MatchTempo.Normal, keeper: false, breaking: true);

        quick.Should().BeLessThan(ordinary);
        quick.Should().BeGreaterThanOrEqualTo(3, "his first touch is still his first touch");

        var through = pass with { Action = TickCarrierAction.ThroughBall };

        TickMatchLoop.HoldTicks(through, default, MatchTempo.Normal, keeper: false, breaking: true)
            .Should().Be(TickMatchLoop.HoldTicks(through, default, MatchTempo.Normal, keeper: false));
    }

    [Theory]
    [InlineData(MatchMentality.Balanced, MatchPressing.MidBlock, MatchDefensiveLine.Normal, 0)]
    [InlineData(MatchMentality.Positive, MatchPressing.HighPress, MatchDefensiveLine.High, 3)]
    [InlineData(MatchMentality.Attacking, MatchPressing.HighPress, MatchDefensiveLine.High, 4)]
    [InlineData(MatchMentality.Defensive, MatchPressing.LowBlock, MatchDefensiveLine.Deep, -4)]
    [InlineData(MatchMentality.Cautious, MatchPressing.LowBlock, MatchDefensiveLine.Deep, -3)]
    [InlineData(MatchMentality.Attacking, MatchPressing.LowBlock, MatchDefensiveLine.Deep, 0)]
    [InlineData(MatchMentality.Balanced, MatchPressing.HighPress, MatchDefensiveLine.Normal, 1)]
    public void The_posture_of_a_side_is_its_mentality_its_line_and_its_pressing_and_not_its_shape(
        MatchMentality mentality,
        MatchPressing pressing,
        MatchDefensiveLine line,
        int posture)
    {
        var instructions = new MatchInstructionsV1 { Mentality = mentality, Pressing = pressing, DefensiveLine = line };

        TickTeamStyle.Posture(instructions).Should().Be(posture);
    }

    [Fact]
    public void The_default_instructions_have_a_posture_of_nothing()
    {
        TickTeamStyle.Posture(new MatchInstructionsV1()).Should().Be(0);
    }

    [Fact]
    public void A_break_is_longest_and_busiest_against_an_attacking_side_neutral_against_a_balanced_one_and_smallest_against_a_deep_one()
    {
        var attacking = TickBreakGain.Plan(4, 100);
        var balanced = TickBreakGain.Plan(0, 100);
        var deep = TickBreakGain.Plan(-4, 100);

        attacking.Ticks.Should().BeGreaterThan(balanced.Ticks);
        balanced.Ticks.Should().BeGreaterThan(deep.Ticks);
        attacking.Runners.Should().BeGreaterThanOrEqualTo(balanced.Runners);
        balanced.Runners.Should().BeGreaterThan(deep.Runners);
        deep.Runners.Should().Be(0, "a packed defence is not run in behind");
        deep.Immediate.Should().BeFalse("the runners wait for a free carrier against a packed defence");
        deep.Ticks.Should().Be(0, "there is nothing to break into against a packed defence");
        deep.ProgressPercent.Should().Be(0);
        attacking.ProgressPercent.Should().BeGreaterThanOrEqualTo(balanced.ProgressPercent, "the hurry is capped at the balanced gain so that it cannot run away");
        balanced.ProgressPercent.Should().BeGreaterThan(0);

        for (var posture = -4; posture < 4; posture++)
        {
            TickBreakGain.Plan(posture + 1, 100).Ticks.Should().BeGreaterThanOrEqualTo(TickBreakGain.Plan(posture, 100).Ticks);
        }
    }

    [Fact]
    public void The_speed_factor_is_neutral_without_an_edge_falls_as_the_opponent_is_quicker_and_rises_as_the_breakers_are()
    {
        TickBreakGain.SpeedFactorPercent(0).Should().Be(100);
        TickBreakGain.SpeedFactorPercent(100).Should().BeLessThan(100);
        TickBreakGain.SpeedFactorPercent(200).Should().BeLessThan(TickBreakGain.SpeedFactorPercent(100));
        TickBreakGain.SpeedFactorPercent(-50).Should().BeGreaterThan(100);
        TickBreakGain.SpeedFactorPercent(-100).Should().BeGreaterThan(TickBreakGain.SpeedFactorPercent(-50));

        TickBreakGain.SpeedFactorPercent(10_000).Should().Be(TickBreakGain.SpeedFloorPercent);
        TickBreakGain.SpeedFactorPercent(-10_000).Should().Be(TickBreakGain.SpeedCeilingPercent);
    }

    [Fact]
    public void The_speed_factor_depends_on_the_edge_and_not_on_the_level_of_the_two_groups()
    {
        // Two groups of the same speed are neutral at any level; a gap of two points counts the same at 13 against 15 as at 18 against 20.
        static int Edge(int recoverers, int breakers) => (recoverers - breakers) * 100;

        TickBreakGain.SpeedFactorPercent(Edge(13, 13)).Should().Be(100);
        TickBreakGain.SpeedFactorPercent(Edge(19, 19)).Should().Be(100);
        TickBreakGain.SpeedFactorPercent(Edge(15, 13)).Should().Be(TickBreakGain.SpeedFactorPercent(Edge(20, 18)));
        TickBreakGain.SpeedFactorPercent(Edge(13, 15)).Should().Be(TickBreakGain.SpeedFactorPercent(Edge(18, 20)));
    }

    [Fact]
    public void The_speed_edge_of_two_real_sides_is_the_same_at_every_level_and_neutral_when_they_are_equally_quick()
    {
        // The breakers are the countering side's Attack and Midfield men, the recoverers the opponent's Defence and Midfield men.
        static int Edge(int recovererAbility, int breakerAbility)
        {
            var state = TickTestMatchState.Create(homeAbility: breakerAbility, awayAbility: recovererAbility);
            var breakers = new TickTeam(state.Home);
            var recoverers = new TickTeam(state.Away);

            return TickBreakGain.GroupSpeed(recoverers, MatchPositionFamily.Defence, MatchPositionFamily.Midfield)
                - TickBreakGain.GroupSpeed(breakers, MatchPositionFamily.Attack, MatchPositionFamily.Midfield);
        }

        Edge(13, 13).Should().BeInRange(-2, 2);
        Edge(19, 19).Should().BeInRange(-2, 2);
        TickBreakGain.SpeedFactorPercent(Edge(13, 13)).Should().Be(100);
        TickBreakGain.SpeedFactorPercent(Edge(19, 19)).Should().Be(100);

        Edge(15, 13).Should().BeInRange(170, 200, "two points of Pace and Acceleration, a little less because the men start a little below full energy");
        Edge(20, 18).Should().BeCloseTo(Edge(15, 13), 3, "two points of speed are two points at 13 and at 18");
        Edge(13, 15).Should().BeInRange(-200, -170);
        TickBreakGain.SpeedFactorPercent(Edge(20, 18)).Should().BeCloseTo(TickBreakGain.SpeedFactorPercent(Edge(15, 13)), 1);
        TickBreakGain.SpeedFactorPercent(Edge(13, 15)).Should().BeGreaterThan(100);
    }

    [Fact]
    public void A_quicker_opponent_shrinks_the_break_and_quicker_breakers_grow_it()
    {
        var neutral = TickBreakGain.Plan(3, 100);
        var caught = TickBreakGain.Plan(3, TickBreakGain.SpeedFloorPercent);
        var away = TickBreakGain.Plan(3, TickBreakGain.SpeedCeilingPercent);

        caught.Ticks.Should().BeLessThan(neutral.Ticks);
        away.Ticks.Should().BeGreaterThan(neutral.Ticks);
        caught.Runners.Should().BeLessThanOrEqualTo(neutral.Runners);
        away.Runners.Should().BeGreaterThanOrEqualTo(neutral.Runners);
    }

    private static TickPlayerSkills Skills(int value) =>
        TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(value, MatchAttributeNames.Count).ToArray()));
}
