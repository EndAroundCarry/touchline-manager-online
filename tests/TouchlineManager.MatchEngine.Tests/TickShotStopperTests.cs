using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies shot-stopping: the forecast of a shot, the goalkeeper's reaction and dive, the reach that decides a save, the draws that
/// settle it, and what the ball does afterwards (Milestone 6).
/// </summary>
/// <remarks>
/// Scenes defend the home goal (X = 0) unless a test says otherwise. A shot is launched the way the carrier brain launches one: rolled
/// along the ground at 18 m/s on arrival, aimed beyond the goal line.
/// </remarks>
public sealed class TickShotStopperTests
{
    private const int KeeperDepth = 216;

    private static readonly int ShotSpeed = TickSpatialUnits.SpeedToFixedPerTick(1_800);

    [Fact]
    public void A_shot_on_target_is_forecast_to_the_net_with_the_time_height_and_place_it_passes_the_goalkeeper()
    {
        var ball = Shot(1_100, 3_850);
        var forecast = Forecast(ball, true);

        forecast.OnTarget.Should().BeTrue();
        forecast.Boundary.Should().Be(TickBallBoundary.GoalHomeEnd);
        forecast.PlaneTicks.Should().BeInRange(1, forecast.Ticks);
        forecast.PlaneY.Should().BeInRange(3_700, 3_760);
        forecast.PlaneZ.Should().Be(0);
        forecast.PlaneSpeed.Should().BeGreaterThan(ShotSpeed);
    }

    [Fact]
    public void The_forecast_leaves_the_real_ball_alone()
    {
        var ball = Shot(1_100, 3_850);
        var x = ball.X;
        var vx = ball.VelocityX;

        Forecast(ball, true);

        ball.X.Should().Be(x);
        ball.VelocityX.Should().Be(vx);
        ball.Mode.Should().Be(TickBallMode.Loose);
    }

    [Fact]
    public void A_shot_wide_of_the_post_is_not_on_target_and_one_that_hits_the_post_is_not_a_save_to_make()
    {
        var wide = Forecast(Shot(1_100, 5_000), true);
        var post = Forecast(Shot(1_100, 2_986), true);

        wide.OnTarget.Should().BeFalse();
        wide.Boundary.Should().Be(TickBallBoundary.OutGoalLine);
        post.OnTarget.Should().BeFalse();
        post.Boundary.Should().Be(TickBallBoundary.HitPost);
    }

    [Fact]
    public void A_ball_going_the_other_way_or_held_or_stopping_short_is_no_shot()
    {
        var away = new TickBallPhysics();

        away.PlaceAt(1_100, 3_500);
        away.LaunchRolling(5_000, 3_500, ShotSpeed);
        Forecast(away, true).Should().Be(TickShotForecast.None);

        var held = new TickBallPhysics();

        held.PlaceAt(1_100, 3_500);
        held.Attach(11);
        Forecast(held, true).Should().Be(TickShotForecast.None);

        var slow = new TickBallPhysics();

        slow.PlaceAt(1_100, 3_500);
        slow.LaunchRolling(800, 3_500);
        Forecast(slow, true).Should().Be(TickShotForecast.None, "the ball dies at 800 and never reaches the plane");
    }

    [Fact]
    public void The_forecast_works_the_same_for_the_goalkeeper_at_the_other_end()
    {
        var home = Forecast(Shot(1_100, 3_850), true);
        var ball = new TickBallPhysics();

        ball.PlaceAt(SpatialPitch.PitchLength - 1_100, 3_500);
        ball.LaunchRolling(SpatialPitch.PitchLength + 400, SpatialPitch.PitchWidth - 3_850, ShotSpeed);

        var away = TickShotStopper.Forecast(ball, new TickBallPhysics(), false, TickSpatialUnits.PitchLengthFixed - TickSpatialUnits.ToFixed(KeeperDepth));

        away.OnTarget.Should().BeTrue();
        away.Boundary.Should().Be(TickBallBoundary.GoalAwayEnd);
        away.PlaneTicks.Should().Be(home.PlaneTicks);
        away.PlaneY.Should().BeInRange(3_240, 3_300, "the mirror of 3,760 is 3,240");
    }

