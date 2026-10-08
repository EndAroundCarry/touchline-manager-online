using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the goalkeeper's feet: the arc between the ball and the middle of his goal, the rush out to a lone attacker or a loose
/// ball, and the smother (Milestone 6).
/// </summary>
/// <remarks>
/// Every position in a scene is given in the goalkeeper's own point of view (he defends X = 0), whichever end the side really
/// plays; a scene mirrors them for the away side.
/// </remarks>
public sealed class TickGoalkeeperAITests
{
    private static readonly SpatialPoint Anchor = new(500, 3_500);

    /// <summary>The goalkeeper's side with the ten outfielders well up the pitch: nobody between a shooter and the goal.</summary>
    private static readonly SpatialPoint[] FarTeam =
    [
        new(150, 3_500),
        new(3_200, 900),
        new(3_000, 2_500),
        new(3_000, 4_500),
        new(3_200, 6_100),
        new(4_800, 900),
        new(4_800, 2_600),
        new(4_800, 4_400),
        new(4_800, 6_100),
        new(6_000, 2_600),
        new(6_000, 4_400),
    ];

    /// <summary>The opposition well away from the goalkeeper's goal.</summary>
    private static readonly SpatialPoint[] FarOpponents =
    [
        new(9_800, 3_500),
        new(7_000, 900),
        new(7_000, 2_500),
        new(7_000, 4_500),
        new(7_000, 6_100),
        new(7_500, 900),
        new(7_500, 2_600),
        new(7_500, 4_400),
        new(7_500, 6_100),
        new(8_000, 2_600),
        new(8_000, 4_400),
    ];

    [Fact]
    public void Inside_the_arc_the_goalkeeper_stands_on_the_line_from_the_middle_of_his_goal_to_the_ball()
    {
        var order = new Scene(true, new SpatialPoint(1_500, 2_500)).Decide();
        var skills = Skills(10);
        var distance = (int)Math.Sqrt((1_500.0 * 1_500) + (1_000.0 * 1_000));

        order.Mode.Should().Be(TickKeeperMode.Guard);
        order.Intent.Arrive.Should().BeTrue();

        double x = order.Intent.TargetXUnits;
        double y = order.Intent.TargetYUnits - SpatialPitch.GoalYCenter;
        var depth = Math.Sqrt((x * x) + (y * y));
        var cross = Math.Abs((x * -1_000) - (y * 1_500));

        depth.Should().BeApproximately(TickGoalkeeperAI.ArcDepth(skills, distance), 3);
        (cross / (depth * distance)).Should().BeLessThan(0.02, "he is on the bisector, within a rounding of a unit");
        y.Should().BeNegative("the ball is on his left, so he stands to that side of the middle");
    }

    [Fact]
    public void Far_from_the_ball_he_stands_on_his_anchor_and_slides_onto_the_arc_as_it_comes()
    {
        var far = new Scene(true, new SpatialPoint(6_000, 3_500)).Decide();
        var halfway = new Scene(true, new SpatialPoint(3_500, 1_500)).Decide();
        var near = new Scene(true, new SpatialPoint(1_500, 1_500)).Decide();

        far.Intent.TargetXUnits.Should().Be(Anchor.X);
        far.Intent.TargetYUnits.Should().Be(Anchor.Y);
        far.Intent.SpeedLimitBasisPoints.Should().BeLessThan(near.Intent.SpeedLimitBasisPoints, "he is only strolling to his place");

        halfway.Intent.TargetYUnits.Should().BeLessThan(Anchor.Y).And.BeGreaterThan(near.Intent.TargetYUnits);
        near.Intent.SpeedLimitBasisPoints.Should().Be(10_000, "the shooter is on him");
    }

    [Fact]
    public void His_depth_off_the_line_grows_as_the_ball_comes_and_with_his_positioning()
    {
        var average = Skills(10);

        TickGoalkeeperAI.ArcDepth(average, TickGoalkeeperAI.ArcRange).Should().Be(TickGoalkeeperAI.MinimumDepth);
        TickGoalkeeperAI.ArcDepth(average, 5_000).Should().Be(TickGoalkeeperAI.MinimumDepth);
        TickGoalkeeperAI.ArcDepth(average, TickGoalkeeperAI.NearRange).Should().Be(350);
        TickGoalkeeperAI.ArcDepth(average, 1_700).Should().BeGreaterThan(TickGoalkeeperAI.ArcDepth(average, 2_200));

        TickGoalkeeperAI.ArcDepth(Skills(1), TickGoalkeeperAI.NearRange).Should().BeInRange(200, 230, "about 2 m for a poor positioner");
        TickGoalkeeperAI.ArcDepth(Skills(20), TickGoalkeeperAI.NearRange).Should().BeInRange(480, 520, "about 5 m for the best");
    }

