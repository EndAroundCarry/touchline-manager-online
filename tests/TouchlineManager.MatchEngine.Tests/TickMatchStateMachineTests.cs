using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the restarts and set pieces of the tick engine: the dead-ball state machine, the set-piece pictures and the
/// kicks that take them (Milestone 7).
/// </summary>
/// <remarks>
/// Every position in a scene is given in the taking side's own point of view (it attacks towards X = 10,000), whichever
/// end the side really plays; a scene mirrors them for the away side. Kick tests settle the players into their set-piece
/// places first, as the tick loop does while the whistle holds.
/// </remarks>
public sealed class TickMatchStateMachineTests
{
    /// <summary>A 4-4-2 in the side's own point of view: goalkeeper first, then defence, midfield, attack.</summary>
    private static readonly TickAnchorSpec[] FourFourTwo =
    [
        new(500, 3_500, MatchPositionFamily.Goalkeeper),
        new(2_200, 900, MatchPositionFamily.Defence),
        new(2_000, 2_500, MatchPositionFamily.Defence),
        new(2_000, 4_500, MatchPositionFamily.Defence),
        new(2_200, 6_100, MatchPositionFamily.Defence),
        new(5_000, 900, MatchPositionFamily.Midfield),
        new(4_800, 2_600, MatchPositionFamily.Midfield),
        new(4_800, 4_400, MatchPositionFamily.Midfield),
        new(5_000, 6_100, MatchPositionFamily.Midfield),
        new(7_500, 2_600, MatchPositionFamily.Attack),
        new(7_500, 4_400, MatchPositionFamily.Attack),
    ];

    /// <summary>The taking side's shape, in its own point of view.</summary>
    private static readonly SpatialPoint[] Shape =
    [
        new(500, 3_500),
        new(2_000, 900),
        new(1_800, 2_500),
        new(1_800, 4_500),
        new(2_000, 6_100),
        new(4_200, 900),
        new(4_200, 2_600),
        new(4_200, 4_400),
        new(4_200, 6_100),
        new(4_500, 2_800),
        new(4_500, 4_200),
    ];

    /// <summary>The defending side's shape as the taking side sees it: their own 4-4-2, mirrored.</summary>
    private static readonly SpatialPoint[] OppositeShape =
    [
        new(9_500, 3_500),
        new(8_000, 6_100),
        new(8_200, 4_500),
        new(8_200, 2_500),
        new(8_000, 900),
        new(5_800, 6_100),
        new(5_800, 4_400),
        new(5_800, 2_600),
        new(5_800, 900),
        new(5_500, 4_200),
        new(5_500, 2_800),
    ];

    [Fact]
    public void A_goal_is_celebrated_and_the_conceding_side_kicks_off()
    {
        var machine = new TickMatchStateMachine();

        machine.Goal(MatchSide.Home);

        machine.Phase.Should().Be(TickMatchPhase.GoalCelebration);
        machine.Restart.Side.Should().Be(MatchSide.Away, "the side that conceded kicks off");
        machine.Restart.Ball.Should().Be(SpatialPoint.Center);
        machine.IsDeadBall.Should().BeTrue();

        for (var tick = 0; tick < TickMatchStateMachine.GoalCelebrationTicks; tick++)
        {
            machine.Step();
        }

        machine.Phase.Should().Be(TickMatchPhase.KickOffPending, "the celebration chains into the kick-off");
        machine.Restart.Side.Should().Be(MatchSide.Away);
        machine.HoldTicksRemaining.Should().Be(TickMatchStateMachine.KickOffHoldTicks);

        for (var tick = 0; tick < TickMatchStateMachine.KickOffHoldTicks - 1; tick++)
        {
            machine.Step().Should().BeFalse("the hold is still running");
        }

        machine.Step().Should().BeTrue("the hold ends and the kick is due");
        machine.Phase.Should().Be(TickMatchPhase.OpenPlay);
        machine.IsKickDue.Should().BeTrue();

        machine.KickTaken();

        machine.IsKickDue.Should().BeFalse();
        machine.IsDeadBall.Should().BeFalse();
        machine.Step().Should().BeFalse("a match in open play does not change phase");
    }