    [Fact]
    public void Reflexes_set_the_reaction_time_between_150_and_340_milliseconds()
    {
        TickShotStopper.ReactionMilliseconds(Skills(20)).Should().Be(150);
        TickShotStopper.ReactionMilliseconds(Skills(10)).Should().Be(250);
        TickShotStopper.ReactionMilliseconds(Skills(1)).Should().Be(340);
    }

    [Fact]
    public void The_goalkeeper_stands_still_until_he_has_reacted_and_then_runs_flat_out_across_the_ball()
    {
        var forecast = Forecast(Shot(1_100, 3_850), true);
        var keeper = Keeper();

        foreach (var (reflexes, reactsAtTick) in new[] { (20, 2), (10, 3), (1, 4) })
        {
            var skills = Skills(10) with { Reflexes = reflexes };

            for (var tick = 0; tick < reactsAtTick; tick++)
            {
                TickShotStopper.DiveIntent(keeper, skills, forecast, tick).SpeedLimitBasisPoints
                    .Should().Be(0, $"Reflexes {reflexes} have not read the shot at tick {tick}");
            }

            var dive = TickShotStopper.DiveIntent(keeper, skills, forecast, reactsAtTick);

            dive.SpeedLimitBasisPoints.Should().Be(10_000);
            dive.Arrive.Should().BeFalse();
            dive.TargetYUnits.Should().Be(forecast.PlaneY);
            dive.TargetXUnits.Should().Be(KeeperDepth);
        }

        TickShotStopper.DiveIntent(keeper, Skills(10), Forecast(Shot(1_100, 5_000), true), 9).SpeedLimitBasisPoints
            .Should().Be(0, "he does not run at a shot that is going wide");
    }

    [Fact]
    public void The_save_is_settled_on_the_tick_before_the_ball_passes_him()
    {
        var forecast = new TickShotForecast(true, TickBallBoundary.GoalHomeEnd, 6, 5, 3_700, 0, 100_000);

        for (var tick = 0; tick < 4; tick++)
        {
            TickShotStopper.ShouldResolve(forecast, tick).Should().BeFalse($"tick {tick}");
        }

        TickShotStopper.ShouldResolve(forecast, 4).Should().BeTrue();
        TickShotStopper.ShouldResolve(forecast with { OnTarget = false }, 4).Should().BeFalse();
    }

    [Fact]
    public void Agility_and_handling_and_jumping_widen_his_reach()
    {
        var keeper = Keeper();
        var forecast = new TickShotForecast(true, TickBallBoundary.GoalHomeEnd, 6, 5, 3_700, 0, ShotSpeed);
        var average = TickShotStopper.Assess(keeper, Skills(10), forecast);
        var agile = TickShotStopper.Assess(keeper, Skills(10) with { Agility = 20 }, forecast);
        var safe = TickShotStopper.Assess(keeper, Skills(10) with { Handling = 20 }, forecast);
        var tall = TickShotStopper.Assess(keeper, Skills(10) with { JumpingReach = 20, AerialAbility = 20 }, forecast);

        average.Needed.Should().Be(200);
        average.ParryReach.Should().Be(170);
        average.CatchReach.Should().BeLessThan(average.ParryReach);
        agile.ParryReach.Should().BeGreaterThan(average.ParryReach);
        safe.CatchReach.Should().BeGreaterThan(average.CatchReach);
        safe.HoldBasisPoints.Should().BeGreaterThan(average.HoldBasisPoints);
        tall.HeightReach.Should().BeGreaterThan(average.HeightReach);
        TickShotStopper.Assess(keeper, Skills(20), forecast).HeightReach.Should().BeInRange(33, 38);
    }

