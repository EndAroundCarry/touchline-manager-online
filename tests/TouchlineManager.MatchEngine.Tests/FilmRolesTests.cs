using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Who goes to the ball and where the others stand around it (`replay-v6`): the move that is a break or a long ball,
/// and what the roles leave of the player on the ball's space.
/// </summary>
public sealed class FilmRolesTests
{
    private static FilmBeat Beat(BeatKind kind, int possession, MatchSide side, Vec from, Vec to, HoldKind hold = HoldKind.None) =>
        new()
        {
            Kind = kind,
            Hold = hold,
            Possession = possession,
            Period = 1,
            Side = side,
            From = from,
            To = to,
        };

    [Fact]
    public void A_long_pass_forward_is_a_transition()
    {
        FilmBeat[] beats = [Beat(BeatKind.LoftedPass, 0, MatchSide.Home, new Vec(30, 34), new Vec(72, 20))];

        FilmMotion.IsTransition(beats, 0).Should().BeTrue();
    }

    [Fact]
    public void A_short_pass_is_not()
    {
        FilmBeat[] beats = [Beat(BeatKind.Pass, 0, MatchSide.Home, new Vec(30, 34), new Vec(42, 30))];

        FilmMotion.IsTransition(beats, 0).Should().BeFalse();
    }

    [Fact]
    public void A_long_pass_backwards_or_across_is_not()
    {
        FilmBeat[] beats =
        [
            Beat(BeatKind.Pass, 0, MatchSide.Home, new Vec(60, 5), new Vec(60, 55)),
            Beat(BeatKind.LoftedPass, 0, MatchSide.Home, new Vec(70, 34), new Vec(30, 34)),
        ];

        FilmMotion.IsTransition(beats, 0).Should().BeFalse("fifty metres across the pitch is a switch of play, not a ball in behind");
        FilmMotion.IsTransition(beats, 1).Should().BeFalse();
    }

    [Fact]
    public void A_move_that_covers_the_pitch_after_a_turnover_is_a_transition()
    {
        FilmBeat[] beats =
        [
            Beat(BeatKind.Pass, 0, MatchSide.Away, new Vec(60, 30), new Vec(35, 34)),
            Beat(BeatKind.Pass, 1, MatchSide.Home, new Vec(35, 34), new Vec(45, 30)),
            Beat(BeatKind.Carry, 1, MatchSide.Home, new Vec(45, 30), new Vec(66, 30)),
        ];

        FilmMotion.IsTransition(beats, 1).Should().BeFalse("the break has not gone anywhere yet");
        FilmMotion.IsTransition(beats, 2).Should().BeTrue();
    }

    [Fact]
    public void The_same_move_with_the_ball_kept_is_not()
    {
        FilmBeat[] beats =
        [
            Beat(BeatKind.Pass, 0, MatchSide.Home, new Vec(20, 30), new Vec(35, 34)),
            Beat(BeatKind.Pass, 1, MatchSide.Home, new Vec(35, 34), new Vec(45, 30)),
            Beat(BeatKind.Carry, 1, MatchSide.Home, new Vec(45, 30), new Vec(66, 30)),
        ];

        FilmMotion.IsTransition(beats, 2).Should().BeFalse("the side had the ball already: it is build-up, not a break");
    }

    [Theory]
    [InlineData(2)] // HoldKind.GoalKick
    [InlineData(5)] // HoldKind.Corner
    [InlineData(4)] // HoldKind.FreeKick
    [InlineData(1)] // HoldKind.KickOff
    public void A_restart_is_not_a_break(int restart)
    {
        FilmBeat[] beats =
        [
            Beat(BeatKind.Pass, 0, MatchSide.Away, new Vec(60, 30), new Vec(35, 34)),
            Beat(BeatKind.Hold, 1, MatchSide.Home, new Vec(35, 34), new Vec(35, 34), (HoldKind)restart),
            Beat(BeatKind.Pass, 1, MatchSide.Home, new Vec(35, 34), new Vec(45, 30)),
            Beat(BeatKind.Carry, 1, MatchSide.Home, new Vec(45, 30), new Vec(66, 30)),
        ];

        FilmMotion.IsTransition(beats, 3).Should().BeFalse();
    }

    [Fact]
    public void A_move_that_has_taken_too_long_is_not_a_break()
    {
        var beats = new List<FilmBeat> { Beat(BeatKind.Pass, 0, MatchSide.Away, new Vec(60, 30), new Vec(35, 34)) };

        for (var step = 0; step < 8; step++)
        {
            beats.Add(Beat(BeatKind.Pass, 1, MatchSide.Home, new Vec(35 + (step * 3.5), 34), new Vec(38.5 + (step * 3.5), 34)));
        }

        FilmMotion.IsTransition(beats, beats.Count - 1).Should().BeFalse("a possession that has been worked for eight touches has settled");
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
