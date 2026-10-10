using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's defensive AI: one presser instead of a swarm, markers goal-side in the defensive third,
/// cover shadows in the passing lanes, a back line that moves as one, and the offside it sets (Milestone 3).
/// </summary>
public sealed class TickDefensiveAITests
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

    /// <summary>The attacking side's winger has the ball down the left, with two strikers waiting in the box.</summary>
    private static readonly SpatialPoint[] WingerAtTheByline =
    [
        new(9_500, 3_500),
        new(6_500, 1_200),
        new(6_500, 2_600),
        new(6_500, 4_400),
        new(6_500, 5_800),
        new(1_800, 700),
        new(3_800, 2_200),
        new(3_900, 4_600),
        new(3_800, 5_800),
        new(1_300, 2_800),
        new(1_400, 4_100),
    ];

    [Fact]
    public void When_the_opposition_winger_has_the_ball_only_the_fullback_presses_while_the_centre_backs_mark_the_strikers()
    {
        var scene = new Scene(isHome: true, MatchPressing.MidBlock, 1_800, 700, WingerAtTheByline, carrier: 5);

        var orders = scene.Assign();

        Roles(orders, TickDefensiveRole.Presser).Should().Equal(1);
        Roles(orders, TickDefensiveRole.SupportPresser).Should().BeEmpty();
        Roles(orders, TickDefensiveRole.Marker).Should().BeEquivalentTo(new[] { 2, 3 });
        orders[2].Opponent.Should().Be(9);
        orders[3].Opponent.Should().Be(10);
        orders[4].Role.Should().Be(TickDefensiveRole.Line, "the far fullback holds the line");
        orders[0].Role.Should().Be(TickDefensiveRole.Keeper);

        // The markers stand between their man and the goal they defend (X = 0 for the home side).
        orders[2].Target.X.Should().BeLessThan(scene.Attackers[9].X / TickSpatialUnits.FixedScale);
        orders[3].Target.X.Should().BeLessThan(scene.Attackers[10].X / TickSpatialUnits.FixedScale);
    }

    [Theory]
    [InlineData(MatchPressing.LowBlock)]
    [InlineData(MatchPressing.MidBlock)]
    [InlineData(MatchPressing.HighPress)]
    public void However_the_play_runs_nobody_but_the_presser_is_sent_at_the_ball(MatchPressing pressing)
    {
        var maximum = 0;

        foreach (var ballX in new[] { 500, 1_800, 3_000, 5_000, 7_000, 9_000 })
        {
            foreach (var ballY in new[] { 200, 1_500, 3_500, 5_500, 6_800 })
            {
                var attackers = (SpatialPoint[])WingerAtTheByline.Clone();

                attackers[5] = new SpatialPoint(ballX, ballY);

                var orders = new Scene(isHome: true, pressing, ballX, ballY, attackers, carrier: 5).Assign();
                var pressers = Roles(orders, TickDefensiveRole.Presser).Count();
                var supports = Roles(orders, TickDefensiveRole.SupportPresser).Count();

                pressers.Should().BeLessThanOrEqualTo(1);
                supports.Should().BeLessThanOrEqualTo(pressing == MatchPressing.HighPress ? 1 : 0);

                if (supports > 0)
                {
                    pressers.Should().Be(1, "the trap needs a first presser");
                    Math.Min(ballY, SpatialPitch.PitchWidth - ballY).Should().BeLessThanOrEqualTo(TickDefensiveAI.TrapDistance);
                }

                maximum = Math.Max(maximum, pressers + supports);
            }
        }

        maximum.Should().BeLessThanOrEqualTo(pressing == MatchPressing.HighPress ? 2 : 1);
    }

    [Fact]
    public void The_pressing_instruction_sets_how_far_from_the_ball_a_defender_is_sent()
    {
        // A midfielder stands 15 m (1,500 units) from the ball, in the defender's own half.
        bool Presses(MatchPressing pressing, int distance, int ballX = 3_000)
        {
            var attackers = (SpatialPoint[])WingerAtTheByline.Clone();

            attackers[5] = new SpatialPoint(ballX, 3_500);

            var scene = new Scene(isHome: true, pressing, ballX, 3_500, attackers, carrier: 5);

            // Everyone is sent out of range, then the midfielder is put at the distance.
            for (var index = 1; index < scene.Defenders.Length; index++)
            {
                scene.Defenders[index] = TickPlayerState.Standing(10_000, 0, 0, 10_000);
            }

            scene.Defenders[6] = TickPlayerState.Standing(ballX + distance, 3_500, 0, 10_000);

            return Roles(scene.Assign(), TickDefensiveRole.Presser).Contains(6);
        }

        Presses(MatchPressing.LowBlock, 1_100).Should().BeTrue();
        Presses(MatchPressing.LowBlock, 1_500).Should().BeFalse("a low block closes down from 12 m");
        Presses(MatchPressing.MidBlock, 1_900).Should().BeTrue();
        Presses(MatchPressing.MidBlock, 2_500).Should().BeFalse("a mid block closes down from 20 m");
        Presses(MatchPressing.HighPress, 3_100).Should().BeTrue();
        Presses(MatchPressing.HighPress, 3_500).Should().BeFalse("a high press closes down from 32 m");

        // How high up the pitch each one goes: a low block stays in its own half, a mid block out of the final third, a high press out of the opponent's box.
        Presses(MatchPressing.LowBlock, 800, ballX: 6_000).Should().BeFalse();
        Presses(MatchPressing.MidBlock, 800, ballX: 6_000).Should().BeTrue();
        Presses(MatchPressing.MidBlock, 800, ballX: 8_000).Should().BeFalse();
        Presses(MatchPressing.HighPress, 800, ballX: 7_500).Should().BeTrue();
        Presses(MatchPressing.HighPress, 800, ballX: 9_000).Should().BeFalse("a high press starts at the edge of the box, not at the goal kick");
    }

    [Fact]
    public void The_goalkeeper_never_presses_and_a_defender_off_balance_is_passed_over()
    {
        var scene = new Scene(isHome: true, MatchPressing.HighPress, 1_800, 3_500, WingerAtTheByline, carrier: 5);

        scene.Defenders[0] = TickPlayerState.Standing(1_700, 3_500, 0, 10_000);
        scene.Defenders[2] = TickPlayerState.Standing(2_000, 3_500, 0, 10_000);
        scene.Defenders[3] = TickPlayerState.Standing(2_400, 3_500, 0, 10_000);

        Roles(scene.Assign(), TickDefensiveRole.Presser).Should().Equal(new[] { 2 }, "the keeper is nearest but is not a presser");

        var stumbled = scene.Defenders[2];

        stumbled.Lockout = 5;
        scene.Defenders[2] = stumbled;

        Roles(scene.Assign(), TickDefensiveRole.Presser).Should().Equal(new[] { 3 }, "the beaten defender is out of the play");
    }

    [Fact]
    public void The_defender_already_pressing_keeps_the_job_unless_another_is_clearly_closer()
    {
        var scene = new Scene(isHome: true, MatchPressing.MidBlock, 3_000, 3_500, WingerAtTheByline, carrier: 5);

        for (var index = 1; index < scene.Defenders.Length; index++)
        {
            scene.Defenders[index] = TickPlayerState.Standing(10_000, 0, 0, 10_000);
        }

        scene.Defenders[2] = TickPlayerState.Standing(3_000, 4_000, 0, 10_000);
        scene.Defenders[3] = TickPlayerState.Standing(3_000, 3_000, 0, 10_000);

        scene.Previous = 2;
        Roles(scene.Assign(), TickDefensiveRole.Presser).Should().Equal(new[] { 2 }, "equally near, he keeps it");

        scene.Defenders[3] = TickPlayerState.Standing(3_000, 3_200, 0, 10_000);

        Roles(scene.Assign(), TickDefensiveRole.Presser).Should().Equal(new[] { 3 }, "now the other is a good deal nearer");
    }

    [Fact]
    public void A_high_press_traps_the_ball_on_the_touchline_with_a_second_player_cutting_the_inside()
    {
        var attackers = (SpatialPoint[])WingerAtTheByline.Clone();

        attackers[5] = new SpatialPoint(5_000, 400);

        var trap = new Scene(isHome: true, MatchPressing.HighPress, 5_000, 400, attackers, carrier: 5).Assign();
        var mid = new Scene(isHome: true, MatchPressing.MidBlock, 5_000, 400, attackers, carrier: 5).Assign();

        Roles(trap, TickDefensiveRole.Presser).Count().Should().Be(1);
        Roles(trap, TickDefensiveRole.SupportPresser).Count().Should().Be(1);
        Roles(mid, TickDefensiveRole.SupportPresser).Should().BeEmpty("only a high press springs the trap");

        var support = trap[Roles(trap, TickDefensiveRole.SupportPresser).Single()];

        support.Target.Y.Should().BeGreaterThan(400, "he stands on the inside of the ball, away from the touchline");
        support.Target.X.Should().Be(5_000);

        var central = (SpatialPoint[])WingerAtTheByline.Clone();

        central[5] = new SpatialPoint(5_000, 3_500);

        Roles(new Scene(isHome: true, MatchPressing.HighPress, 5_000, 3_500, central, carrier: 5).Assign(), TickDefensiveRole.SupportPresser)
            .Should().BeEmpty("in the middle of the pitch there is nothing to trap the ball against");
    }

    [Fact]
    public void A_marker_stands_closer_to_his_man_the_better_his_Marking_and_always_between_him_and_the_goal()
    {
        int StandOff(int marking)
        {
            var scene = new Scene(isHome: true, MatchPressing.MidBlock, 1_800, 700, WingerAtTheByline, carrier: 5);

            scene.Skills[2] = scene.Skills[2] with { Marking = marking };

            var order = scene.Assign()[2];
            var man = scene.Attackers[order.Opponent];
            var dx = (man.X / TickSpatialUnits.FixedScale) - order.Target.X;
            var dy = (man.Y / TickSpatialUnits.FixedScale) - order.Target.Y;

            order.Role.Should().Be(TickDefensiveRole.Marker);
            order.Target.X.Should().BeLessThan(man.X / TickSpatialUnits.FixedScale, "goal-side");

            return (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));
        }

        var tight = StandOff(20);
        var loose = StandOff(1);

        tight.Should().BeLessThan(300, "an elite marker is within 3 m");
        loose.Should().BeGreaterThan(tight + 200);
    }

    [Fact]
    public void Attackers_outside_the_defensive_third_are_not_marked_and_nobody_marks_the_man_on_the_ball()
    {
        var attackers = (SpatialPoint[])WingerAtTheByline.Clone();

        // The strikers have pulled out of the box and are standing 40 m from goal.
        attackers[9] = new SpatialPoint(4_200, 2_800);
        attackers[10] = new SpatialPoint(4_300, 4_100);

        var orders = new Scene(isHome: true, MatchPressing.MidBlock, 1_800, 700, attackers, carrier: 5).Assign();

        Roles(orders, TickDefensiveRole.Marker).Should().BeEmpty();

        // The carrier himself is in the defensive third but is the presser's, not a marker's.
        orders.Where(order => order.Role == TickDefensiveRole.Marker).Select(order => order.Opponent).Should().NotContain(5);
    }

    [Fact]
    public void The_pairs_picked_do_not_depend_on_the_order_the_players_are_listed_in()
    {
        var forward = new Scene(isHome: true, MatchPressing.MidBlock, 1_800, 700, WingerAtTheByline, carrier: 5);
        var reversed = forward.WithBackLineReversed();

        var one = forward.Assign();
        var other = reversed.Assign();

        // The back line is indices 1..4; the reversed scene lists them 4..1.
        for (var index = 1; index <= 4; index++)
        {
            var mirror = other[5 - index];

            mirror.Role.Should().Be(one[index].Role);
            mirror.Opponent.Should().Be(one[index].Opponent);
            mirror.Target.Should().Be(one[index].Target);
        }
    }

    [Fact]
    public void Midfielders_stand_in_the_passing_lanes_and_follow_them_the_more_the_better_their_Positioning()
    {
        // The carrier in the middle of the pitch with three runners ahead of him towards the defender's goal.
        var attackers = new[]
        {
            new SpatialPoint(9_500, 3_500),
            new SpatialPoint(8_000, 1_000),
            new SpatialPoint(8_000, 6_000),
            new SpatialPoint(8_000, 3_500),
            new SpatialPoint(8_500, 5_000),
            new SpatialPoint(5_000, 3_500),
            new SpatialPoint(2_800, 1_800),
            new SpatialPoint(3_000, 5_200),
            new SpatialPoint(8_500, 2_000),
            new SpatialPoint(7_500, 3_000),
            new SpatialPoint(7_500, 4_300),
        };

        var scene = new Scene(isHome: true, MatchPressing.LowBlock, 5_000, 3_500, attackers, carrier: 5);

        scene.Skills[5] = scene.Skills[5] with { Positioning = 20 };
        scene.Skills[6] = scene.Skills[6] with { Positioning = 1 };
        scene.Skills[7] = scene.Skills[7] with { Positioning = 20 };

        var orders = scene.Assign();
        var screens = Roles(orders, TickDefensiveRole.Screen).ToArray();

        screens.Should().NotBeEmpty();
        screens.Length.Should().BeLessThanOrEqualTo(TickDefensiveAI.MaximumScreens);
        screens.Select(index => orders[index].Opponent).Should().OnlyHaveUniqueItems();

        foreach (var index in screens)
        {
            var order = orders[index];
            var receiver = scene.Attackers[order.Opponent];
            var anchor = scene.Anchors[index];

            // The screen lies on the pull from his anchor to a point on the lane: nearer the lane the better he is.
            var laneDistance = DistanceToSegment(
                order.Target,
                new SpatialPoint(5_000, 3_500),
                new SpatialPoint(receiver.X / TickSpatialUnits.FixedScale, receiver.Y / TickSpatialUnits.FixedScale));
            var anchorDistance = DistanceToSegment(
                anchor,
                new SpatialPoint(5_000, 3_500),
                new SpatialPoint(receiver.X / TickSpatialUnits.FixedScale, receiver.Y / TickSpatialUnits.FixedScale));

            laneDistance.Should().BeLessThan(anchorDistance, "he moves into the lane");

            if (scene.Skills[index].Positioning == 20)
            {
                laneDistance.Should().BeLessThan(40, "full commitment puts him on the lane");
            }
        }

        // Two players in the same job: the sharper one ends up closer to the lane than the sloppy one would.
        var sharp = new Scene(isHome: true, MatchPressing.LowBlock, 5_000, 3_500, attackers, carrier: 5);
        var sloppy = new Scene(isHome: true, MatchPressing.LowBlock, 5_000, 3_500, attackers, carrier: 5);

        foreach (var index in screens)
        {
            sharp.Skills[index] = sharp.Skills[index] with { Positioning = 20 };
            sloppy.Skills[index] = sloppy.Skills[index] with { Positioning = 1 };
        }

        var sharpOrders = sharp.Assign();
        var sloppyOrders = sloppy.Assign();

        foreach (var index in screens)
        {
            var receiver = sharp.Attackers[sharpOrders[index].Opponent];
            var end = new SpatialPoint(receiver.X / TickSpatialUnits.FixedScale, receiver.Y / TickSpatialUnits.FixedScale);
            var start = new SpatialPoint(5_000, 3_500);

            DistanceToSegment(sharpOrders[index].Target, start, end)
                .Should().BeLessThanOrEqualTo(DistanceToSegment(sloppyOrders[index].Target, start, end));
        }
    }

    [Fact]
    public void A_screen_is_never_cast_on_a_receiver_in_the_presser_s_face_or_out_of_range()
    {
        var attackers = (SpatialPoint[])WingerAtTheByline.Clone();

        // One teammate 5 m from the ball, one 60 m away; the rest are marked or in the box.
        attackers[6] = new SpatialPoint(2_200, 900);
        attackers[7] = new SpatialPoint(9_000, 6_500);
        attackers[8] = new SpatialPoint(9_000, 6_000);

        var orders = new Scene(isHome: true, MatchPressing.MidBlock, 1_800, 700, attackers, carrier: 5).Assign();

        orders.Where(order => order.Role == TickDefensiveRole.Screen).Select(order => order.Opponent)
            .Should().NotContain(new[] { 6, 7, 8 });
    }

    [Fact]
    public void The_back_line_holds_one_height_steps_up_when_the_carrier_is_closed_down_and_drops_when_he_has_time()
    {
        var far = new SpatialPoint[11];

        far[0] = new SpatialPoint(9_500, 3_500);

        for (var index = 1; index < far.Length; index++)
        {
            far[index] = new SpatialPoint(7_000 + (index * 100), 500 + (index * 500));
        }

        far[5] = new SpatialPoint(5_000, 3_500);

        // Closed down: a midfielder is 2 m from the ball.
        var closed = new Scene(isHome: true, MatchPressing.MidBlock, 5_000, 3_500, far, carrier: 5);

        closed.Defenders[6] = TickPlayerState.Standing(4_800, 3_500, 0, 10_000);

        // Free: nobody in the side is within 12 m of a ball that is 64 m from the defender's goal line.
        var freeBall = new SpatialPoint[11];

        far.CopyTo(freeBall, 0);
        freeBall[5] = new SpatialPoint(6_400, 3_500);

        var free = new Scene(isHome: true, MatchPressing.MidBlock, 6_400, 3_500, freeBall, carrier: 5);

        for (var index = 5; index < free.Defenders.Length; index++)
        {
            free.Defenders[index] = TickPlayerState.Standing(1_000, 3_500, 0, 10_000);
        }

        var meanClosed = (int)closed.Anchors.Skip(1).Take(4).Average(anchor => anchor.X);
        var meanFree = (int)free.Anchors.Skip(1).Take(4).Average(anchor => anchor.X);

        var closedLine = closed.Assign().Where(order => order.Role == TickDefensiveRole.Line).ToArray();
        var freeLine = free.Assign().Where(order => order.Role == TickDefensiveRole.Line).ToArray();

        closedLine.Should().HaveCount(4);
        closedLine.Select(order => order.Target.X).Distinct().Should().ContainSingle("the line is flat");
        closedLine[0].Target.X.Should().BeInRange(meanClosed + 100, meanClosed + 150, "Decisions 10 steps it up half the squeeze");
        freeLine.Should().HaveCount(4);
        freeLine.Select(order => order.Target.X).Distinct().Should().ContainSingle();
        freeLine[0].Target.X.Should().BeInRange(meanFree - 301, meanFree - 299, "it drops 3 m when he has time");

        // Each keeps the width of his own position.
        freeLine.Select(order => order.Target.Y).Should().Equal(free.Anchors.Skip(1).Take(4).Select(anchor => anchor.Y));
    }

    [Fact]
    public void The_back_line_never_goes_beyond_the_halfway_line_or_into_its_own_six_yard_box()
    {
        foreach (var isHome in new[] { true, false })
        {
            foreach (var line in new[] { MatchDefensiveLine.Deep, MatchDefensiveLine.High })
            {
                foreach (var ballX in new[] { 0, 5_000, 10_000 })
                {
                    var attackers = (SpatialPoint[])WingerAtTheByline.Clone();

                    for (var index = 0; index < attackers.Length; index++)
                    {
                        attackers[index] = new SpatialPoint(9_000 - (index * 100), 500 + (index * 500));
                    }

                    var scene = new Scene(isHome, MatchPressing.MidBlock, ballX, 3_500, attackers, carrier: 5, line);

                    foreach (var order in scene.Assign().Where(order => order.Role == TickDefensiveRole.Line))
                    {
                        var own = isHome ? order.Target.X : SpatialPitch.PitchLength - order.Target.X;

                        own.Should().BeInRange(TickDefensiveAI.LineFloor, TickDefensiveAI.LineCeiling);
                    }
                }
            }
        }
    }

    [Fact]
    public void The_away_side_defends_the_same_way_mirrored()
    {
        var home = new Scene(isHome: true, MatchPressing.MidBlock, 1_800, 700, WingerAtTheByline, carrier: 5);
        var away = new Scene(
            isHome: false,
            MatchPressing.MidBlock,
            SpatialPitch.PitchLength - 1_800,
            SpatialPitch.PitchWidth - 700,
            [.. WingerAtTheByline.Select(point => new SpatialPoint(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y))],
            carrier: 5);

        var homeOrders = home.Assign();
        var awayOrders = away.Assign();

        for (var index = 0; index < homeOrders.Length; index++)
        {
            awayOrders[index].Role.Should().Be(homeOrders[index].Role, $"player {index}");
            awayOrders[index].Opponent.Should().Be(homeOrders[index].Opponent);
            awayOrders[index].Target.X.Should().Be(SpatialPitch.PitchLength - homeOrders[index].Target.X);
            awayOrders[index].Target.Y.Should().Be(SpatialPitch.PitchWidth - homeOrders[index].Target.Y);
        }
    }

    [Fact]
    public void With_the_ball_loose_the_nearest_defender_goes_for_it_and_nobody_is_screened()
    {
        var orders = new Scene(isHome: true, MatchPressing.MidBlock, 1_800, 700, WingerAtTheByline, carrier: -1).Assign();

        Roles(orders, TickDefensiveRole.Presser).Should().Equal(1);
        Roles(orders, TickDefensiveRole.Screen).Should().BeEmpty();
        orders[1].Opponent.Should().Be(-1);
    }

    [Fact]
    public void Offside_is_set_by_the_second_last_defender_with_the_goalkeeper_counting_as_one()
    {
        var defenders = DefendingLine(isHome: true, lastOutfield: 2_000);

        TickDefensiveAI.OffsideLine(defenders, defendersAreHome: true).Should().Be(TickSpatialUnits.ToFixed(2_000));

        // A striker 5 m beyond the line, with the ball well behind him, is offside.
        Offside(defenders, true, receiver: 1_500, ball: 5_500).Should().BeTrue();

        // Onside: behind the line, level with it, behind the ball, or in his own half.
        Offside(defenders, true, receiver: 2_100, ball: 5_500).Should().BeFalse();
        Offside(defenders, true, receiver: 2_000, ball: 5_500).Should().BeFalse("level is onside");
        Offside(defenders, true, receiver: 1_500, ball: 1_200).Should().BeFalse("he is not nearer the goal line than the ball");
        Offside(defenders, true, receiver: 1_500, ball: 1_500).Should().BeFalse("level with the ball is onside");

        // A keeper who has come off his line is no longer the last man: the line is then the second of the back four.
        var sweeper = DefendingLine(isHome: true, lastOutfield: 2_000);

        sweeper[0] = TickPlayerState.Standing(2_500, 3_500, 0, 10_000);
        TickDefensiveAI.OffsideLine(sweeper, true).Should().Be(TickSpatialUnits.ToFixed(2_100));

        // With a single man left the law has no second-last defender to measure from.
        TickDefensiveAI.OffsideLine(defenders.AsSpan(0, 1), true).Should().Be(TickSpatialUnits.PitchLengthFixed);
    }

    [Fact]
    public void Nobody_is_offside_in_his_own_half_and_the_law_is_the_same_for_the_away_side_mirrored()
    {
        // A line pushed past the halfway line: the receiver is only offside once he is in the opponent's half.
        var high = new TickPlayerState[11];

        for (var index = 0; index < high.Length; index++)
        {
            high[index] = TickPlayerState.Standing(6_600 + (index * 10), 1_000 + (index * 500), 0, 10_000);
        }

        Offside(high, true, receiver: 5_500, ball: 9_000).Should().BeFalse("he is in his own half");
        Offside(high, true, receiver: 4_800, ball: 9_000).Should().BeTrue();

        var away = DefendingLine(isHome: false, lastOutfield: 2_000);

        TickDefensiveAI.OffsideLine(away, defendersAreHome: false).Should().Be(TickSpatialUnits.ToFixed(2_000));
        Offside(away, false, receiver: SpatialPitch.PitchLength - 1_500, ball: SpatialPitch.PitchLength - 5_500).Should().BeTrue();
        Offside(away, false, receiver: SpatialPitch.PitchLength - 2_100, ball: SpatialPitch.PitchLength - 5_500).Should().BeFalse();
        Offside(away, false, receiver: SpatialPitch.PitchLength - 1_500, ball: SpatialPitch.PitchLength - 1_200).Should().BeFalse();
    }

    [Fact]
    public void A_through_ball_to_an_offside_striker_puts_an_offside_on_the_log_against_the_attacking_side()
    {
        var state = TickTestMatchState.Create();
        var receiver = state.Away.Outfield.First(slot => slot.Slot.Family == MatchPositionFamily.Attack).Participant.ParticipantId;
        var passer = state.Away.Outfield.First(slot => slot.Slot.Family == MatchPositionFamily.Midfield).Participant.ParticipantId;
        var defenders = DefendingLine(isHome: true, lastOutfield: 2_000);

        TickDefensiveAI.IsOffside(
            TickSpatialUnits.ToFixed(1_500),
            TickSpatialUnits.ToFixed(5_500),
            defenders,
            defendersAreHome: true).Should().BeTrue();

        TickDefensiveAI.FlagOffside(state, MatchSide.Away, receiver, passer);

        var flagged = state.Events.Should().ContainSingle(matchEvent => matchEvent.Type == EngineEventType.Offside).Subject;

        flagged.Side.Should().Be(MatchSide.Away);
        flagged.ParticipantId.Should().Be(receiver);
        flagged.SecondaryParticipantId.Should().Be(passer);
    }

    [Fact]
    public void A_whole_match_of_pressing_and_steering_is_deterministic_and_allocates_nothing()
    {
        var homeStyle = TickTeamStyle.From(new MatchInstructionsV1 { Pressing = MatchPressing.HighPress });
        var awayStyle = TickTeamStyle.From(new MatchInstructionsV1 { Pressing = MatchPressing.LowBlock });
        var profiles = Profiles();
        var skills = Skills();
        var homeAnchors = new SpatialPoint[11];
        var awayAnchors = new SpatialPoint[11];
        var targets = new SpatialPoint[11];
        var paces = new int[11];

        int Run(TickPlayerState[] home, TickPlayerState[] away)
        {
            var hash = 17;
            var homePresser = -1;
            var awayPresser = -1;

            // Once, outside the loop: a stackalloc in a loop would grow the stack on every pass.
            Span<TickDefensiveOrder> orders = stackalloc TickDefensiveOrder[11];

            for (var tick = 0; tick < 54_000; tick++)
            {
                var homeHasBall = (tick / 200) % 2 == 0;
                var attackers = homeHasBall ? home : away;
                var carrier = attackers[6];
                var ballX = TickSpatialUnits.ToUnits(carrier.X);
                var ballY = TickSpatialUnits.ToUnits(carrier.Y);

                TickTacticalGeometry.ResolveTeam(FourFourTwo, homeStyle, true, homeHasBall, ballX, ballY, homeAnchors);
                TickTacticalGeometry.ResolveTeam(FourFourTwo, awayStyle, false, !homeHasBall, ballX, ballY, awayAnchors);

                if (homeHasBall)
                {
                    var situation = new TickDefensiveSituation
                    {
                        IsHome = false,
                        Pressing = MatchPressing.LowBlock,
                        Defenders = away,
                        Specs = FourFourTwo,
                        Anchors = awayAnchors,
                        Skills = skills,
                        Attackers = home,
                        BallX = ballX,
                        BallY = ballY,
                        CarrierIndex = 6,
                        PreviousPresser = awayPresser,
                    };

                    TickDefensiveAI.Assign(situation, orders);
                    awayPresser = Presser(orders);
                    TickDefensiveAI.ToSteering(orders, targets, paces);
                    TickSteering.StepTeam(away, profiles, targets, paces);
                    TickSteering.StepTeam(home, profiles, homeAnchors);
                }
                else
                {
                    var situation = new TickDefensiveSituation
                    {
                        IsHome = true,
                        Pressing = MatchPressing.HighPress,
                        Defenders = home,
                        Specs = FourFourTwo,
                        Anchors = homeAnchors,
                        Skills = skills,
                        Attackers = away,
                        BallX = ballX,
                        BallY = ballY,
                        CarrierIndex = 6,
                        PreviousPresser = homePresser,
                    };

                    TickDefensiveAI.Assign(situation, orders);
                    homePresser = Presser(orders);
                    TickDefensiveAI.ToSteering(orders, targets, paces);
                    TickSteering.StepTeam(home, profiles, targets, paces);
                    TickSteering.StepTeam(away, profiles, awayAnchors);
                }

                hash = unchecked((hash * 31) + home[tick % 11].X);
                hash = unchecked((hash * 31) + away[tick % 11].Y);
                hash = unchecked((hash * 31) + homePresser + (awayPresser * 11));
            }

            return hash;
        }

        var first = Run(Kickoff(true), Kickoff(false));
        var home = Kickoff(true);
        var away = Kickoff(false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run(home, away);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);
        allocated.Should().BeLessThan(256, "two sides for 54,000 ticks must not allocate");
    }

    private static int Presser(ReadOnlySpan<TickDefensiveOrder> orders)
    {
        for (var index = 0; index < orders.Length; index++)
        {
            if (orders[index].Role == TickDefensiveRole.Presser)
            {
                return index;
            }
        }

        return -1;
    }

    private static IEnumerable<int> Roles(TickDefensiveOrder[] orders, TickDefensiveRole role) =>
        orders.Select((order, index) => (order, index)).Where(pair => pair.order.Role == role).Select(pair => pair.index);

    private static bool Offside(TickPlayerState[] defenders, bool defendersAreHome, int receiver, int ball) =>
        TickDefensiveAI.IsOffside(TickSpatialUnits.ToFixed(receiver), TickSpatialUnits.ToFixed(ball), defenders, defendersAreHome);

    /// <summary>A defending side with its keeper on the line and four defenders on one height, the rest further up.</summary>
    private static TickPlayerState[] DefendingLine(bool isHome, int lastOutfield)
    {
        // In the side's own point of view (0 is its own goal line), then mirrored for the away side.
        var xs = new[] { 400, lastOutfield, lastOutfield + 100, lastOutfield + 200, lastOutfield + 300, 4_500, 4_500, 4_500, 4_500, 6_000, 6_000 };

        return
        [
            .. xs.Select((x, index) => TickPlayerState.Standing(
                isHome ? x : SpatialPitch.PitchLength - x,
                1_000 + (index * 500),
                0,
                10_000)),
        ];
    }

    private static TickPlayerState[] Kickoff(bool isHome) =>
        [
            .. FourFourTwo.Select(spec => TickPlayerState.Standing(
                isHome ? spec.OwnX : SpatialPitch.PitchLength - spec.OwnX,
                isHome ? spec.OwnY : SpatialPitch.PitchWidth - spec.OwnY,
                isHome ? 0 : TickTrigonometry.HalfTurn,
                10_000)),
        ];

    private static TickPlayerProfile[] Profiles() =>
        [.. Enumerable.Repeat(TickPlayerProfile.From(PlayerAttributesV1.From(Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray())), 11)];

    private static TickPlayerSkills[] Skills() =>
        [.. Enumerable.Repeat(TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray())), 11)];

    /// <summary>Gets how far a point is from a segment, in whole units.</summary>
    private static int DistanceToSegment(SpatialPoint point, SpatialPoint start, SpatialPoint end)
    {
        long sx = end.X - start.X;
        long sy = end.Y - start.Y;
        long length2 = (sx * sx) + (sy * sy);
        long along = length2 == 0 ? 0 : Math.Clamp((((point.X - start.X) * sx) + ((point.Y - start.Y) * sy)) * 10_000 / length2, 0, 10_000);
        var nearestX = start.X + (sx * along / 10_000);
        var nearestY = start.Y + (sy * along / 10_000);
        var dx = point.X - nearestX;
        var dy = point.Y - nearestY;

        return (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>One defending side standing on its anchors, and the attackers it faces, with everything the AI reads.</summary>
    private sealed class Scene
    {
        public Scene(
            bool isHome,
            MatchPressing pressing,
            int ballX,
            int ballY,
            SpatialPoint[] attackers,
            int carrier,
            MatchDefensiveLine line = MatchDefensiveLine.Normal)
        {
            IsHome = isHome;
            Pressing = pressing;
            BallX = ballX;
            BallY = ballY;
            Carrier = carrier;

            var style = TickTeamStyle.From(new MatchInstructionsV1 { Pressing = pressing, DefensiveLine = line });

            Specs = FourFourTwo;
            Anchors = new SpatialPoint[Specs.Length];
            TickTacticalGeometry.ResolveTeam(Specs, style, isHome, hasPossession: false, ballX, ballY, Anchors);
            Defenders =
            [
                .. Anchors.Select(anchor => TickPlayerState.Standing(anchor.X, anchor.Y, isHome ? 0 : TickTrigonometry.HalfTurn, 10_000)),
            ];
            Skills = TickDefensiveAITests.Skills();
            Attackers =
            [
                .. attackers.Select(point => TickPlayerState.Standing(point.X, point.Y, isHome ? TickTrigonometry.HalfTurn : 0, 10_000)),
            ];
        }

        public bool IsHome { get; }

        public MatchPressing Pressing { get; }

        public TickAnchorSpec[] Specs { get; private set; }

        public SpatialPoint[] Anchors { get; private set; }

        public TickPlayerState[] Defenders { get; private set; }

        public TickPlayerSkills[] Skills { get; private set; }

        public TickPlayerState[] Attackers { get; }

        public int BallX { get; }

        public int BallY { get; }

        public int Carrier { get; }

        public int Previous { get; set; } = -1;

        /// <summary>Gets the same scene with the four back-line players listed in the opposite order.</summary>
        public Scene WithBackLineReversed()
        {
            var copy = (Scene)MemberwiseClone();

            int[] map = [0, 4, 3, 2, 1, 5, 6, 7, 8, 9, 10];

            copy.Specs = [.. map.Select(index => Specs[index])];
            copy.Anchors = [.. map.Select(index => Anchors[index])];
            copy.Defenders = [.. map.Select(index => Defenders[index])];
            copy.Skills = [.. map.Select(index => Skills[index])];

            return copy;
        }

        public TickDefensiveOrder[] Assign()
        {
            var orders = new TickDefensiveOrder[Defenders.Length];
            var situation = new TickDefensiveSituation
            {
                IsHome = IsHome,
                Pressing = Pressing,
                Defenders = Defenders,
                Specs = Specs,
                Anchors = Anchors,
                Skills = Skills,
                Attackers = Attackers,
                BallX = BallX,
                BallY = BallY,
                CarrierIndex = Carrier,
                PreviousPresser = Previous,
            };

            TickDefensiveAI.Assign(situation, orders);

            return orders;
        }
    }
}
