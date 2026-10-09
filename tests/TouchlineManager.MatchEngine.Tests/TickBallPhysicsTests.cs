using FluentAssertions;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's integer trigonometry, fixed-point units, and ball physics (Milestone 1).
/// </summary>
public sealed class TickBallPhysicsTests
{
    private const int GoalMid = SpatialPitch.GoalYCenter;

    [Fact]
    public void Unit_conversions_put_the_pitch_and_the_crossbar_where_the_plan_says()
    {
        TickSpatialUnits.TicksPerSecond.Should().Be(10);
        TickSpatialUnits.TickDeltaMs.Should().Be(100);
        TickSpatialUnits.ToFixed(10_000).Should().Be(TickSpatialUnits.PitchLengthFixed);

        // 105 m is 10,000 units; 9.81 m/s^2 over one 0.1 s tick is about a tenth of a metre.
        TickSpatialUnits.CentimetresToFixed(10_500).Should().BeInRange(9_999_000, 10_001_000);

        // The 2.44 m crossbar is at Z 35.
        TickSpatialUnits.HeightToZFixed(244).Should().BeInRange(34_000, 36_000);
    }

    [Fact]
    public void Sine_and_cosine_are_exact_at_the_quarter_turns_and_symmetric()
    {
        TickTrigonometry.Sin(0).Should().Be(0);
        TickTrigonometry.Cos(0).Should().Be(10_000);
        TickTrigonometry.Sin(TickTrigonometry.QuarterTurn).Should().Be(10_000);
        TickTrigonometry.Cos(TickTrigonometry.QuarterTurn).Should().Be(0);
        TickTrigonometry.Sin(TickTrigonometry.HalfTurn).Should().Be(0);
        TickTrigonometry.Cos(TickTrigonometry.HalfTurn).Should().Be(-10_000);
        TickTrigonometry.Sin(3 * TickTrigonometry.QuarterTurn).Should().Be(-10_000);

        for (var angle = 0; angle < TickTrigonometry.FullTurn; angle++)
        {
            TickTrigonometry.Sin(-angle).Should().Be(-TickTrigonometry.Sin(angle));
            TickTrigonometry.Cos(-angle).Should().Be(TickTrigonometry.Cos(angle));

            var norm = (TickTrigonometry.Sin(angle) * TickTrigonometry.Sin(angle))
                + (TickTrigonometry.Cos(angle) * TickTrigonometry.Cos(angle));
            norm.Should().BeInRange(99_900_000, 100_100_000, "sin^2 + cos^2 is 1 to within table rounding");
        }
    }

    [Fact]
    public void Angle_of_inverts_the_direction_of_every_heading_to_within_one_unit()
    {
        for (var angle = 0; angle < TickTrigonometry.FullTurn; angle++)
        {
            var found = TickTrigonometry.AngleOf(
                TickTrigonometry.Cos(angle) * 1_000L,
                TickTrigonometry.Sin(angle) * 1_000L);

            Math.Abs(TickTrigonometry.Difference(angle, found)).Should().BeLessThanOrEqualTo(1, $"heading {angle}");
        }

        TickTrigonometry.AngleOf(0, 0).Should().Be(0);
        TickTrigonometry.AngleOf(5, 0).Should().Be(0);
        TickTrigonometry.AngleOf(0, 5).Should().Be(256);
        TickTrigonometry.AngleOf(-5, 0).Should().Be(512);
        TickTrigonometry.AngleOf(0, -5).Should().Be(768);
    }

    [Fact]
    public void Difference_takes_the_short_way_round()
    {
        TickTrigonometry.Difference(10, 20).Should().Be(10);
        TickTrigonometry.Difference(20, 10).Should().Be(-10);
        TickTrigonometry.Difference(1_020, 4).Should().Be(8);
        TickTrigonometry.Difference(4, 1_020).Should().Be(-8);
        TickTrigonometry.Difference(0, 512).Should().Be(512);
    }

    [Fact]
    public void A_thirty_metre_lofted_pass_lands_on_its_target_on_the_tick_it_was_solved_for()
    {
        var ball = new TickBallPhysics();
        ball.PlaceAt(2_000, 3_000);

        // 30 m is about 2,857 pitch units.
        var flightTicks = ball.LaunchLofted(4_857, 3_400, apexZUnits: 25);

        flightTicks.Should().BeInRange(10, 20, "a 25-unit (1.75 m) apex is a 1.2 s flight");
        ball.Mode.Should().Be(TickBallMode.Flight);

        var peak = 0;

        for (var tick = 1; tick < flightTicks; tick++)
        {
            ball.Step().Should().Be(TickBallBoundary.InPlay);
            ball.BounceCount.Should().Be(0, $"still in the air on tick {tick}");
            ball.IsAirborne.Should().BeTrue();
            peak = Math.Max(peak, ball.UnitZ);
        }

        peak.Should().BeInRange(20, 30, "the requested apex is 25");

        ball.Step().Should().Be(TickBallBoundary.InPlay);

        ball.BounceCount.Should().Be(1, "the ball is back on the grass on tick T exactly");
        ball.UnitX.Should().BeInRange(4_855, 4_859);
        ball.UnitY.Should().BeInRange(3_398, 3_402);
    }