    [Fact]
    public void He_stays_off_his_line_and_between_the_posts_even_with_the_ball_on_the_byline()
    {
        var order = new Scene(true, new SpatialPoint(60, 600)).Decide();

        order.Intent.TargetXUnits.Should().BeGreaterThanOrEqualTo(TickGoalkeeperAI.LineClearance);
        order.Intent.TargetYUnits.Should().BeInRange(
            SpatialPitch.GoalYMin + TickGoalkeeperAI.PostClearance,
            SpatialPitch.GoalYMax - TickGoalkeeperAI.PostClearance);
    }

    [Fact]
    public void The_away_goalkeeper_is_the_mirror_of_the_home_one()
    {
        var ball = new SpatialPoint(1_500, 2_500);
        var home = new Scene(true, ball).Decide();
        var away = new Scene(false, ball).Decide();

        away.Mode.Should().Be(home.Mode);
        away.Intent.TargetXUnits.Should().Be(SpatialPitch.PitchLength - home.Intent.TargetXUnits);
        away.Intent.TargetYUnits.Should().Be(SpatialPitch.PitchWidth - home.Intent.TargetYUnits);
        away.Intent.SpeedLimitBasisPoints.Should().Be(home.Intent.SpeedLimitBasisPoints);
    }

    [Fact]
    public void While_his_own_side_has_the_ball_he_stays_on_his_anchor()
    {
        var order = new Scene(true, new SpatialPoint(1_500, 2_500)) { Possession = TickKeeperPossession.HeldByOwn }.Decide();

        order.Mode.Should().Be(TickKeeperMode.Guard);
        order.Intent.TargetXUnits.Should().Be(Anchor.X);
        order.Intent.TargetYUnits.Should().Be(Anchor.Y);
    }

    [Fact]
    public void A_lone_attacker_inside_his_range_makes_him_charge_out_to_where_the_ball_will_be()
    {
        var skills = Skills(10) with { OneOnOnes = 15 };
        var order = new Scene(true, new SpatialPoint(1_200, 3_200)) { Skills = skills }.Decide();

        order.Mode.Should().Be(TickKeeperMode.Rush);
        order.Intent.Arrive.Should().BeFalse("he runs through the ball");
        order.Intent.SpeedLimitBasisPoints.Should().Be(10_000);
        order.Intent.TargetXUnits.Should().BeInRange(1_100, 1_300);
        order.Intent.TargetYUnits.Should().BeInRange(3_100, 3_300);
    }

    [Fact]
    public void A_rush_leads_a_ball_that_is_moving_but_never_takes_him_out_of_his_area()
    {
        var ball = new TickBallPhysics();

        ball.PlaceAt(1_500, 3_500);
        ball.Kick(-TickSpatialUnits.SpeedToFixedPerTick(600), 0, 0);

        var skills = Skills(10) with { OneOnOnes = 20 };
        var order = new Scene(true, new SpatialPoint(1_500, 3_500)) { Skills = skills, Ball = ball }.Decide();

        order.Mode.Should().Be(TickKeeperMode.Rush);
        order.Intent.TargetXUnits.Should().BeLessThan(1_500, "he goes to where the ball is heading");

        var outside = new Scene(true, new SpatialPoint(1_700, 3_500)) { Skills = skills }.Decide();

        outside.Intent.TargetXUnits.Should().BeLessThanOrEqualTo(TickGoalkeeperAI.RushLimitX);
    }

