using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The measures the film is compared with the Football Manager reference clips by (`replay-v12` baseline): how many players
/// are near the ball, how many are marked, how still the far ones stand, where the keepers stand, how long a set piece waits.
/// These pin that each measure is there, sane and deterministic. They deliberately do not pin the values: the milestones that
/// follow change them on purpose, and the benchmark prints them next to the reference.
/// </summary>
public sealed class FilmReferenceMeasureTests
{
    private const int Seeds = 8;

    [Fact]
    public void Every_measure_is_taken_and_inside_what_a_pitch_allows()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var shape = ShapeOf(seed);

            shape.Samples.Should().BeGreaterThan(1_000, "a match has thousands of steps of open play");

            // Twenty outfielders; the ones near the ball are a subset of the ones nearer still.
            shape.PackWithin10mP50.Should().BeInRange(0, 20);
            shape.PackWithin25mP50.Should().BeInRange(shape.PackWithin10mP50, 20);
            shape.FarPlayerCount.Should().BeInRange(0, 20);
            shape.FarPlayerMotion.Should().BeInRange(0, 12, "no player outruns the sprint cap, even at the film's pace");

            shape.MarkedShare.Should().BeInRange(0, 1);
            shape.CornerMarkedShare.Should().BeInRange(0, 1);
            shape.FreeKickMarkedShare.Should().BeInRange(0, 1);

            foreach (var keeper in new[] { shape.AttackKeeperOffLine, shape.DefendKeeperOffLine })
            {
                foreach (var distance in new[] { keeper.Own, keeper.Middle, keeper.Final })
                {
                    distance.Should().BeInRange(0, 52.5, "a keeper is inside his own half");
                }
            }

            shape.ChasersP50.Should().BeInRange(0, 10);
            shape.ChasersShare.Should().BeInRange(0, 1);
        }
    }

    [Fact]
    public void A_set_piece_is_measured_waiting_and_the_wing_is_measured_being_run()
    {
        int holds = 0, wings = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var shape = ShapeOf(seed);

            holds += shape.SetPieceHolds;
            wings += shape.WingCarrySamples;

            if (shape.SetPieceHolds > 0)
            {
                shape.SetPieceHoldSeconds.Should().BeInRange(0.3, 6.0, "a set piece waits for about a second of film");
                shape.SetPieceSettledShare.Should().BeInRange(0, 1);
                shape.SetPieceSettledMotion.Should().BeInRange(0, 12);
            }
        }

        holds.Should().BeGreaterThan(Seeds * 4, "most matches have corners and free kicks");
        wings.Should().BeGreaterThan(0, "somebody runs a wing in an attack");
    }

    [Theory]
    [InlineData(MatchSide.Home)]
    [InlineData(MatchSide.Away)]
    public void The_keeper_of_the_side_with_the_ball_comes_up_the_pitch_with_it_and_the_other_stays_home(MatchSide side)
    {
        double Off(double depth, bool inPossession)
        {
            var goal = FilmSpace.OwnGoal(side);
            var ball = new Vec(goal.X + (FilmSpace.Direction(side) * depth), FilmSpace.Width / 2);

            return Math.Abs(FilmShape.KeeperOn(side, ball, inPossession).X - goal.X);
        }

        Off(20, inPossession: true).Should().BeLessThan(4.0, "he stays near his line while his side builds from the back");
        Off(52.5, inPossession: true).Should().BeInRange(12.0, 14.0, "the reference has him twelve to fourteen metres out with the ball at halfway");
        Off(85, inPossession: true).Should().BeInRange(15.0, 30.0, "and fifteen to thirty with the ball inside twenty-five metres of goal");

        var previous = 0.0;

        for (var depth = 5.0; depth <= 100.0; depth += 5.0)
        {
            var off = Off(depth, inPossession: true);

            off.Should().BeGreaterThanOrEqualTo(previous - 1e-9, "the further up the ball, the further up he stands");
            previous = off;
        }

        for (var depth = 5.0; depth <= 100.0; depth += 5.0)
        {
            Off(depth, inPossession: false).Should().BeInRange(1.5, 2.5, "the keeper of the side without the ball stays about two metres off his line");
        }
    }

    [Fact]
    public void The_keepers_stand_by_the_reference_in_the_film_the_attacker_up_the_pitch_and_the_defender_home()
    {
        double own = 0, middle = 0, final = 0, defendMiddle = 0, defendFinal = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var shape = ShapeOf(seed);

            own += shape.AttackKeeperOffLine.Own;
            middle += shape.AttackKeeperOffLine.Middle;
            final += shape.AttackKeeperOffLine.Final;
            defendMiddle += shape.DefendKeeperOffLine.Middle;
            defendFinal += shape.DefendKeeperOffLine.Final;
        }

        (own / Seeds).Should().BeLessThan(6.0, "while his side builds he stays near his line (reference about 3 m)");
        (middle / Seeds).Should().BeGreaterThan(own / Seeds + 2.0, "he is further out when the ball is in the middle third");
        (final / Seeds).Should().BeGreaterThan(middle / Seeds + 3.0, "and further still with the ball near goal (reference 15 to 30 m)");
        (final / Seeds).Should().BeGreaterThanOrEqualTo(13.0);
        (defendMiddle / Seeds).Should().BeLessThan(6.0, "the keeper of the side without the ball is near his line (reference about 2 m)");
        (defendFinal / Seeds).Should().BeLessThan(4.0);
    }

    [Fact]
    public void The_measures_are_the_same_every_time_the_film_is_built()
    {
        var first = ShapeOf(3);
        var second = ShapeOf(3);

        second.Should().BeEquivalentTo(first, "the film is derived from the record and has no randomness of its own");
    }

    private static ShapeMetrics ShapeOf(ulong seed) =>
        TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;
}
