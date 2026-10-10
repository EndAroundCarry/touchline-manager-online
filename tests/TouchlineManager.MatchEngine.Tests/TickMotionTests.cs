using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies that the tick engine's men keep moving the way footballers do (`tick-film-v1`, Milestone 5): a man in position walks about
/// instead of standing, eases onto his place, keeps clear of an opponent he is not in a duel with, and the block follows the ball's
/// destination and bends between its two shapes; then that a whole recorded match has few men standing, no stops all at once and no
/// opponents sitting on each other.
/// </summary>
public sealed class TickMotionTests
{
    private const int Half = TickMatchRecording.Entities / 2;

    private static TickPlayerProfile Profile() =>
        TickPlayerProfile.From(PlayerAttributesV1.From(Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray()));

    [Fact]
    public void A_man_in_position_with_a_square_to_walk_to_walks_there_and_never_faster_than_a_walk()
    {
        var profile = Profile();
        var anchor = new SpatialPoint(5_000, 3_500);
        var square = new SpatialPoint(5_190, 3_500);
        var team = new[] { TickPlayerState.Standing(5_000, 3_500, 0, 10_000) };
        var top = TickPlayerPhysics.EffectiveTopSpeed(team[0], profile);
        var fastest = 0;

        for (var tick = 0; tick < 80; tick++)
        {
            var context = new TickSteerContext { HasMicro = true, Micro = square, Engaged = true };
            var intent = TickSteering.Steer(0, team, profile, anchor, 0, context);

            TickPlayerPhysics.Step(ref team[0], profile, intent);
            fastest = Math.Max(fastest, team[0].Speed);
        }

        fastest.Should().BeGreaterThan(0, "he does walk");
        fastest.Should().BeLessThanOrEqualTo(top * TickPlayerPhysics.WalkCeilingBasisPoints / 10_000, "in the walk band, where he recovers energy");
        Math.Abs(TickSpatialUnits.ToUnits(team[0].X) - square.X).Should().BeLessThan(60, "he is there within about half a metre");
    }

    [Fact]
    public void A_man_in_position_with_no_square_stands_still()
    {
        var profile = Profile();
        var anchor = new SpatialPoint(5_000, 3_500);
        var team = new[] { TickPlayerState.Standing(5_000, 3_500, 0, 10_000) };

        for (var tick = 0; tick < 40; tick++)
        {
            TickPlayerPhysics.Step(ref team[0], profile, TickSteering.Steer(0, team, profile, anchor));
        }

        team[0].Speed.Should().Be(0);
    }

    [Fact]
    public void A_man_far_from_his_place_goes_back_to_it_and_does_not_walk_about_his_square()
    {
        var profile = Profile();
        var anchor = new SpatialPoint(5_000, 3_500);
        var square = new SpatialPoint(3_000, 3_500);
        var team = new[] { TickPlayerState.Standing(6_500, 3_500, TickTrigonometry.HalfTurn, 10_000) };
        var start = Math.Abs(TickSpatialUnits.ToUnits(team[0].X) - anchor.X);

        for (var tick = 0; tick < 30; tick++)
        {
            var context = new TickSteerContext { HasMicro = true, Micro = square, Engaged = true };

            TickPlayerPhysics.Step(ref team[0], profile, TickSteering.Steer(0, team, profile, anchor, 0, context));
        }

        Math.Abs(TickSpatialUnits.ToUnits(team[0].X) - anchor.X).Should().BeLessThan(start - 300, "he is going to his place, 15 m away, not to a square beyond it");
    }

    [Fact]
    public void A_man_holding_his_place_eases_onto_it_and_a_man_sent_at_a_pace_brakes_late()
    {
        var profile = Profile();
        var anchor = new SpatialPoint(5_000, 3_500);

        int SpeedTwoMetresOut(int pace)
        {
            var team = new[] { TickPlayerState.Standing(3_000, 3_500, 0, 10_000) };

            for (var tick = 0; tick < 200; tick++)
            {
                TickPlayerPhysics.Step(ref team[0], profile, TickSteering.Steer(0, team, profile, anchor, pace));

                if (Math.Abs(TickSpatialUnits.ToUnits(team[0].X) - anchor.X) <= 200)
                {
                    return team[0].Speed;
                }
            }

            return int.MaxValue;
        }

        SpeedTwoMetresOut(0).Should().BeLessThan(SpeedTwoMetresOut(TickSteering.NearSpeedBasisPoints), "the same cap, but he starts slowing sooner");
    }