    [Fact]
    public void A_faster_ball_is_harder_to_hold_and_a_stretched_save_is_more_likely_to_go_behind()
    {
        var keeper = Keeper();
        var slow = new TickShotForecast(true, TickBallBoundary.GoalHomeEnd, 6, 5, 3_550, 0, TickSpatialUnits.SpeedToFixedPerTick(800));
        var fast = slow with { PlaneSpeed = TickSpatialUnits.SpeedToFixedPerTick(2_800) };
        var stretched = slow with { PlaneY = 3_650 };

        TickShotStopper.Assess(keeper, Skills(10), slow).HoldBasisPoints.Should().Be(6_800);
        TickShotStopper.Assess(keeper, Skills(10), fast).HoldBasisPoints.Should().BeLessThan(6_800 - 5_000);
        TickShotStopper.Assess(keeper, Skills(10), stretched).TipBasisPoints
            .Should().BeGreaterThan(TickShotStopper.Assess(keeper, Skills(10), slow).TipBasisPoints);
    }

    [Theory]
    [InlineData(201, 20, 0, 0, "Beaten")]
    [InlineData(100, 31, 0, 0, "Beaten")]
    [InlineData(100, 20, 5_999, 0, "Caught")]
    [InlineData(100, 20, 6_000, 2_999, "TippedAround")]
    [InlineData(100, 20, 6_000, 3_000, "Parried")]
    [InlineData(150, 20, 0, 9_999, "Parried")]
    [InlineData(100, 27, 0, 9_999, "Parried")]
    public void Two_draws_settle_a_save_inside_his_reach_and_nothing_softens_a_shot_outside_it(
        int needed,
        int planeZ,
        int holdRoll,
        int deflectRoll,
        string expected)
    {
        var assessment = new TickSaveAssessment(needed, 200, 140, 30, 6_000, 3_000);

        TickShotStopper.Resolve(assessment, planeZ, holdRoll, deflectRoll).ToString().Should().Be(expected);
    }

    [Fact]
    public void A_save_takes_two_draws_from_the_stream_whatever_the_outcome()
    {
        foreach (var needed in new[] { 50, 150, 500 })
        {
            var used = new Pcg32(99);
            var fresh = new Pcg32(99);
            var assessment = new TickSaveAssessment(needed, 200, 140, 30, 6_000, 3_000);

            TickShotStopper.Resolve(assessment, 0, used, out var deflect);

            var hold = fresh.NextBasisPoints();

            deflect.Should().Be(fresh.NextBasisPoints());
            hold.Should().BeInRange(0, 9_999);
            used.NextUInt32().Should().Be(fresh.NextUInt32(), $"needed {needed}");
        }
    }

    [Fact]
    public void A_weak_long_range_shot_never_beats_a_competent_goalkeeper_but_a_close_corner_shot_beats_an_ordinary_one()
    {
        var ordinary = Skills(10);
        var longRange = Seeds().Select(seed => Play(3_100, 3_850, ordinary, seed)).ToList();
        var closeRange = Seeds().Select(seed => Play(1_100, 3_850, ordinary, seed)).ToList();

        longRange.Should().OnlyContain(outcome => outcome != TickSaveOutcome.Beaten, "he has more than a second to cross the goal");
        closeRange.Should().OnlyContain(outcome => outcome == TickSaveOutcome.Beaten, "the corner is out of an ordinary keeper's reach at 11 m");
    }

