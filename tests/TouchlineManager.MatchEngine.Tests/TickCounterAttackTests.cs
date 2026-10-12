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

    private static TickPlayerSkills Skills(int value) =>
        TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(value, MatchAttributeNames.Count).ToArray()));
}