    [Fact]
    public void An_opponent_who_is_not_in_a_duel_is_pushed_clear_and_one_who_is_engaged_is_not()
    {
        var profile = Profile();
        var anchor = new SpatialPoint(5_000, 3_500);
        var team = new[] { TickPlayerState.Standing(5_000, 3_500, 0, 10_000) };
        var near = new[] { TickPlayerState.Standing(5_100, 3_500, 0, 10_000) };
        var far = new[] { TickPlayerState.Standing(5_400, 3_500, 0, 10_000) };

        var pushed = TickSteering.Steer(0, team, profile, anchor, 0, new TickSteerContext { Opponents = near, Engaged = false });
        var engaged = TickSteering.Steer(0, team, profile, anchor, 0, new TickSteerContext { Opponents = near, Engaged = true });
        var apart = TickSteering.Steer(0, team, profile, anchor, 0, new TickSteerContext { Opponents = far, Engaged = false });

        pushed.SpeedLimitBasisPoints.Should().BeGreaterThan(0);
        pushed.TargetXUnits.Should().BeLessThan(anchor.X, "away from the man a metre in front of him");
        engaged.SpeedLimitBasisPoints.Should().Be(0, "a man in a duel goes at the opponent");
        apart.SpeedLimitBasisPoints.Should().Be(0, "an opponent four metres off is no concern");
    }

