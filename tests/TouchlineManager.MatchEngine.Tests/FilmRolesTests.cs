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
    public void The_run_up_the_flight_and_the_finish_of_a_cross_are_all_part_of_the_delivery()
    {
        FilmBeat[] beats =
        [
            Beat(BeatKind.Carry, 1, MatchSide.Home, new Vec(70, 60), new Vec(88, 62)),
            Beat(BeatKind.Cross, 1, MatchSide.Home, new Vec(88, 62), new Vec(99, 34)),
            Beat(BeatKind.Header, 1, MatchSide.Home, new Vec(99, 34), new Vec(99, 34)),
            Beat(BeatKind.Shot, 1, MatchSide.Home, new Vec(99, 34), new Vec(105, 36)),
            Beat(BeatKind.Pass, 2, MatchSide.Away, new Vec(20, 30), new Vec(30, 30)),
        ];

        FilmMotion.CrossOf(beats, 0).Should().Be(1, "the beat before a cross is its run-up");
        FilmMotion.CrossOf(beats, 1).Should().Be(1);
        FilmMotion.CrossOf(beats, 2).Should().Be(1, "the box stays set while the cross is headed");
        FilmMotion.CrossOf(beats, 3).Should().Be(1, "and while the header is shot at goal");
        FilmMotion.CrossOf(beats, 4).Should().Be(-1, "another possession is open play again");
    }

    [Fact]
    public void A_cross_from_a_corner_or_a_free_kick_is_not_a_delivery_in_open_play()
    {
        var corner = Beat(BeatKind.Cross, 1, MatchSide.Home, new Vec(105, 0), new Vec(99, 34));

        corner.Formation = FormationMode.Corner;

        FilmBeat[] beats =
        [
            corner,
            Beat(BeatKind.Header, 1, MatchSide.Home, new Vec(99, 34), new Vec(99, 34)),
        ];

        FilmMotion.CrossOf(beats, 0).Should().Be(-1, "a corner arranges its own box");
        FilmMotion.CrossOf(beats, 1).Should().Be(-1);
    }

    [Fact]
    public void The_box_is_set_the_same_way_from_either_flank_and_for_either_side()
    {
        var high = FilmShape.SetBox(MatchSide.Home, 1.0);
        var low = FilmShape.SetBox(MatchSide.Home, -1.0);
        var away = FilmShape.SetBox(MatchSide.Away, 1.0);

        low.NearPost.X.Should().BeApproximately(high.NearPost.X, 1e-9);
        low.NearPost.Y.Should().BeApproximately(FilmSpace.Width - high.NearPost.Y, 1e-9, "the other flank is the mirror image across the pitch");
        low.FarPost.Y.Should().BeApproximately(FilmSpace.Width - high.FarPost.Y, 1e-9);
        away.NearPost.X.Should().BeApproximately(FilmSpace.Length - high.NearPost.X, 1e-9, "the away side attacks the other goal");

        high.NearPost.Y.Should().BeGreaterThan(FilmSpace.Width / 2, "the near-post runner is on the side the ball comes from");
        high.FarPost.Y.Should().BeLessThan(FilmSpace.Width / 2, "the far-post runner is on the other");
        FilmSpace.Attacking(high.NearMark, MatchSide.Home).Should().BeGreaterThan(FilmSpace.Attacking(high.NearPost, MatchSide.Home), "a marker stands goal-side of the runner");
        high.NearMark.DistanceTo(high.SixYardZone).Should().BeGreaterThan(2.5, "two defenders are not set on one spot");
    }

    [Fact]
    public void A_cross_arrives_into_a_box_with_runners_and_markers_in_it()
    {
        int matches = 0;
        double attackers = 0, defenders = 0, sixYard = 0;

        for (var seed = 1UL; seed <= 16; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            if (shape.Deliveries == 0)
            {
                continue;
            }

            matches++;
            attackers += shape.DeliveryAttackersP50;
            defenders += shape.DeliveryDefendersP50;
            sixYard = Math.Max(sixYard, shape.DeliverySixYardP95);
        }

        matches.Should().BeGreaterThan(8, "most matches have a cross in open play");
        (attackers / matches).Should().BeGreaterThanOrEqualTo(3, "a near-post, a far-post and a penalty-spot runner");
        (defenders / matches).Should().BeGreaterThanOrEqualTo(4, "a marker for each of them and a zone or two");
        sixYard.Should().BeLessThanOrEqualTo(7, "a crowded six-yard box is a handful of players, not a queue");
    }

    [Fact]
    public void The_player_on_the_ball_is_not_swarmed_in_open_play()
    {
        double crowded = 0, clustered = 0, p95 = 0;

        for (var seed = 1UL; seed <= 4; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            shape.NearBallP50.Should().BeLessThanOrEqualTo(3, "one challenger and a team-mate or two, not a ring of players");
            shape.NearBallP95.Should().BeLessThanOrEqualTo(5, "a match is never a ring of players");
            shape.NeighbourSpacingP5.Should().BeGreaterThanOrEqualTo(1.5, "the roles do not put two team-mates on one spot");

            p95 += shape.NearBallP95;
            crowded += shape.CrowdedShare;
            clustered += shape.ClusteredShare;
        }

        (p95 / 4).Should().BeLessThan(4.5, "the players the beat is about, one challenger and nobody else; it was 5 before the zone");
        (crowded / 4).Should().BeLessThan(0.055, "five players within five metres of the ball was 6.4% of open play before the zone around it");
        (clustered / 4).Should().BeLessThan(0.038, "four players within three metres of the ball was 4.4% of open play before the zone around it");
    }
}