    [Fact]
    public void An_elite_goalkeeper_saves_the_close_shot_an_ordinary_one_cannot()
    {
        var elite = Skills(10) with { Reflexes = 20, Agility = 20, Pace = 20, Acceleration = 20, Handling = 20 };

        Seeds().Select(seed => Play(1_100, 3_850, elite, seed))
            .Count(outcome => outcome != TickSaveOutcome.Beaten)
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_shot_down_the_middle_is_never_scored_and_a_safer_pair_of_hands_holds_it_more_often()
    {
        int Held(int handling) => Seeds()
            .Select(seed => Play(2_200, 3_500, Skills(10) with { Handling = handling }, seed))
            .Count(outcome => outcome == TickSaveOutcome.Caught);

        Seeds().Select(seed => Play(2_200, 3_500, Skills(10), seed))
            .Should().OnlyContain(outcome => outcome != TickSaveOutcome.Beaten);
        Held(10).Should().BeGreaterThan(0, "a drive at 20 m/s is held sometimes");
        Held(20).Should().BeGreaterThan(Held(10));
    }

    [Fact]
    public void A_caught_shot_ends_in_his_hands_with_him_on_the_ball()
    {
        var ball = Shot(1_500, 3_500);
        var keeper = Keeper();

        for (var tick = 0; tick < 3; tick++)
        {
            ball.Step();
        }

        TickShotStopper.Apply(TickSaveOutcome.Caught, ref keeper, 0, true, ball, 0);

        ball.Mode.Should().Be(TickBallMode.Controlled);
        ball.ControllerIndex.Should().Be(0);
        keeper.X.Should().Be(ball.X);
        keeper.Y.Should().Be(ball.Y);
        keeper.Speed.Should().Be(0);
        keeper.Heading.Should().Be(0, "the home goalkeeper faces up the pitch");
    }

    [Fact]
    public void A_parry_runs_loose_upfield_towards_the_flank_the_shot_came_from_and_the_away_one_mirrors_it()
    {
        var ball = Shot(1_500, 3_800);
        var keeper = Keeper();

        TickShotStopper.Apply(TickSaveOutcome.Parried, ref keeper, 0, true, ball, 50);

        ball.Mode.Should().Be(TickBallMode.Loose);
        ball.VelocityX.Should().BePositive();
        ball.VelocityY.Should().BePositive("the shot was on the high-Y side of the goal");
        ball.GroundSpeed.Should().BeInRange(
            TickSpatialUnits.SpeedToFixedPerTick(TickShotStopper.ParrySpeedCentimetresPerSecond) - 300,
            TickSpatialUnits.SpeedToFixedPerTick(TickShotStopper.ParrySpeedCentimetresPerSecond) + 300);

        var awayBall = new TickBallPhysics();
        var awayKeeper = TickPlayerState.Standing(9_784, 3_500, TickTrigonometry.HalfTurn, 10_000);

        awayBall.PlaceAt(8_500, 3_200);
        TickShotStopper.Apply(TickSaveOutcome.Parried, ref awayKeeper, 0, false, awayBall, 50);

        awayBall.VelocityX.Should().BeNegative();
        awayBall.VelocityY.Should().BeNegative("the shot was on the low-Y side");
    }

    [Fact]
    public void A_beaten_goalkeeper_changes_nothing()
    {
        var ball = Shot(1_500, 3_800);
        var keeper = Keeper();
        var x = ball.VelocityX;

        TickShotStopper.Apply(TickSaveOutcome.Beaten, ref keeper, 0, true, ball, 50);

        ball.VelocityX.Should().Be(x);
        keeper.X.Should().Be(TickSpatialUnits.ToFixed(KeeperDepth));
    }

    [Fact]
    public void A_ball_tipped_round_the_post_crosses_the_goal_line_outside_the_frame_and_one_tipped_over_clears_the_bar()
    {
        var ball = Shot(1_500, 3_800);

        for (var tick = 0; tick < 4; tick++)
        {
            ball.Step();
        }

        var keeper = Keeper();

        TickShotStopper.Apply(TickSaveOutcome.TippedAround, ref keeper, 0, true, ball, 0);
        EndOf(ball).Should().Be(TickBallBoundary.OutGoalLine, "round the post, for a corner");

        var high = new TickBallPhysics();

        high.Drop(300, 3_600, 26);
        keeper = Keeper();
        TickShotStopper.Apply(TickSaveOutcome.TippedAround, ref keeper, 0, true, high, 0);
        EndOf(high).Should().Be(TickBallBoundary.OutGoalLine, "over the bar, for a corner");
    }

    [Fact]
    public void A_ball_tipped_away_from_the_far_end_goes_behind_that_goal_too()
    {
        var ball = new TickBallPhysics();
        var keeper = TickPlayerState.Standing(9_784, 3_500, TickTrigonometry.HalfTurn, 10_000);

        ball.PlaceAt(9_700, 3_300);
        TickShotStopper.Apply(TickSaveOutcome.TippedAround, ref keeper, 0, false, ball, 0);

        EndOf(ball).Should().Be(TickBallBoundary.OutGoalLine);
        ball.X.Should().Be(TickSpatialUnits.PitchLengthFixed);
    }

    [Fact]
    public void A_thousand_forecasts_are_repeatable_and_allocate_nothing()
    {
        int Run(TickBallPhysics[] balls, TickBallPhysics scratch)
        {
            var hash = 17;

            for (var index = 0; index < 1_000; index++)
            {
                var forecast = TickShotStopper.Forecast(balls[index % balls.Length], scratch, true, TickSpatialUnits.ToFixed(KeeperDepth));

                hash = unchecked((hash * 31) + forecast.Ticks + (forecast.PlaneY * 7) + (int)forecast.Boundary);
            }

            return hash;
        }

        TickBallPhysics[] Balls() => [Shot(1_100, 3_850), Shot(3_000, 3_200), Shot(2_000, 5_000), Shot(1_500, 3_022)];

        var first = Run(Balls(), new TickBallPhysics());
        var balls = Balls();
        var scratch = new TickBallPhysics();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run(balls, scratch);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);
        allocated.Should().BeLessThan(256, "forecasting is stepping a scratch ball and nothing else");
    }

