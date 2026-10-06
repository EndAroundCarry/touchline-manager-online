using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Who goes to the ball and where the others stand around it (`replay-v6`): the counter-attack the engine plays,
/// and what the roles leave of the player on the ball's space.
/// </summary>
public sealed class FilmRolesTests
{
    private static FilmBeat Beat(BeatKind kind, int possession, MatchSide side, Vec from, Vec to, HoldKind hold = HoldKind.None, bool counter = false) =>
        new()
        {
            Kind = kind,
            Hold = hold,
            Possession = possession,
            Period = 1,
            Side = side,
            From = from,
            To = to,
            Counter = counter,
        };

    [Fact]
    public void The_first_moves_of_a_counter_the_engine_played_are_a_transition()
    {
        FilmBeat[] beats =
        [
            Beat(BeatKind.Pass, 1, MatchSide.Home, new Vec(35, 34), new Vec(45, 30), counter: true),
            Beat(BeatKind.Carry, 1, MatchSide.Home, new Vec(45, 30), new Vec(66, 30), counter: true),
        ];

        FilmMotion.IsTransition(beats, 0, CounterTuning.Base).Should().BeTrue();
        FilmMotion.IsTransition(beats, 1, CounterTuning.Base).Should().BeTrue();
    }

    [Fact]
    public void The_same_moves_in_a_possession_that_was_not_a_counter_are_not()
    {
        FilmBeat[] beats =
        [
            Beat(BeatKind.Pass, 1, MatchSide.Home, new Vec(35, 34), new Vec(45, 30)),
            Beat(BeatKind.Carry, 1, MatchSide.Home, new Vec(45, 30), new Vec(66, 30)),
        ];

        FilmMotion.IsTransition(beats, 1, CounterTuning.On).Should().BeFalse("only the possessions the engine played as counters are drawn as one");
    }

    [Fact]
    public void A_counter_that_has_reached_the_box_or_gone_on_too_long_has_settled()
    {
        var beats = new List<FilmBeat>();

        for (var step = 0; step < 8; step++)
        {
            beats.Add(Beat(BeatKind.Pass, 1, MatchSide.Home, new Vec(35 + (step * 3.5), 34), new Vec(38.5 + (step * 3.5), 34), counter: true));
        }

        beats.Add(Beat(BeatKind.Carry, 1, MatchSide.Home, new Vec(60, 34), new Vec(90, 34), counter: true));

        FilmMotion.IsTransition(beats, 7, CounterTuning.Base).Should().BeFalse("the small counter holds for three beats");
        FilmMotion.IsTransition(beats, 7, CounterTuning.On).Should().BeFalse("the big one holds for six");
        FilmMotion.IsTransition(beats, 8, CounterTuning.On).Should().BeFalse("a ball in the box is the attack, not the break");
    }

    [Fact]
    public void A_side_that_plays_on_the_counter_draws_a_bigger_one()
    {
        var off = CounterTuning.For(new MatchInstructionsV1());
        var on = CounterTuning.For(new MatchInstructionsV1 { CounterAttack = true });

        off.Should().BeSameAs(CounterTuning.Base);
        on.Should().BeSameAs(CounterTuning.On);

        on.BreakBeats.Should().BeGreaterThan(off.BreakBeats);
        on.OutletAhead.Should().BeGreaterThan(off.OutletAhead);
        on.RunnerAhead.Should().BeGreaterThan(off.RunnerAhead);
        on.Outlets.Should().BeGreaterThan(off.Outlets);
        on.DefendersDrop.Should().BeTrue();
        off.DefendersDrop.Should().BeFalse();
    }

    [Fact]
    public void The_player_on_the_ball_is_not_swarmed_in_open_play()
    {
        for (var seed = 1UL; seed <= 4; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            shape.NearBallP50.Should().BeLessThanOrEqualTo(3, "one challenger and a team-mate or two, not a ring of players");
            shape.NearBallP95.Should().BeLessThanOrEqualTo(6);
            shape.NeighbourSpacingP5.Should().BeGreaterThanOrEqualTo(1.5, "the roles do not put two team-mates on one spot");
        }
    }
}