    [Theory]
    [InlineData(5_000, 3_500, 6_500, 3_500, 10)]
    [InlineData(1_000, 500, 9_000, 6_500, 40)]
    [InlineData(8_000, 6_000, 2_000, 1_000, 60)]
    [InlineData(3_000, 3_500, 3_100, 3_500, 4)]
    public void Lofted_balls_land_on_target_across_distances_and_apexes(int fromX, int fromY, int toX, int toY, int apex)
    {
        var ball = new TickBallPhysics();
        ball.PlaceAt(fromX, fromY);

        var flightTicks = ball.LaunchLofted(toX, toY, apex);

        for (var tick = 0; tick < flightTicks; tick++)
        {
            ball.Step().Should().Be(TickBallBoundary.InPlay);
        }

        ball.BounceCount.Should().Be(1);
        ball.UnitX.Should().BeInRange(toX - 3, toX + 3);
        ball.UnitY.Should().BeInRange(toY - 3, toY + 3);
    }

    [Fact]
    public void A_ball_bounces_with_less_height_each_time_and_settles_on_the_grass()
    {
        var ball = new TickBallPhysics();
        ball.Drop(5_000, 3_500, zUnits: 60);

        var apexes = new List<int>();
        var rising = false;
        var previous = ball.Z;
        var settledOn = -1;

        for (var tick = 0; tick < 400; tick++)
        {
            ball.Step();

            if (rising && ball.Z < previous)
            {
                apexes.Add(previous);
                rising = false;
            }
            else if (ball.Z > previous)
            {
                rising = true;
            }

            previous = ball.Z;

            if (ball.Mode == TickBallMode.Loose)
            {
                settledOn = tick;
                break;
            }
        }

        settledOn.Should().BeGreaterThan(0, "a dropped ball comes to rest");
        apexes.Should().HaveCountGreaterThan(2);
        apexes.Should().BeInDescendingOrder();
        apexes[0].Should().BeInRange(
            (int)(TickSpatialUnits.ToFixed(60) * 0.35),
            (int)(TickSpatialUnits.ToFixed(60) * 0.50),
            "restitution 0.65 on speed keeps about 42% of the height");
        ball.Z.Should().Be(0);
        ball.BounceCount.Should().BeGreaterThan(2);
    }

    [Fact]
    public void A_rolling_pass_dies_close_to_its_target_after_slowing_every_tick()
    {
        var ball = new TickBallPhysics();
        ball.PlaceAt(1_000, 3_500);

        ball.LaunchRolling(3_857, 3_500);

        var lastSpeed = ball.GroundSpeed;
        var stoppedAt = -1;

        for (var tick = 0; tick < 400; tick++)
        {
            ball.Step().Should().Be(TickBallBoundary.InPlay);
            ball.GroundSpeed.Should().BeLessThanOrEqualTo(lastSpeed, "friction only ever slows the ball");
            lastSpeed = ball.GroundSpeed;

            if (ball.GroundSpeed == 0)
            {
                stoppedAt = tick;
                break;
            }
        }

        stoppedAt.Should().BeGreaterThan(20).And.BeLessThan(400, "the ball rolls to a natural stop");
        ball.Mode.Should().Be(TickBallMode.Loose);
        ball.Z.Should().Be(0);
        ball.UnitX.Should().BeInRange(3_800, 3_860, "it stops a hair short: the last half unit a tick is below the stopping speed");
        ball.UnitY.Should().Be(3_500);
    }

    [Fact]
    public void A_rolling_pass_with_an_arrival_speed_reaches_its_target_still_moving()
    {
        var ball = new TickBallPhysics();
        ball.PlaceAt(2_000, 3_500);
        var arrival = TickSpatialUnits.SpeedToFixedPerTick(500);

        ball.LaunchRolling(3_000, 3_500, arrival);

        var ticks = 0;

        while (ball.UnitX < 3_000 && ticks < 200)
        {
            ball.Step();
            ticks++;
        }

        ticks.Should().BeLessThan(200);
        ball.GroundSpeed.Should().BeInRange((int)(arrival * 0.9), (int)(arrival * 1.15));
    }