    [Fact]
    public void A_teammate_between_the_ball_and_the_goal_keeps_him_on_his_line_but_one_behind_the_ball_does_not()
    {
        var skills = Skills(10) with { OneOnOnes = 20 };
        var ball = new SpatialPoint(1_400, 3_500);

        new Scene(true, ball) { Skills = skills, Teammates = Move(FarTeam, (2, 700, 3_600)) }.Decide().Mode
            .Should().Be(TickKeeperMode.Guard, "a defender is goalside of the attacker and on his line");
        new Scene(true, ball) { Skills = skills, Teammates = Move(FarTeam, (2, 2_500, 3_600)) }.Decide().Mode
            .Should().Be(TickKeeperMode.Rush, "a defender chasing from behind does not cover the goal");
        new Scene(true, ball) { Skills = skills, Teammates = Move(FarTeam, (2, 700, 5_900)) }.Decide().Mode
            .Should().Be(TickKeeperMode.Rush, "a defender on the far side is no cover");
    }

    [Fact]
    public void Only_a_confident_one_on_one_goalkeeper_comes_out_to_an_attacker_far_from_his_goal()
    {
        var ball = new SpatialPoint(1_700, 3_500);

        new Scene(true, ball) { Skills = Skills(1) }.Decide().Mode.Should().Be(TickKeeperMode.Guard);
        new Scene(true, ball) { Skills = Skills(20) }.Decide().Mode.Should().Be(TickKeeperMode.Rush);
        TickGoalkeeperAI.RushRange(Skills(20)).Should().BeInRange(1_800, 2_000, "the plan's 18 m");
    }

    [Fact]
    public void A_goalkeeper_already_running_does_not_pull_up_just_outside_his_range()
    {
        var skills = Skills(10);
        var ball = new SpatialPoint(1_650, 3_500);

        TickGoalkeeperAI.RushRange(skills).Should().Be(1_400);
        new Scene(true, ball) { Skills = skills }.Decide().Mode.Should().Be(TickKeeperMode.Guard);
        new Scene(true, ball) { Skills = skills, WasRushing = true }.Decide().Mode.Should().Be(TickKeeperMode.Rush);
    }

    [Fact]
    public void A_goalkeeper_on_the_floor_cannot_rush()
    {
        var order = new Scene(true, new SpatialPoint(1_000, 3_500)) { Skills = Skills(20), KeeperLockout = 8 }.Decide();

        order.Mode.Should().Be(TickKeeperMode.Guard);
    }

    [Fact]
    public void A_slow_loose_ball_in_his_area_is_his_when_he_is_first_to_it()
    {
        var ball = new SpatialPoint(900, 3_400);

        new Scene(true, ball) { Possession = TickKeeperPossession.Loose }.Decide().Mode
            .Should().Be(TickKeeperMode.Rush);
        new Scene(true, ball) { Possession = TickKeeperPossession.Loose, Opponents = Move(FarOpponents, (3, 1_000, 3_000)) }.Decide().Mode
            .Should().Be(TickKeeperMode.Guard, "a forward is nearer to it");
        new Scene(true, new SpatialPoint(3_000, 3_400)) { Possession = TickKeeperPossession.Loose }.Decide().Mode
            .Should().Be(TickKeeperMode.Guard, "it is nowhere near his area");
    }

    [Fact]
    public void A_ball_travelling_at_pace_is_a_shot_or_a_pass_and_not_a_loose_ball_to_collect()
    {
        var ball = new TickBallPhysics();

        ball.PlaceAt(900, 3_400);
        ball.Kick(-TickSpatialUnits.SpeedToFixedPerTick(1_800), 0, 0);

        new Scene(true, new SpatialPoint(900, 3_400)) { Possession = TickKeeperPossession.Loose, Ball = ball }.Decide().Mode
            .Should().Be(TickKeeperMode.Guard);
    }

    [Fact]
    public void The_goalkeeper_can_smother_a_ball_at_his_feet_but_not_one_beyond_reach_in_the_air_or_while_on_the_floor()
    {
        var keeper = TickPlayerState.Standing(800, 3_500, 0, 10_000);
        var ball = new TickBallPhysics();

        ball.PlaceAt(900, 3_500);
        TickGoalkeeperAI.CanSmother(keeper, ball).Should().BeTrue("a metre away");

        ball.PlaceAt(1_100, 3_500);
        TickGoalkeeperAI.CanSmother(keeper, ball).Should().BeFalse("three metres away");

        ball.Drop(900, 3_500, 30);
        TickGoalkeeperAI.CanSmother(keeper, ball).Should().BeFalse("it is in the air");

        ball.PlaceAt(900, 3_500);
        keeper.Lockout = 5;
        TickGoalkeeperAI.CanSmother(keeper, ball).Should().BeFalse("he is on the floor");
    }