    [Fact]
    public void The_second_half_begins_with_the_away_sides_kick_off()
    {
        var machine = new TickMatchStateMachine();

        machine.HalfTime();

        machine.Phase.Should().Be(TickMatchPhase.HalfTime);
        machine.Restart.Side.Should().Be(MatchSide.Away, "the away side kicks off the second half");

        for (var tick = 0; tick < TickMatchStateMachine.HalfTimeTicks; tick++)
        {
            machine.Step();
        }

        machine.Phase.Should().Be(TickMatchPhase.KickOffPending);
        machine.Restart.Side.Should().Be(MatchSide.Away);
        machine.Restart.Ball.Should().Be(SpatialPoint.Center);
    }

    [Fact]
    public void A_ball_over_the_touchline_is_a_throw_in_for_the_other_side_on_the_line()
    {
        var machine = new TickMatchStateMachine();

        machine.TouchlineOut(new SpatialPoint(3_200, 6_980), MatchSide.Home);

        machine.Phase.Should().Be(TickMatchPhase.ThrowInPending);
        machine.Restart.Side.Should().Be(MatchSide.Away, "the side that did not put it out takes it");
        machine.Restart.Ball.Should().Be(new SpatialPoint(3_200, SpatialPitch.PitchWidth), "the ball is on the line where it crossed");
    }

    [Fact]
    public void A_ball_behind_the_goal_line_is_a_corner_or_a_goal_kick_by_who_put_it_there()
    {
        var corner = new TickMatchStateMachine();

        corner.GoalLineOut(homeGoalLine: true, new SpatialPoint(0, 300), MatchSide.Home);

        corner.Phase.Should().Be(TickMatchPhase.CornerPending, "a defender put it behind his own line");
        corner.Restart.Side.Should().Be(MatchSide.Away);
        corner.Restart.Ball.Should().Be(
            new SpatialPoint(TickSetPieces.CornerInset, TickSetPieces.CornerInset),
            "the ball goes to the corner arc on the side it went out");

        var goalKick = new TickMatchStateMachine();

        goalKick.GoalLineOut(homeGoalLine: true, new SpatialPoint(0, 3_600), MatchSide.Away);

        goalKick.Phase.Should().Be(TickMatchPhase.GoalKickPending, "an attacker put it behind");
        goalKick.Restart.Side.Should().Be(MatchSide.Home);
        goalKick.Restart.Ball.Should().Be(
            new SpatialPoint(TickSetPieces.GoalAreaDepth, SpatialPitch.GoalYCenter),
            "the ball is placed on the six-yard line");
    }

    [Fact]
    public void A_foul_is_a_penalty_inside_the_area_and_a_free_kick_outside_it()
    {
        var outside = new TickMatchStateMachine();

        outside.Foul(new SpatialPoint(8_300, 3_500), MatchSide.Home);

        outside.Phase.Should().Be(TickMatchPhase.FreeKickPending);
        outside.Restart.Ball.Should().Be(new SpatialPoint(8_300, 3_500), "the ball is placed at the foul");

        var inside = new TickMatchStateMachine();

        inside.Foul(new SpatialPoint(8_600, 3_500), MatchSide.Home);

        inside.Phase.Should().Be(TickMatchPhase.PenaltyPending);
        inside.Restart.Side.Should().Be(MatchSide.Home);
        inside.Restart.Ball.Should().Be(new SpatialPoint(SpatialPitch.PenaltySpotAwayX, SpatialPitch.PenaltySpotY));

        var againstAway = new TickMatchStateMachine();

        againstAway.Foul(new SpatialPoint(1_200, 3_500), MatchSide.Away);

        againstAway.Phase.Should().Be(TickMatchPhase.PenaltyPending);
        againstAway.Restart.Side.Should().Be(MatchSide.Away);
        againstAway.Restart.Ball.Should().Be(new SpatialPoint(SpatialPitch.PenaltySpotHomeX, SpatialPitch.PenaltySpotY));

        var wide = new TickMatchStateMachine();

        wide.Foul(new SpatialPoint(9_500, 700), MatchSide.Home);

        wide.Phase.Should().Be(TickMatchPhase.FreeKickPending, "a foul beside the area is not a penalty");
    }

