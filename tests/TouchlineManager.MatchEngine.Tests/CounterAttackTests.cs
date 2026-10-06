using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using Xunit.Abstractions;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The counter-attack (`engine-v11`): how often a regained ball becomes one, who gets one, and how it fares against a
/// side that has committed forward and against one that sits deep.
/// </summary>
public sealed class CounterAttackTests(ITestOutputHelper output)
{
    private const int Matches = 300;

    private const int DirectionMatches = 800;

    private static readonly MatchInstructionsV1 Attacking = new()
    {
        Mentality = MatchMentality.Attacking,
        DefensiveLine = MatchDefensiveLine.High,
    };

    private static readonly MatchInstructionsV1 Defensive = new()
    {
        Mentality = MatchMentality.Defensive,
        DefensiveLine = MatchDefensiveLine.Deep,
    };

    /// <summary>What a spread of matches produced for the home side's counters.</summary>
    private sealed record Tally(int Regained, int Counters, int CountersElsewhere, double Goals, double AwayGoals);

    private static Tally Play(MatchInstructionsV1 home, MatchInstructionsV1 away, int matches = Matches)
    {
        int regained = 0, counters = 0, elsewhere = 0;
        double goals = 0, awayGoals = 0;

        for (var seed = 1UL; seed <= (ulong)matches; seed++)
        {
            var input = TestMatchFactory.WithInstructions(home, away, seed);
            var recorder = new MatchPassageRecorder();
            var result = MatchSimulator.Simulate(input, TestMatchFactory.Rules, null, recorder);
            MatchPassageV1? previous = null;

            foreach (var passage in recorder.Passages)
            {
                var fromPlay = passage.Restart == PassageRestartKind.None;
                var regainedBall = fromPlay && previous is not null && previous.Side != passage.Side;

                if (passage.Side == MatchSide.Home && regainedBall)
                {
                    regained++;
                    counters += passage.Counter ? 1 : 0;
                }

                // Nothing but a regained ball is ever a counter.
                elsewhere += passage.Counter && !regainedBall ? 1 : 0;
                previous = passage;
            }

            goals += result.HomeGoals;
            awayGoals += result.AwayGoals;
        }

        return new Tally(regained, counters, elsewhere, goals / matches, awayGoals / matches);
    }

    [Fact]
    public void Only_a_ball_regained_from_play_is_ever_a_counter()
    {
        var tally = Play(new MatchInstructionsV1 { CounterAttack = true }, new MatchInstructionsV1 { CounterAttack = true });

        tally.CountersElsewhere.Should().Be(0, "a kick-off, a free kick or a goal kick is not a ball won back");
    }

    [Fact]
    public void A_side_not_asked_to_counter_breaks_about_one_time_in_five()
    {
        var tally = Play(new MatchInstructionsV1(), new MatchInstructionsV1());
        var share = (double)tally.Counters / tally.Regained;

        output.WriteLine($"off: {tally.Counters}/{tally.Regained} = {share:P1}");
        share.Should().BeInRange(0.17, 0.23);
    }

    [Fact]
    public void A_side_asked_to_counter_breaks_about_one_time_in_two()
    {
        var tally = Play(new MatchInstructionsV1 { CounterAttack = true }, new MatchInstructionsV1());
        var share = (double)tally.Counters / tally.Regained;

        output.WriteLine($"on: {tally.Counters}/{tally.Regained} = {share:P1}");
        share.Should().BeInRange(0.46, 0.54);
    }

    [Fact]
    public void The_roll_moves_no_play_draw_so_a_side_with_nothing_to_exploit_plays_the_same_match()
    {
        // Against a balanced opponent a counter is worth the rules' base only, so the instruction does not turn a
        // match into a different one at random: the scoreline distribution stays close.
        var off = Play(new MatchInstructionsV1(), new MatchInstructionsV1());
        var on = Play(new MatchInstructionsV1 { CounterAttack = true }, new MatchInstructionsV1());

        output.WriteLine($"balanced: off {off.Goals:F2}-{off.AwayGoals:F2}  on {on.Goals:F2}-{on.AwayGoals:F2}");
        Math.Abs(on.Goals - off.Goals).Should().BeLessThan(0.5);
    }

    [Fact]
    public void The_counter_works_against_a_side_that_has_committed_forward_and_backfires_against_one_that_sits_deep()
    {
        var offVsAttacking = Play(new MatchInstructionsV1(), Attacking, DirectionMatches);
        var onVsAttacking = Play(new MatchInstructionsV1 { CounterAttack = true }, Attacking, DirectionMatches);
        var offVsDefensive = Play(new MatchInstructionsV1(), Defensive, DirectionMatches);
        var onVsDefensive = Play(new MatchInstructionsV1 { CounterAttack = true }, Defensive, DirectionMatches);

        output.WriteLine($"vs attacking: off {offVsAttacking.Goals:F2}  on {onVsAttacking.Goals:F2}");
        output.WriteLine($"vs defensive: off {offVsDefensive.Goals:F2}  on {onVsDefensive.Goals:F2}");

        (onVsAttacking.Goals - offVsAttacking.Goals).Should().BeGreaterThan(0, "the space behind an attacking side is there to be run into");
        (onVsDefensive.Goals - offVsDefensive.Goals).Should().BeLessThan(0, "a deep side cuts the long ball out");
    }

    [Fact]
    public void The_edge_of_a_counter_rises_with_how_far_the_opponent_has_committed()
    {
        var rules = TestMatchFactory.Rules;
        var deep = PossessionSimulatorEdge(Defensive);
        var balanced = PossessionSimulatorEdge(new MatchInstructionsV1());
        var committed = PossessionSimulatorEdge(Attacking);

        balanced.Progress.Should().Be(rules.CounterProgressBasisPoints);
        balanced.Creation.Should().Be(rules.CounterCreationBasisPoints);
        deep.Progress.Should().BeLessThan(0, "a deep side is well placed to cut the long ball out");
        deep.Creation.Should().BeLessThan(0);
        committed.Progress.Should().BeGreaterThan(balanced.Progress);
        committed.Creation.Should().BeGreaterThan(balanced.Creation);
    }

    [Fact]
    public void Playing_on_the_counter_costs_patience_in_possession_and_shape_in_defence()
    {
        var rules = TestMatchFactory.Rules;
        var off = new MatchInstructionsV1();
        var on = new MatchInstructionsV1 { CounterAttack = true };

        TacticalModifiers.For(MatchUnit.BuildUp, on, rules).Should().Be(TacticalModifiers.For(MatchUnit.BuildUp, off, rules) - rules.CounterAttackBuildUpCostBasisPoints);
        TacticalModifiers.For(MatchUnit.DefensiveShape, on, rules).Should().Be(TacticalModifiers.For(MatchUnit.DefensiveShape, off, rules) - rules.CounterAttackShapeCostBasisPoints);
        TacticalModifiers.For(MatchUnit.Creation, on, rules).Should().Be(TacticalModifiers.For(MatchUnit.Creation, off, rules));
    }

    private static (int Progress, int Creation) PossessionSimulatorEdge(MatchInstructionsV1 opponent) =>
        Simulation.PossessionSimulator.CounterEdge(TestMatchFactory.Rules, opponent);
}