    [Fact]
    public void A_better_one_on_one_goalkeeper_claims_more_often_and_the_chance_is_banded()
    {
        var elite = Skills(20);
        var poor = Skills(1);
        var even = Skills(10);

        TickGoalkeeperAI.ClaimChanceBasisPoints(even, even).Should().Be(5_000);
        TickGoalkeeperAI.ClaimChanceBasisPoints(elite, poor).Should().Be(8_500);
        TickGoalkeeperAI.ClaimChanceBasisPoints(poor, elite).Should().Be(1_500);
        TickGoalkeeperAI.ClaimChanceBasisPoints(even with { OneOnOnes = 16 }, even).Should().BeGreaterThan(5_000);
    }

    [Theory]
    [InlineData(0, "Spilled")]
    [InlineData(1_499, "Spilled")]
    [InlineData(1_500, "Claimed")]
    [InlineData(4_999, "Claimed")]
    [InlineData(5_000, "Foul")]
    [InlineData(5_599, "Foul")]
    [InlineData(5_600, "Beaten")]
    [InlineData(9_999, "Beaten")]
    public void One_draw_settles_the_smother_between_the_four_outcomes(int roll, string expected)
    {
        TickGoalkeeperAI.Smother(Skills(10), Skills(10), roll).ToString().Should().Be(expected);
    }

    [Fact]
    public void A_claim_gives_him_the_ball_and_leaves_the_attacker_off_balance()
    {
        var (keeper, attacker, ball) = Duel();

        TickGoalkeeperAI.ApplySmother(TickSmotherOutcome.Claimed, ref keeper, ref attacker, 0, true, ball);

        ball.Mode.Should().Be(TickBallMode.Controlled);
        ball.ControllerIndex.Should().Be(0);
        attacker.Lockout.Should().Be(TickGoalkeeperAI.DispossessedLockoutTicks);
        attacker.Speed.Should().Be(TickSpatialUnits.SpeedToFixedPerTick(400));
    }

    [Fact]
    public void A_spill_knocks_the_ball_away_from_the_goalkeeper()
    {
        var (keeper, attacker, ball) = Duel();

        TickGoalkeeperAI.ApplySmother(TickSmotherOutcome.Spilled, ref keeper, ref attacker, 0, true, ball);

        ball.Mode.Should().Be(TickBallMode.Loose);
        ball.VelocityX.Should().BePositive("the ball is upfield of him, so it runs away from his goal");
        ball.GroundSpeed.Should().BeInRange(
            TickSpatialUnits.SpeedToFixedPerTick(TickGoalkeeperAI.SpillSpeedCentimetresPerSecond) - 200,
            TickSpatialUnits.SpeedToFixedPerTick(TickGoalkeeperAI.SpillSpeedCentimetresPerSecond) + 200);
        attacker.Lockout.Should().Be(TickGoalkeeperAI.DispossessedLockoutTicks);
    }

    [Fact]
    public void A_beaten_goalkeeper_is_left_on_the_floor_and_a_foul_stops_everything()
    {
        var (keeper, attacker, ball) = Duel();

        TickGoalkeeperAI.ApplySmother(TickSmotherOutcome.Beaten, ref keeper, ref attacker, 0, true, ball);

        keeper.Lockout.Should().Be(TickGoalkeeperAI.BeatenLockoutTicks);
        attacker.Lockout.Should().Be(0);

        (keeper, attacker, ball) = Duel();
        attacker.Speed = 5_000;
        TickGoalkeeperAI.ApplySmother(TickSmotherOutcome.Foul, ref keeper, ref attacker, 0, true, ball);

        ball.Mode.Should().Be(TickBallMode.Loose);
        ball.GroundSpeed.Should().Be(0);
        attacker.Speed.Should().Be(0);
        keeper.Speed.Should().Be(0);
    }