    [Fact]
    public void Every_restart_holds_its_setup_for_between_eight_and_fifteen_ticks()
    {
        var restarts = new[]
        {
            TickMatchPhase.KickOffPending,
            TickMatchPhase.GoalKickPending,
            TickMatchPhase.CornerPending,
            TickMatchPhase.ThrowInPending,
            TickMatchPhase.FreeKickPending,
            TickMatchPhase.PenaltyPending,
        };

        foreach (var restart in restarts)
        {
            TickMatchStateMachine.HoldTicksFor(restart)
                .Should().BeInRange(8, 15, "the plan holds every restart for 0.8 to 1.5 seconds");
        }

        var machine = new TickMatchStateMachine();
        machine.KickOff(MatchSide.Home);

        for (var tick = 0; tick < TickMatchStateMachine.KickOffHoldTicks; tick++)
        {
            machine.Phase.Should().Be(TickMatchPhase.KickOffPending, "the hold is still running");
            machine.Step();
        }

        machine.IsKickDue.Should().BeTrue();
    }

    [Fact]
    public void A_kick_off_puts_two_players_at_the_centre_and_everyone_else_in_his_own_half()
    {
        var (takers, defenders, plan) = new Scene().Assign();

        plan.Taker.Should().BeInRange(1, 10);
        plan.Partner.Should().BeInRange(1, 10).And.NotBe(plan.Taker);
        Distance(takers[plan.Taker], SpatialPoint.Center).Should().BeLessThanOrEqualTo(TickSetPieces.KickOffCircleRadius);
        Distance(takers[plan.Partner], SpatialPoint.Center).Should().BeLessThanOrEqualTo(TickSetPieces.KickOffCircleRadius);

        for (var index = 0; index < takers.Length; index++)
        {
            if (index == plan.Taker || index == plan.Partner)
            {
                continue;
            }

            takers[index].X.Should().BeLessThanOrEqualTo(TickSetPieces.KickOffOwnHalfLimit, "everyone waits in his own half at a kick-off");
        }

        for (var index = 0; index < defenders.Length; index++)
        {
            defenders[index].X.Should().BeGreaterThanOrEqualTo(TickSetPieces.KickOffOpponentHalfLimit, "the opponents wait in their own half");
            Distance(defenders[index], SpatialPoint.Center).Should().BeGreaterThanOrEqualTo(
                TickSetPieces.KickOffCircleRadius - 1,
                "the opponents stand outside the centre circle");
        }
    }

    [Fact]
    public void A_goal_kick_is_placed_on_the_six_yard_line_with_the_centre_backs_split_wide()
    {
        var (takers, defenders, plan) = new Scene
        {
            Kind = TickMatchPhase.GoalKickPending,
            Ball = new SpatialPoint(TickSetPieces.GoalAreaDepth, SpatialPitch.GoalYCenter),
        }.Assign();

        plan.Taker.Should().Be(0, "the goalkeeper takes his own goal kick");
        takers[0].Should().Be(new SpatialPoint(TickSetPieces.GoalAreaDepth - TickSetPieces.TakerStandOff, SpatialPitch.GoalYCenter));

        takers[2].Should().Be(new SpatialPoint(TickSetPieces.GoalKickCentreBackX, TickSetPieces.GoalKickSplitYMin));
        takers[3].Should().Be(new SpatialPoint(TickSetPieces.GoalKickCentreBackX, TickSetPieces.GoalKickSplitYMax));
        takers[1].X.Should().Be(TickSetPieces.GoalKickFullBackX, "the full-backs push up ahead of the split centre-backs");
        takers[4].X.Should().Be(TickSetPieces.GoalKickFullBackX);
        takers[6].Should().Be(new SpatialPoint(TickSetPieces.GoalKickMidfieldX, 2_600));
        takers[9].Should().Be(new SpatialPoint(TickSetPieces.GoalKickAttackX, 2_600));

        defenders.Should().OnlyContain(
            point => point.X >= TickSetPieces.GoalKickOpponentClearance,
            "the opponents wait outside the area until the ball is in play");
    }