    [Fact]
    public void A_dribbled_ball_sits_ahead_of_its_player_and_moves_on_when_released()
    {
        var ball = new TickBallPhysics();
        var playerX = TickSpatialUnits.ToFixed(4_000);
        var playerY = TickSpatialUnits.ToFixed(3_000);
        var speed = TickSpatialUnits.SpeedToFixedPerTick(600);

        ball.Attach(7);
        ball.Mode.Should().Be(TickBallMode.Controlled);
        ball.ControllerIndex.Should().Be(7);

        // Facing +Y (256 binary units).
        ball.Carry(playerX, playerY, TickTrigonometry.QuarterTurn, speed);

        ball.UnitX.Should().Be(4_000);
        ball.UnitY.Should().Be(3_000 + TickBallPhysics.DribbleOffsetUnits);
        ball.Step().Should().Be(TickBallBoundary.InPlay);
        ball.UnitY.Should().Be(3_000 + TickBallPhysics.DribbleOffsetUnits, "a controlled ball moves only with its player");

        ball.Release();
        ball.Mode.Should().Be(TickBallMode.Loose);
        ball.ControllerIndex.Should().Be(-1);
        ball.Step();
        ball.UnitY.Should().BeGreaterThan(3_000 + TickBallPhysics.DribbleOffsetUnits, "it carries on at the player's pace");
    }

    [Fact]
    public void Reception_reaches_further_on_the_grass_than_in_the_air_and_not_at_all_overhead()
    {
        var ball = new TickBallPhysics();
        ball.PlaceAt(5_000, 3_500);
        var x = TickSpatialUnits.ToFixed(5_000);

        ball.IsReceivableFrom(x + TickSpatialUnits.ToFixed(119), TickSpatialUnits.ToFixed(3_500)).Should().BeTrue();
        ball.IsReceivableFrom(x + TickSpatialUnits.ToFixed(121), TickSpatialUnits.ToFixed(3_500)).Should().BeFalse();

        ball.Drop(5_000, 3_500, zUnits: 25);
        ball.IsReceivableFrom(x + TickSpatialUnits.ToFixed(99), TickSpatialUnits.ToFixed(3_500)).Should().BeTrue();
        ball.IsReceivableFrom(x + TickSpatialUnits.ToFixed(110), TickSpatialUnits.ToFixed(3_500)).Should().BeFalse();

        ball.Drop(5_000, 3_500, zUnits: 60);
        ball.IsReceivableFrom(x, TickSpatialUnits.ToFixed(3_500)).Should().BeFalse("it is above any header");

        ball.Attach(3);
        ball.IsReceivableFrom(x, TickSpatialUnits.ToFixed(3_500)).Should().BeFalse("it is already at someone's feet");
    }

    [Fact]
    public void A_shot_into_the_frame_is_a_goal_at_the_end_it_crossed()
    {
        Shoot(fromX: 8_500, fromY: GoalMid, aimY: GoalMid, lift: 4_500)
            .Should().Be(TickBallBoundary.GoalAwayEnd);

        Shoot(fromX: 1_500, fromY: 3_300, aimY: 3_300, lift: 4_500)
            .Should().Be(TickBallBoundary.GoalHomeEnd);
    }

    [Fact]
    public void A_shot_that_clips_the_post_rebounds_into_play()
    {
        // Sweep the sideways speed of a low drive from 8,500 so that one of them meets the near post (Y 3,123).
        var found = false;

        for (var sideways = -22_000; sideways >= -42_000 && !found; sideways -= 250)
        {
            var ball = new TickBallPhysics();
            ball.PlaceAt(8_500, 3_300);
            ball.Kick(260_000, sideways, 4_000);

            var result = Run(ball);

            if (result == TickBallBoundary.HitPost)
            {
                found = true;
                ball.VelocityX.Should().BeLessThan(0, "it comes back out of the goal");
                ball.UnitX.Should().BeLessThan(SpatialPitch.AwayGoalX);
                ball.Mode.Should().NotBe(TickBallMode.Controlled);
                ball.Step().Should().NotBe(TickBallBoundary.HitPost, "it is moving away from the post");
            }
        }

        found.Should().BeTrue("some drive meets the post");
    }