    [Fact]
    public void A_season_of_goalkeeper_orders_is_repeatable_and_allocates_nothing()
    {
        int Run(Scene[] scenes)
        {
            var hash = 17;

            for (var tick = 0; tick < 54_000; tick++)
            {
                var order = scenes[tick % scenes.Length].Decide();

                hash = unchecked((hash * 31) + (int)order.Mode + (order.Intent.TargetXUnits * 7) + order.Intent.TargetYUnits + order.Intent.SpeedLimitBasisPoints);
            }

            return hash;
        }

        Scene[] Scenes() =>
        [
            new(true, new SpatialPoint(1_500, 2_500)),
            new(false, new SpatialPoint(1_200, 3_300)) { Skills = Skills(16) },
            new(true, new SpatialPoint(900, 3_400)) { Possession = TickKeeperPossession.Loose },
            new(true, new SpatialPoint(6_000, 3_500)),
        ];

        var first = Run(Scenes());
        var scenes = Scenes();

        foreach (var scene in scenes)
        {
            scene.Decide();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run(scenes);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);
        allocated.Should().BeLessThan(256, "54,000 orders must not allocate");
    }

    private static (TickPlayerState Keeper, TickPlayerState Attacker, TickBallPhysics Ball) Duel()
    {
        var keeper = TickPlayerState.Standing(800, 3_500, 0, 10_000);
        var attacker = TickPlayerState.Standing(880, 3_500, TickTrigonometry.HalfTurn, 10_000);
        var ball = new TickBallPhysics();

        attacker.Speed = TickSpatialUnits.SpeedToFixedPerTick(800);
        ball.PlaceAt(900, 3_500);
        ball.Attach(11);

        return (keeper, attacker, ball);
    }

    private static SpatialPoint[] Move(SpatialPoint[] layout, params (int Index, int X, int Y)[] moves)
    {
        var copy = (SpatialPoint[])layout.Clone();

        foreach (var (index, x, y) in moves)
        {
            copy[index] = new SpatialPoint(x, y);
        }

        return copy;
    }

    private static TickPlayerSkills Skills(int value) =>
        TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(value, MatchAttributeNames.Count).ToArray()));

    /// <summary>One goalkeeper, his side, the opposition and the ball, everything given in the goalkeeper's own point of view.</summary>
    private sealed class Scene
    {
        private readonly bool _isHome;
        private readonly SpatialPoint _ball;
        private TickPlayerState[]? _teammates;
        private TickPlayerState[]? _opponents;
        private SpatialPoint _anchor;
        private TickBallPhysics? _theBall;

        public Scene(bool isHome, SpatialPoint ball)
        {
            _isHome = isHome;
            _ball = ball;
            Possession = TickKeeperPossession.HeldByOpponent;
            Skills = TickGoalkeeperAITests.Skills(10);
            Teammates = FarTeam;
            Opponents = FarOpponents;
        }

        public TickKeeperPossession Possession { get; init; }

        public TickPlayerSkills Skills { get; init; }

        public SpatialPoint[] Teammates { get; init; }

        public SpatialPoint[] Opponents { get; init; }

        public TickBallPhysics? Ball { get; init; }

        public bool WasRushing { get; init; }

        public int KeeperLockout { get; init; }

        public TickKeeperOrder Decide()
        {
            if (_teammates is null)
            {
                _theBall = Ball ?? new TickBallPhysics();

                if (Ball is null)
                {
                    _theBall.PlaceAt(Mirror(_ball).X, Mirror(_ball).Y);
                }

                _teammates = [.. Teammates.Select(point => State(point, 0))];
                _opponents = [.. Opponents.Select(point => State(point, TickTrigonometry.HalfTurn))];
                _teammates[0].Lockout = KeeperLockout;

                if (Possession == TickKeeperPossession.HeldByOpponent)
                {
                    _opponents[1] = State(_ball, TickTrigonometry.HalfTurn);
                }

                _anchor = Mirror(Anchor);
            }

            return TickGoalkeeperAI.Decide(new TickKeeperSituation
            {
                IsHome = _isHome,
                Keeper = _teammates[0],
                Skills = Skills,
                Anchor = _anchor,
                Ball = _theBall!,
                Possession = Possession,
                Teammates = _teammates,
                Opponents = _opponents!,
                WasRushing = WasRushing,
            });
        }

        private TickPlayerState State(SpatialPoint own, int heading)
        {
            var point = Mirror(own);

            return TickPlayerState.Standing(point.X, point.Y, _isHome ? heading : heading + TickTrigonometry.HalfTurn, 10_000);
        }

        private SpatialPoint Mirror(SpatialPoint point) =>
            _isHome ? point : new SpatialPoint(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);
    }
}