    [Fact]
    public void A_corner_sends_the_headers_into_the_box_and_a_marker_onto_each_of_them()
    {
        var skills = Uniform(Skills(10));

        skills[7] = Skills(10) with { Crossing = 18 };
        skills[9] = Skills(10) with { Heading = 18, JumpingReach = 18 };
        skills[10] = Skills(10) with { Heading = 17, JumpingReach = 17 };

        var scene = new Scene
        {
            Kind = TickMatchPhase.CornerPending,
            Ball = new SpatialPoint(SpatialPitch.PitchLength - TickSetPieces.CornerInset, TickSetPieces.CornerInset),
            TakerSkills = skills,
        };

        var (takers, defenders, plan) = scene.Assign();
        var ball = scene.Ball;

        plan.Taker.Should().Be(7, "the best crosser takes the corner");
        takers[7].Should().Be(ball, "the taker stands over the ball");

        takers[9].Should().Be(new SpatialPoint(TickSetPieces.CornerSixYardX, SpatialPitch.GoalYCenter), "the best header takes the six-yard box");
        takers[10].Should().Be(
            new SpatialPoint(TickSetPieces.CornerNearPostX, SpatialPitch.GoalYMin + TickSetPieces.CornerPostInset),
            "the second header goes to the near post");

        var inBox = takers
            .Where(point => point.X >= SpatialPitch.PitchLength - SpatialPitch.PenaltyBoxWidth
                && point.Y >= SpatialPitch.PenaltyBoxYMin
                && point.Y <= SpatialPitch.PenaltyBoxYMax)
            .ToArray();

        inBox.Should().HaveCount(TickSetPieces.CornerAttackersInBox, "the headers assemble in the penalty area");

        foreach (var attacker in inBox)
        {
            defenders.Should().Contain(
                marker => marker.Y == attacker.Y && marker.X - attacker.X >= 200 && marker.X - attacker.X <= 580,
                "every attacker in the box has a marker goal-side of him");
        }

        defenders[0].X.Should().Be(SpatialPitch.PitchLength - TickSetPieces.CornerKeeperDepth, "the keeper stays on his line");
        defenders[0].Y.Should().Be(SpatialPitch.GoalYMin + TickSetPieces.CornerKeeperInset, "the keeper shades towards the near post");
    }

    [Fact]
    public void A_penalty_puts_every_other_player_behind_the_ball_and_outside_the_area()
    {
        var (takers, defenders, plan) = new Scene
        {
            Kind = TickMatchPhase.PenaltyPending,
            Ball = new SpatialPoint(SpatialPitch.PenaltySpotAwayX, SpatialPitch.PenaltySpotY),
        }.Assign();

        takers[plan.Taker].Should().Be(
            new SpatialPoint(SpatialPitch.PenaltySpotAwayX - TickSetPieces.PenaltyRunUp, SpatialPitch.GoalYCenter));
        defenders[0].Should().Be(
            new SpatialPoint(SpatialPitch.PitchLength - TickSetPieces.PenaltyKeeperDepth, SpatialPitch.GoalYCenter),
            "the keeper stands on his line");

        for (var index = 0; index < takers.Length; index++)
        {
            if (index == plan.Taker)
            {
                continue;
            }

            takers[index].X.Should().BeLessThanOrEqualTo(TickSetPieces.PenaltyRestLimit, "everyone else waits behind the ball");
        }

        for (var index = 1; index < defenders.Length; index++)
        {
            defenders[index].X.Should().BeLessThanOrEqualTo(TickSetPieces.PenaltyRestLimit);
        }
    }

    [Fact]
    public void A_free_kick_in_shooting_range_builds_a_wall_nine_metres_from_the_ball()
    {
        var spot = new SpatialPoint(7_000, 3_500);
        var scene = new Scene { Kind = TickMatchPhase.FreeKickPending, Ball = spot };
        var (takers, defenders, plan) = scene.Assign();

        takers[plan.Taker].X.Should().Be(spot.X - TickSetPieces.TakerStandOff, "the taker stands behind the ball");

        var wall = defenders
            .Where(point => Distance(point, spot) >= TickSetPieces.WallDistance - 3
                && Distance(point, spot) <= TickSetPieces.WallDistance + 90)
            .ToArray();

        wall.Should().HaveCount(TickSetPieces.WallSize, "four men form the wall");
        wall.Should().OnlyContain(man => man.X > spot.X, "the wall stands between the ball and the goal");

        var far = new Scene { Kind = TickMatchPhase.FreeKickPending, Ball = new SpatialPoint(4_000, 3_500) };
        var (_, farDefenders, _) = far.Assign();

        for (var index = 1; index < farDefenders.Length; index++)
        {
            Distance(farDefenders[index], new SpatialPoint(4_000, 3_500))
                .Should().BeGreaterThanOrEqualTo(TickSetPieces.FreeKickClearRadius - 1, "outside shooting range there is no wall");
        }
    }