    /// <summary>Plays one shot out as the tick loop will: the goalkeeper dives, the save is settled before the ball passes him.</summary>
    private static TickSaveOutcome Play(int fromX, int toY, TickPlayerSkills goalkeeper, ulong seed)
    {
        var ball = Shot(fromX, toY);
        var keeper = Keeper();
        var profile = TickPlayerProfile.From(Attributes(goalkeeper));
        var forecast = TickShotStopper.Forecast(ball, new TickBallPhysics(), true, keeper.X);
        var random = new Pcg32(seed);

        for (var tick = 0; tick < 40; tick++)
        {
            TickPlayerPhysics.Step(ref keeper, profile, TickShotStopper.DiveIntent(keeper, goalkeeper, forecast, tick));

            if (TickShotStopper.ShouldResolve(forecast, tick))
            {
                var assessment = TickShotStopper.Assess(keeper, goalkeeper, forecast);

                return TickShotStopper.Resolve(assessment, forecast.PlaneZ, random, out _);
            }

            if (ball.Step() != TickBallBoundary.InPlay)
            {
                break;
            }
        }

        throw new InvalidOperationException("The shot was never settled.");
    }

    private static IEnumerable<ulong> Seeds() => Enumerable.Range(1, 50).Select(seed => (ulong)seed);

    private static TickBallBoundary EndOf(TickBallPhysics ball)
    {
        for (var tick = 0; tick < 60; tick++)
        {
            var boundary = ball.Step();

            if (boundary != TickBallBoundary.InPlay)
            {
                return boundary;
            }
        }

        throw new InvalidOperationException("The ball never left the pitch.");
    }

    private static TickBallPhysics Shot(int fromX, int toY)
    {
        var ball = new TickBallPhysics();

        ball.PlaceAt(fromX, 3_500);
        ball.LaunchRolling(-400, toY, ShotSpeed);

        return ball;
    }

    private static TickShotForecast Forecast(TickBallPhysics ball, bool keeperIsHome) =>
        TickShotStopper.Forecast(ball, new TickBallPhysics(), keeperIsHome, TickSpatialUnits.ToFixed(KeeperDepth));

    private static TickPlayerState Keeper() => TickPlayerState.Standing(KeeperDepth, 3_500, 0, 10_000);

    private static TickPlayerSkills Skills(int value) =>
        TickPlayerSkills.From(Attributes(value));

    private static PlayerAttributesV1 Attributes(int value) =>
        PlayerAttributesV1.From(Enumerable.Repeat(value, MatchAttributeNames.Count).ToArray());

    /// <summary>Builds the attributes behind a skills record by laying its fields back onto the canonical table.</summary>
    private static PlayerAttributesV1 Attributes(TickPlayerSkills skills)
    {
        var values = new int[MatchAttributeNames.Count];

        values[(int)MatchAttributeName.Pace] = skills.Pace;
        values[(int)MatchAttributeName.Acceleration] = skills.Acceleration;
        values[(int)MatchAttributeName.Agility] = skills.Agility;
        values[(int)MatchAttributeName.Stamina] = 10;
        values[(int)MatchAttributeName.WorkRate] = 10;

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = values[index] == 0 ? 10 : values[index];
        }

        return PlayerAttributesV1.From(values);
    }
}
