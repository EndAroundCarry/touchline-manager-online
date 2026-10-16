using FluentAssertions;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// Verifies the attack edge (`weaker-side-v2`, Milestone 6): how good a side's forwards are against the other side's defenders, and how
/// that scales what Positive and Attacking add to Balanced, down to less than Balanced when the forwards are worse.
/// </summary>
public sealed class TickAttackEdgeTests
{
    [Fact]
    public void The_factor_is_neutral_without_an_edge_falls_when_the_attackers_are_worse_and_stays_within_its_limits()
    {
        TickAttackEdge.Factor(0).Should().Be(TickAttackEdge.NeutralPercent);
        TickAttackEdge.Factor(100).Should().BeGreaterThan(100);
        TickAttackEdge.Factor(-100).Should().BeLessThan(100);
        TickAttackEdge.Factor(-200).Should().BeLessThan(TickAttackEdge.Factor(-100));

        TickAttackEdge.Factor(100_000).Should().Be(TickAttackEdge.CeilingPercent);
        TickAttackEdge.Factor(-100_000).Should().Be(TickAttackEdge.FloorPercent);
        TickAttackEdge.FloorPercent.Should().BeNegative("an attack that is outclassed must play worse than Balanced, not merely no better");
    }

    [Fact]
    public void An_extra_is_given_in_full_at_a_hundred_none_at_zero_and_turns_into_a_cost_below()
    {
        TickAttackEdge.Scaled(30, 100).Should().Be(30);
        TickAttackEdge.Scaled(30, 0).Should().Be(0);
        TickAttackEdge.Scaled(30, 50).Should().Be(15);
        TickAttackEdge.Scaled(30, -50).Should().Be(-15);
        TickAttackEdge.Scaled(2, 130).Should().Be(3);

        // A half rounds away from zero, in both directions.
        TickAttackEdge.Scaled(1, 50).Should().Be(1);
        TickAttackEdge.Scaled(1, -50).Should().Be(-1);
        TickAttackEdge.Scaled(1, 49).Should().Be(0);
    }

    [Theory]
    [InlineData(MatchMentality.Defensive)]
    [InlineData(MatchMentality.Cautious)]
    [InlineData(MatchMentality.Balanced)]
    public void Balanced_and_the_cautious_mentalities_are_never_scaled(MatchMentality mentality)
    {
        foreach (var factor in new[] { TickAttackEdge.FloorPercent, 0, 100, TickAttackEdge.CeilingPercent })
        {
            TickBallCarrierBrain.ProgressPercent(mentality, MatchTempo.Normal, 0, factor).Should().Be(TickBallCarrierBrain.ProgressPercent(mentality, MatchTempo.Normal, 0));
            TickBallCarrierBrain.ShotEagerness(mentality, factor).Should().Be(TickBallCarrierBrain.ShotEagerness(mentality));
            TickOffBallSupport.RunBudget(mentality, edgeFactor: factor).Should().Be(TickOffBallSupport.RunBudget(mentality));
        }
    }

    [Theory]
    [InlineData(MatchMentality.Positive, 135, 115, 2)]
    [InlineData(MatchMentality.Attacking, 150, 130, 3)]
    public void Positive_and_attacking_keep_their_extras_at_a_hundred_become_balanced_at_zero_and_play_worse_than_balanced_below(MatchMentality mentality, int progress, int eagerness, int runners)
    {
        TickBallCarrierBrain.ProgressPercent(mentality, MatchTempo.Normal, 0, 100).Should().Be(progress);
        TickBallCarrierBrain.ShotEagerness(mentality, 100).Should().Be(eagerness);
        TickOffBallSupport.RunBudget(mentality, edgeFactor: 100).Should().Be(runners);

        TickBallCarrierBrain.ProgressPercent(mentality, MatchTempo.Normal, 0, 0).Should().Be(TickBallCarrierBrain.ProgressPercent(MatchMentality.Balanced, MatchTempo.Normal, 0));
        TickBallCarrierBrain.ShotEagerness(mentality, 0).Should().Be(TickBallCarrierBrain.ShotEagerness(MatchMentality.Balanced));
        TickOffBallSupport.RunBudget(mentality, edgeFactor: 0).Should().Be(TickOffBallSupport.RunBudget(MatchMentality.Balanced));

        var floor = TickAttackEdge.FloorPercent;

        TickBallCarrierBrain.ProgressPercent(mentality, MatchTempo.Normal, 0, floor).Should().BeLessThan(TickBallCarrierBrain.ProgressPercent(MatchMentality.Balanced, MatchTempo.Normal, 0));
        TickBallCarrierBrain.ShotEagerness(mentality, floor).Should().BeLessThan(TickBallCarrierBrain.ShotEagerness(MatchMentality.Balanced));
        TickOffBallSupport.RunBudget(mentality, edgeFactor: floor).Should().BeLessThan(TickOffBallSupport.RunBudget(MatchMentality.Balanced));

        TickBallCarrierBrain.ProgressPercent(mentality, MatchTempo.Normal, 0, TickAttackEdge.CeilingPercent).Should().BeGreaterThan(progress);
        TickBallCarrierBrain.ShotEagerness(mentality, TickAttackEdge.CeilingPercent).Should().BeGreaterThan(eagerness);
    }

    [Fact]
    public void The_edge_of_a_side_is_its_forwards_against_the_other_sides_defenders_and_the_two_sides_mirror_each_other()
    {
        var state = TickTestMatchState.Create(homeAbility: 19, awayAbility: 11);
        var home = new TickTeam(state.Home);
        var away = new TickTeam(state.Away);

        TickAttackEdge.Edge(home, away).Should().BeInRange(780, 820, "eight points of attribute between the sides' players");
        TickAttackEdge.Edge(away, home).Should().BeInRange(-820, -780);

        TickAttackEdge.Refresh(home, away);

        home.AttackEdgeFactorPercent.Should().Be(TickAttackEdge.CeilingPercent);
        away.AttackEdgeFactorPercent.Should().Be(TickAttackEdge.FloorPercent);
    }

    [Fact]
    public void Two_equal_sides_have_no_edge_and_every_side_starts_neutral()
    {
        var state = TickTestMatchState.Create(homeAbility: 16, awayAbility: 16);
        var home = new TickTeam(state.Home);
        var away = new TickTeam(state.Away);

        home.AttackEdgeFactorPercent.Should().Be(TickAttackEdge.NeutralPercent, "a side has no edge until the loop works it out");

        TickAttackEdge.Edge(home, away).Should().BeInRange(-20, 20);

        TickAttackEdge.Refresh(home, away);

        home.AttackEdgeFactorPercent.Should().BeInRange(96, 104);
        away.AttackEdgeFactorPercent.Should().BeInRange(96, 104);
    }
}