    [Fact]
    public void A_throw_in_is_taken_from_the_touchline_and_everyone_else_stands_clear()
    {
        var ball = new SpatialPoint(3_000, 0);
        var (takers, defenders, plan) = new Scene { Kind = TickMatchPhase.ThrowInPending, Ball = ball }.Assign();

        takers[plan.Taker].Should().Be(ball, "the thrower stands on the line");

        var nearest = Enumerable
            .Range(1, Shape.Length - 1)
            .OrderBy(index => Distance(Shape[index], ball))
            .First();

        plan.Taker.Should().Be(nearest, "the nearest teammate takes the throw");

        for (var index = 0; index < takers.Length; index++)
        {
            if (index != plan.Taker)
            {
                Distance(takers[index], ball).Should().BeGreaterThanOrEqualTo(TickSetPieces.ThrowClearRadius - 1);
            }
        }

        defenders.Should().OnlyContain(point => Distance(point, ball) >= TickSetPieces.ThrowClearRadius - 1);
    }

    [Fact]
    public void The_away_sides_set_piece_is_the_mirror_of_the_home_sides()
    {
        var home = new Scene().Assign();
        var away = new Scene(false).Assign();

        home.Plan.Should().Be(away.Plan);

        for (var index = 0; index < home.Takers.Length; index++)
        {
            away.Takers[index].Should().Be(Mirror(home.Takers[index]));
            away.Defenders[index].Should().Be(Mirror(home.Defenders[index]));
        }

        var homeCorner = new Scene { Kind = TickMatchPhase.CornerPending, Ball = new SpatialPoint(9_970, 30) };
        var awayCorner = new Scene(false) { Kind = TickMatchPhase.CornerPending, Ball = new SpatialPoint(9_970, 30) };

        var (homePoints, _, homePlan) = homeCorner.Assign();
        var (awayPoints, _, awayPlan) = awayCorner.Assign();

        homePlan.Should().Be(awayPlan);

        for (var index = 0; index < homePoints.Length; index++)
        {
            awayPoints[index].Should().Be(Mirror(homePoints[index]));
        }
    }

    [Fact]
    public void The_kick_off_is_passed_backwards_into_midfield()
    {
        var scene = new Scene();
        var (takers, _, plan) = scene.Assign();
        var (action, receiver, ball, _) = scene.Kick(11);

        action.Should().Be(TickRestartAction.Pass);
        receiver.Should().Be(plan.Partner);
        ball.Mode.Should().Be(TickBallMode.Loose, "a kick-off rolls backwards");
        ball.VelocityX.Should().BeNegative("the ball is played back towards his own goal");
        takers[plan.Partner].X.Should().BeLessThan(SpatialPoint.Center.X, "the partner waits behind the spot");
    }

    [Fact]
    public void A_goal_kick_is_either_rolled_short_to_a_centre_back_or_played_long_downfield()
    {
        var shortBalls = 0;
        var longBalls = 0;

        for (var seed = 1; seed <= 20; seed++)
        {
            var scene = new Scene
            {
                Kind = TickMatchPhase.GoalKickPending,
                Ball = new SpatialPoint(TickSetPieces.GoalAreaDepth, SpatialPitch.GoalYCenter),
            };
            var (action, receiver, ball, _) = scene.Kick((ulong)seed);

            action.Should().BeOneOf(TickRestartAction.Pass, TickRestartAction.LongBall);
            ball.Mode.Should().NotBe(TickBallMode.Controlled, "the goal kick is played");

            if (action == TickRestartAction.Pass)
            {
                FourFourTwo[receiver].Family.Should().Be(MatchPositionFamily.Defence, "the short goal kick finds a split centre-back");
                shortBalls++;
            }
            else
            {
                FourFourTwo[receiver].OwnX.Should().Be(
                    FourFourTwo.Skip(1).Max(spec => spec.OwnX),
                    "the long goal kick is aimed at the furthest-up forward");
                longBalls++;
            }
        }

        shortBalls.Should().BePositive();
        longBalls.Should().BePositive();
    }

