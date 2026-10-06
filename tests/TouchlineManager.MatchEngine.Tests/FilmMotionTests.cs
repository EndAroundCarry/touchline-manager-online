using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The constant-pace film's motion (`replay-v4`): what the ball and the players do, measured on the simulated
/// record itself rather than on the compressed tracks a payload carries, so the guarantees are the film's and not
/// an artefact of how it was sampled.
/// </summary>
public sealed class FilmMotionTests
{
    private const int Seeds = 16;

    private const int FilmRosterSize = 22;

    private static readonly HighlightOptionsV1 Defaults = new();

    [Fact]
    public void The_ball_never_jumps_outside_a_cut()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, build) = TestMatchFactory.Analyse(TestMatchFactory.Even(seed));

            build.Diagnostics!.Teleports.Should().Be(0, "a strike is the fastest the ball ever moves, and only a cut jumps");
        }
    }

    [Fact]
    public void Players_never_exceed_their_speed_caps_times_the_pace()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, build) = TestMatchFactory.Analyse(TestMatchFactory.Even(seed));
            var film = build.Diagnostics!;

            film.MaxPlayerFilmSpeed.Should().BeLessThanOrEqualTo(Defaults.SprintMetresPerSecond * film.Pace * 1.001);
            film.MaxKeeperFilmSpeed.Should().BeLessThanOrEqualTo(Defaults.DiveMetresPerSecond * film.Pace * 1.001);
        }
    }

    [Fact]
    public void The_receiver_of_a_pass_is_at_the_ball_when_it_arrives()
    {
        int receptions = 0, atTheBall = 0;
        var worst = 0.0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var film = TestMatchFactory.Analyse(TestMatchFactory.Even(seed)).Build.Diagnostics!;

            receptions += film.Receptions;
            atTheBall += film.ReceiversAtBall;
            worst = Math.Max(worst, film.WorstReceiverGap);
        }

        receptions.Should().BeGreaterThan(1_000);
        ((double)atTheBall / receptions).Should().BeGreaterThanOrEqualTo(0.99, "a pass is received by the player it is played to");
        worst.Should().BeLessThanOrEqualTo(2.5);
    }

    [Fact]
    public void A_player_driving_the_ball_has_it_at_his_feet()
    {
        int carries = 0, withBall = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var film = TestMatchFactory.Analyse(TestMatchFactory.Even(seed)).Build.Diagnostics!;

            carries += film.CarryBeats;
            withBall += film.CarriesWithBall;
        }

        carries.Should().BeGreaterThan(500);
        ((double)withBall / carries).Should().BeGreaterThanOrEqualTo(0.98, "the ball is within a metre and a half of the player driving it");
    }

    [Fact]
    public void The_goalkeeper_is_at_the_ball_when_he_saves_it()
    {
        var saves = 0;
        var worst = 0.0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var film = TestMatchFactory.Analyse(TestMatchFactory.Even(seed)).Build.Diagnostics!;

            saves += film.Saves;
            worst = Math.Max(worst, film.WorstKeeperGap);
        }

        saves.Should().BeGreaterThan(20);
        worst.Should().BeLessThanOrEqualTo(1.0, "the keeper dives to the ball, and the shot ends where he is");
    }

    [Fact]
    public void A_goal_ends_inside_the_goal_mouth_on_the_line()
    {
        var goals = 0;

        for (var seed = 1UL; seed <= Seeds * 2; seed++)
        {
            var (result, build) = TestMatchFactory.Analyse(TestMatchFactory.Even(seed));
            var film = build.Diagnostics!;

            film.GoalStrikes.Should().Be(result.Events.Count(matchEvent => matchEvent.IsGoal), "every goal is a strike the ball is seen to make");
            film.GoalsInNet.Should().Be(film.GoalStrikes, "a goal ends inside the goal mouth");
            goals += film.GoalStrikes;
        }

        goals.Should().BeGreaterThan(20);
    }

    [Fact]
    public void The_ball_is_not_left_standing_outside_the_holds()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var film = TestMatchFactory.Analyse(TestMatchFactory.Even(seed)).Build.Diagnostics!;

            film.StillBallShare.Should().BeLessThanOrEqualTo(0.05, "the film is the ball moving, with the holds the only still moments");
        }
    }

    [Fact]
    public void Holds_keep_their_fixed_film_length_and_the_half_time_card_is_three_seconds()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, build) = TestMatchFactory.Analyse(TestMatchFactory.Even(seed));
            var card = build.Presentation.Passages.Single(passage => passage.OutcomeCode == "half_time");

            card.DurationMilliseconds.Should().BeCloseTo(
                (int)Math.Round(Defaults.HalfTimeHoldSeconds * build.Diagnostics!.HoldScale * 1000),
                50);
        }
    }

    [Fact]
    public void A_pace_that_has_to_rise_condenses_quiet_play_first()
    {
        // A band that nothing fits in forces every quiet possession to be condensed before the pace rises.
        var squeezed = new HighlightOptionsV1 { CondensePaceMilli = 1_900 };
        var condensed = 0;

        for (var seed = 1UL; seed <= 8; seed++)
        {
            var normal = TestMatchFactory.Analyse(TestMatchFactory.Even(seed)).Build.Diagnostics!;
            var tight = TestMatchFactory.Analyse(TestMatchFactory.Even(seed), squeezed).Build.Diagnostics!;

            tight.CondensedPossessions.Should().BeGreaterThanOrEqualTo(normal.CondensedPossessions);
            tight.Pace.Should().BeLessThanOrEqualTo(normal.Pace * 1.01, "condensing quiet play slows the pace down, give or take where the settling stops");
            condensed += tight.CondensedPossessions;
        }

        condensed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_shape_is_measured_in_open_play_on_the_boards_own_formation()
    {
        for (var seed = 1UL; seed <= 4; seed++)
        {
            var shape = TestMatchFactory.Analyse(TestMatchFactory.OnTheBoard(TestMatchFactory.Even(seed))).Build.Diagnostics!.Shape;

            shape.Samples.Should().BeGreaterThan(10_000, "most of the film is open play, sampled at every step");
            shape.NearBallP50.Should().BeInRange(1, FilmRosterSize);
            shape.NearBallP95.Should().BeGreaterThanOrEqualTo(shape.NearBallP50);
            shape.NeighbourSpacingP5.Should().BeGreaterThan(0);
            shape.BackLineDepth.Should().BeLessThan(shape.FrontLineDepth, "the back line stands behind the front line on the board");
            shape.OutOfPossessionDepth.Should().BeGreaterThan(0);
            shape.InPossessionWidth.Should().BeGreaterThan(0);
            shape.BoxSamples.Should().BeGreaterThan(0, "the ball is in a box at some point of a match");
            shape.InBoxP95.Should().BeGreaterThanOrEqualTo(shape.InBoxP50);
            shape.SixYardP95.Should().BeLessThanOrEqualTo(shape.InBoxP95 + 0.5, "the six-yard box is inside the eighteen-yard box");
        }
    }

    [Fact]
    public void A_film_is_the_same_every_time_it_is_built()
    {
        var input = TestMatchFactory.Even(7);

        var first = TestMatchFactory.Analyse(input).Build.Diagnostics!;
        var second = TestMatchFactory.Analyse(input).Build.Diagnostics!;

        second.Should().BeEquivalentTo(first);
    }
}