    [Fact]
    public void A_shot_that_meets_the_bar_is_deflected_back_and_down()
    {
        var found = false;

        // 250 units a tick from 1,000 units out is about four ticks of flight; sweep the lift so Z is 35 on arrival.
        for (var lift = 8_000; lift <= 14_000 && !found; lift += 25)
        {
            var ball = new TickBallPhysics();
            ball.PlaceAt(9_000, GoalMid);
            ball.Kick(250_000, 0, lift);

            var result = Run(ball);

            if (result == TickBallBoundary.HitCrossbar)
            {
                found = true;
                ball.VelocityX.Should().BeLessThan(0, "it comes back out of the goal");
                ball.VelocityZ.Should().BeLessThan(0, "it is knocked down");
                ball.Mode.Should().Be(TickBallMode.Bounced);
            }
        }

        found.Should().BeTrue("some shot height meets the bar");
    }

    [Fact]
    public void A_shot_over_the_bar_or_wide_of_the_post_is_out_at_the_goal_line()
    {
        Shoot(fromX: 8_500, fromY: GoalMid, aimY: GoalMid, lift: 14_000)
            .Should().Be(TickBallBoundary.OutGoalLine, "far over the bar");

        Shoot(fromX: 8_500, fromY: 2_000, aimY: 2_000, lift: 4_500)
            .Should().Be(TickBallBoundary.OutGoalLine, "far wide of the near post");
    }

    [Fact]
    public void A_ball_that_leaves_the_side_is_dead_on_the_touchline_at_the_crossing_point()
    {
        var ball = new TickBallPhysics();
        ball.PlaceAt(5_000, 6_950);
        ball.Kick(100_000, 120_000, 0);

        var result = ball.Step();

        result.Should().Be(TickBallBoundary.OutTouchline);
        ball.Y.Should().Be(TickSpatialUnits.PitchWidthFixed);
        ball.X.Should().BeInRange(TickSpatialUnits.ToFixed(5_000), TickSpatialUnits.ToFixed(5_000) + 100_000);
        ball.GroundSpeed.Should().Be(0);
        ball.Mode.Should().Be(TickBallMode.Loose);
    }

    [Fact]
    public void Ball_physics_is_deterministic_and_allocates_nothing_in_the_tick_loop()
    {
        static long Run()
        {
            var ball = new TickBallPhysics();
            long hash = 17;

            for (var pass = 0; pass < 200; pass++)
            {
                ball.PlaceAt(1_000 + ((pass * 37) % 8_000), 500 + ((pass * 53) % 6_000));

                if ((pass & 1) == 0)
                {
                    ball.LaunchLofted(500 + ((pass * 91) % 9_000), 500 + ((pass * 29) % 6_000), 10 + (pass % 40));
                }
                else
                {
                    ball.LaunchRolling(500 + ((pass * 61) % 9_000), 500 + ((pass * 17) % 6_000));
                }

                for (var tick = 0; tick < 270; tick++)
                {
                    var boundary = ball.Step();
                    hash = unchecked((hash * 31) + ball.X);
                    hash = unchecked((hash * 31) + ball.Y);
                    hash = unchecked((hash * 31) + ball.Z);
                    hash = unchecked((hash * 31) + (int)boundary);
                }
            }

            return hash;
        }

        var first = Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);

        // One ball is built per run; nothing else may be allocated per tick (54,000 ticks here).
        allocated.Should().BeLessThan(256);
    }

    /// <summary>Steps a ball until it leaves play or hits the woodwork, giving up after 20 seconds.</summary>
    private static TickBallBoundary Run(TickBallPhysics ball)
    {
        var result = TickBallBoundary.InPlay;

        for (var tick = 0; tick < 200 && result == TickBallBoundary.InPlay; tick++)
        {
            result = ball.Step();
        }

        return result;
    }

    private static TickBallBoundary Shoot(int fromX, int fromY, int aimY, int lift)
    {
        var ball = new TickBallPhysics();
        ball.PlaceAt(fromX, fromY);

        var towardsAway = fromX > 5_000;
        var goalX = towardsAway ? SpatialPitch.AwayGoalX : SpatialPitch.HomeGoalX;
        var dx = TickSpatialUnits.ToFixed(goalX - fromX);
        var dy = TickSpatialUnits.ToFixed(aimY - fromY);

        // 25 m/s is about 2,380 units a second: 238 units, 238,000 fixed, a tick.
        var length = (int)SpatialMath.Sqrt(((long)dx * dx) + ((long)dy * dy));
        const int Speed = 238_000;

        ball.Kick((int)((long)dx * Speed / length), (int)((long)dy * Speed / length), lift);

        TickBallBoundary result;
        var ticks = 0;

        do
        {
            result = ball.Step();
            ticks++;
        }
        while (result == TickBallBoundary.InPlay && ticks < 200);

        return result;
    }
}