    [Fact]
    public void A_corner_is_crossed_into_the_box_to_the_best_header()
    {
        var skills = Uniform(Skills(10));
        skills[9] = Skills(10) with { Heading = 18, JumpingReach = 18 };

        var scene = new Scene
        {
            Kind = TickMatchPhase.CornerPending,
            Ball = new SpatialPoint(SpatialPitch.PitchLength - TickSetPieces.CornerInset, TickSetPieces.CornerInset),
            TakerSkills = skills,
        };

        var (action, receiver, ball, _) = scene.Kick(7);

        action.Should().Be(TickRestartAction.Cross);
        receiver.Should().Be(9, "the best header is the target");
        ball.IsAirborne.Should().BeTrue("a corner is crossed in the air");

        var steps = 0;

        while (ball.UnitZ == 0 && steps++ < 100)
        {
            ball.Step();
        }

        while (ball.UnitZ > 0 && steps++ < 100)
        {
            ball.Step();
        }

        steps.Should().BeLessThan(100, "the cross comes down in a few seconds");
        ball.UnitX.Should().BeGreaterThanOrEqualTo(SpatialPitch.PitchLength - SpatialPitch.PenaltyBoxWidth, "the cross comes down in the box");
        ball.UnitY.Should().BeInRange(SpatialPitch.PenaltyBoxYMin, SpatialPitch.PenaltyBoxYMax);
    }

    [Fact]
    public void A_penalty_is_struck_at_the_goal_mouth()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var scene = new Scene
            {
                Kind = TickMatchPhase.PenaltyPending,
                Ball = new SpatialPoint(SpatialPitch.PenaltySpotAwayX, SpatialPitch.PenaltySpotY),
            };
            var (action, receiver, ball, _) = scene.Kick((ulong)seed);

            action.Should().Be(TickRestartAction.Shot);
            receiver.Should().Be(-1);
            ball.VelocityX.Should().BePositive("the kick is aimed at the away goal");

            var forecast = TickShotStopper.Forecast(
                ball,
                new TickBallPhysics(),
                keeperIsHome: false,
                TickSpatialUnits.ToFixed(SpatialPitch.PitchLength - TickSetPieces.PenaltyKeeperDepth));