    [Fact]
    public void Two_opponents_standing_exactly_on_each_other_are_pushed_apart_the_same_way_every_time()
    {
        var profile = Profile();
        var anchor = new SpatialPoint(5_000, 3_500);
        var bodies = new[] { TickPlayerState.Standing(5_000, 3_500, 0, 10_000) };

        var first = TickSteering.Steer(0, bodies, profile, anchor, 0, new TickSteerContext { Opponents = bodies, Engaged = false });
        var second = TickSteering.Steer(0, bodies, profile, anchor, 0, new TickSteerContext { Opponents = bodies, Engaged = false });

        first.Should().Be(second);
        first.SpeedLimitBasisPoints.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_shape_is_part_way_between_the_shape_with_the_ball_and_the_one_without_it()
    {
        var style = TickTeamStyle.From(new MatchInstructionsV1());
        var winger = new TickAnchorSpec(5_000, 900, MatchPositionFamily.Midfield);

        var with = TickTacticalGeometry.Resolve(winger, style, true, hasPossession: true, 6_000, 2_000);
        var without = TickTacticalGeometry.Resolve(winger, style, true, hasPossession: false, 6_000, 2_000);

        TickTacticalGeometry.Resolve(winger, style, true, 10_000, 6_000, 2_000).Should().Be(with);
        TickTacticalGeometry.Resolve(winger, style, true, 0, 6_000, 2_000).Should().Be(without);

        var middle = TickTacticalGeometry.Resolve(winger, style, true, 5_000, 6_000, 2_000);

        with.Should().NotBe(without, "the two shapes differ");
        middle.X.Should().BeInRange(Math.Min(with.X, without.X), Math.Max(with.X, without.X));
        middle.Y.Should().BeInRange(Math.Min(with.Y, without.Y), Math.Max(with.Y, without.Y));
        Math.Abs(middle.X - ((with.X + without.X) / 2)).Should().BeLessThanOrEqualTo(1);
        Math.Abs(middle.Y - ((with.Y + without.Y) / 2)).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void A_point_a_man_walks_to_is_kept_onside()
    {
        var defenders = new[]
        {
            TickPlayerState.Standing(9_500, 3_500, TickTrigonometry.HalfTurn, 10_000),
            TickPlayerState.Standing(8_200, 3_000, TickTrigonometry.HalfTurn, 10_000),
            TickPlayerState.Standing(8_200, 4_000, TickTrigonometry.HalfTurn, 10_000),
        };

        var beyond = TickOffBallSupport.ClampOnside(new SpatialPoint(9_000, 3_500), true, 6_000, defenders);
        var level = TickOffBallSupport.ClampOnside(new SpatialPoint(7_000, 3_500), true, 6_000, defenders);

        beyond.X.Should().BeLessThan(8_200, "not past the second-last defender");
        level.Should().Be(new SpatialPoint(7_000, 3_500), "a point short of the line is not moved");

        var away = TickOffBallSupport.ClampOnside(new SpatialPoint(1_000, 3_500), false, 4_000, [.. defenders.Select(Mirrored)]);

        away.X.Should().BeGreaterThan(1_800, "the away side attacks towards X = 0");
    }

    [Fact]
    public void In_a_whole_match_few_men_stand_no_dozen_stop_together_and_no_two_opponents_sit_on_each_other()
    {
        var recording = TickPlay.Even.Recording;
        var open = 0;
        var frames = 0;
        var standing = 0;
        var synchronised = 0;
        var sitting = 0;

        for (var frame = 1; frame < recording.FrameCount; frame++)
        {
            if (recording.State(frame) != TickPlayState.OpenPlay
                || recording.State(frame - 1) != TickPlayState.OpenPlay
                || recording.Flags(frame) != TickFrameFlags.None)
            {
                continue;
            }

            open++;

            var stopped = 0;

            for (var entity = 0; entity < TickMatchRecording.Entities; entity++)
            {
                if (entity % Half == 0 || recording.PlayerX(frame, entity) < 0 || recording.PlayerX(frame - 1, entity) < 0)
                {
                    continue;
                }

                var step = Step(recording, frame, entity);

                if (step > 150)
                {
                    continue;
                }

                frames++;
                standing += step < 3 ? 1 : 0;
                stopped += step < 5 ? 1 : 0;
            }

            synchronised += stopped >= 12 ? 1 : 0;
            sitting += SittingOnEachOther(recording, frame) ? 1 : 0;
        }

        open.Should().BeGreaterThan(10_000);
        (standing / (double)frames).Should().BeLessThan(0.10, "under a tenth of the time does an outfielder stand still");
        (synchronised / (double)open).Should().BeLessThan(0.01, "the side does not stop all at once");
        (sitting / (double)open).Should().BeLessThan(0.01, "two opponents do not sit on one spot outside a duel");
    }

    private static TickPlayerState Mirrored(TickPlayerState player) =>
        TickPlayerState.Standing(
            SpatialPitch.PitchLength - TickSpatialUnits.ToUnits(player.X),
            SpatialPitch.PitchWidth - TickSpatialUnits.ToUnits(player.Y),
            0,
            10_000);

    private static int Step(TickMatchRecording recording, int frame, int entity)
    {
        long dx = recording.PlayerX(frame, entity) - recording.PlayerX(frame - 1, entity);
        long dy = recording.PlayerY(frame, entity) - recording.PlayerY(frame - 1, entity);

        return (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Whether some pair of opponents, neither on the ball nor within three metres of it, stand within a metre of each other, both walking or slower.</summary>
    private static bool SittingOnEachOther(TickMatchRecording recording, int frame)
    {
        var controller = recording.Controller(frame);
        long ballX = recording.BallX(frame);
        long ballY = recording.BallY(frame);

        for (var home = 1; home < Half; home++)
        {
            if (recording.PlayerX(frame, home) < 0 || home == controller)
            {
                continue;
            }

            for (var away = Half + 1; away < TickMatchRecording.Entities; away++)
            {
                if (recording.PlayerX(frame, away) < 0 || away == controller)
                {
                    continue;
                }

                long dx = recording.PlayerX(frame, home) - recording.PlayerX(frame, away);
                long dy = recording.PlayerY(frame, home) - recording.PlayerY(frame, away);

                if ((dx * dx) + (dy * dy) >= 100 * 100)
                {
                    continue;
                }

                long bx = recording.PlayerX(frame, home) - ballX;
                long by = recording.PlayerY(frame, home) - ballY;

                if (((bx * bx) + (by * by) < 300 * 300) || Step(recording, frame, home) >= 15 || Step(recording, frame, away) >= 15)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }
}