            forecast.OnTarget.Should().BeTrue("a penalty at average skill is on target");
        }
    }

    [Fact]
    public void A_free_kick_in_range_is_struck_at_goal_or_delivered_and_out_of_range_is_passed()
    {
        var shots = 0;
        var deliveries = 0;

        for (var seed = 1; seed <= 20; seed++)
        {
            var scene = new Scene
            {
                Kind = TickMatchPhase.FreeKickPending,
                Ball = new SpatialPoint(7_000, 3_500),
            };
            var (action, receiver, ball, _) = scene.Kick((ulong)seed);

            action.Should().BeOneOf(TickRestartAction.Shot, TickRestartAction.Cross);

            if (action == TickRestartAction.Shot)
            {
                receiver.Should().Be(-1);
                shots++;
            }
            else
            {
                ball.IsAirborne.Should().BeTrue("the delivery is crossed into the box");
                deliveries++;
            }
        }

        shots.Should().BePositive();
        deliveries.Should().BePositive();

        var (farAction, _, _, _) = new Scene
        {
            Kind = TickMatchPhase.FreeKickPending,
            Ball = new SpatialPoint(4_000, 3_500),
        }.Kick(7);

        farAction.Should().Be(TickRestartAction.Pass, "a free kick out of range is played simply");
    }

    [Fact]
    public void A_throw_in_finds_the_nearest_teammate()
    {
        var ball = new SpatialPoint(3_000, 0);
        var scene = new Scene { Kind = TickMatchPhase.ThrowInPending, Ball = ball };
        var (action, receiver, thrown, _) = scene.Kick(5);

        action.Should().Be(TickRestartAction.Throw);
        receiver.Should().BePositive();
        thrown.Mode.Should().Be(TickBallMode.Loose);
        thrown.VelocityY.Should().BePositive("the throw goes back into the pitch");
    }

    [Fact]
    public void A_set_piece_is_repeatable_and_allocates_nothing()
    {
        var scene = new Scene
        {
            Kind = TickMatchPhase.CornerPending,
            Ball = new SpatialPoint(SpatialPitch.PitchLength - TickSetPieces.CornerInset, TickSetPieces.CornerInset),
        };

        var first = scene.Assign();
        var second = scene.Assign();

        second.Plan.Should().Be(first.Plan);
        second.Takers.Should().Equal(first.Takers);
        second.Defenders.Should().Equal(first.Defenders);

        var takers = new SpatialPoint[Shape.Length];
        var defenders = new SpatialPoint[OppositeShape.Length];
        var situation = scene.Situation();

        TickSetPieces.Assign(situation, takers, defenders);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            TickSetPieces.Assign(situation, takers, defenders);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(256, "a set-piece picture must not allocate");
    }

    private static double Distance(SpatialPoint one, SpatialPoint other)
    {
        long dx = (long)one.X - other.X;
        long dy = (long)one.Y - other.Y;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static SpatialPoint Mirror(SpatialPoint point) =>
        new(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);

    private static TickPlayerSkills Skills(int value) =>
        TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(value, MatchAttributeNames.Count).ToArray()));

    private static TickPlayerSkills[] Uniform(TickPlayerSkills skills) => Enumerable.Repeat(skills, 11).ToArray();

    /// <summary>
    /// One dead ball: the taking side, the defending side, the ball and the restart kind, everything in the taking side's
    /// own point of view.
    /// </summary>
    private sealed class Scene
    {
        private readonly bool _isHome;
        private TickPlayerState[]? _takerStates;
        private TickPlayerState[]? _defenderStates;
        private TickBallPhysics? _ball;

        public Scene(bool isHome = true)
        {
            _isHome = isHome;
        }

        public TickMatchPhase Kind { get; init; } = TickMatchPhase.KickOffPending;

        public SpatialPoint Ball { get; init; } = SpatialPoint.Center;

        public SpatialPoint[] Takers { get; init; } = Shape;

        public SpatialPoint[] Defenders { get; init; } = OppositeShape;

        public TickAnchorSpec[] TakerSpecs { get; init; } = FourFourTwo;

        public TickAnchorSpec[] DefenderSpecs { get; init; } = FourFourTwo;

        public TickPlayerSkills[] TakerSkills { get; init; } = Uniform(Skills(10));

        public TickPlayerSkills[] DefenderSkills { get; init; } = Uniform(Skills(10));

        public TickRestartSituation Situation()
        {
            if (_takerStates is null)
            {
                _takerStates = new TickPlayerState[Takers.Length];
                _defenderStates = new TickPlayerState[Defenders.Length];

                for (var index = 0; index < Takers.Length; index++)
                {
                    var point = Mirror(Takers[index]);

                    _takerStates[index] = TickPlayerState.Standing(point.X, point.Y, 0, 10_000);
                }

                for (var index = 0; index < Defenders.Length; index++)
                {
                    var point = Mirror(Defenders[index]);

                    _defenderStates[index] = TickPlayerState.Standing(point.X, point.Y, TickTrigonometry.HalfTurn, 10_000);
                }

                _ball = new TickBallPhysics();

                var spot = Mirror(Ball);

                _ball.PlaceAt(spot.X, spot.Y);
            }

            return new TickRestartSituation
            {
                Kind = Kind,
                IsHome = _isHome,
                Ball = Mirror(Ball),
                Takers = _takerStates,
                TakerSpecs = TakerSpecs,
                TakerSkills = TakerSkills,
                Defenders = _defenderStates!,
                DefenderSpecs = DefenderSpecs,
                DefenderSkills = DefenderSkills,
            };
        }

        public (SpatialPoint[] Takers, SpatialPoint[] Defenders, TickRestartPlan Plan) Assign()
        {
            var takers = new SpatialPoint[Takers.Length];
            var defenders = new SpatialPoint[Defenders.Length];
            var plan = TickSetPieces.Assign(Situation(), takers, defenders);

            return (takers, defenders, plan);
        }

        public (TickRestartAction Action, int Receiver, TickBallPhysics Ball, TickRestartPlan Plan) Kick(ulong seed = 7)
        {
            var (takers, defenders, plan) = Assign();

            // The whistle has held: the players have walked into their set-piece places.
            for (var index = 0; index < _takerStates!.Length; index++)
            {
                Place(ref _takerStates[index], takers[index]);
                Place(ref _defenderStates![index], defenders[index]);
            }

            var action = TickSetPieces.Kick(Situation(), plan, _ball!, new Pcg32(seed), out var receiver);

            return (action, receiver, _ball!, plan);
        }

        private static void Place(ref TickPlayerState player, SpatialPoint point)
        {
            player.X = TickSpatialUnits.ToFixed(point.X);
            player.Y = TickSpatialUnits.ToFixed(point.Y);
        }

        private SpatialPoint Mirror(SpatialPoint point) =>
            _isHome ? point : new SpatialPoint(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);
    }
}
