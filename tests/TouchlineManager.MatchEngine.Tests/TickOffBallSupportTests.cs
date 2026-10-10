using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's attacking support: passing triangles that keep the carrier two clear lanes, runs behind the
/// line into the gaps, checks into the pocket, overlapping full-backs and the offside discipline of the rest (Milestone 4).
/// </summary>
/// <remarks>
/// Every position in a scene is given in the attacking side's own point of view (it attacks towards X = 10,000), whichever
/// end the side really plays, and a scene mirrors them for the away side. The tests read the orders back the same way.
/// </remarks>
public sealed class TickOffBallSupportTests
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

    /// <summary>The attackers: a central midfielder (6) has the ball in the middle of the pitch, the strikers ahead of him.</summary>
    private static readonly SpatialPoint[] CentralMidfielderHasTheBall =
    [
        new(500, 3_500),
        new(2_000, 900),
        new(1_800, 2_500),
        new(1_800, 4_500),
        new(2_000, 6_100),
        new(4_200, 2_200),
        new(4_500, 3_500),
        new(4_200, 4_800),
        new(4_500, 6_100),
        new(7_000, 2_800),
        new(7_000, 4_200),
    ];

    /// <summary>The defenders in the same point of view: a back four at 18 m from goal, a midfield four, two forwards.</summary>
    private static readonly SpatialPoint[] CompactFourFourTwo =
    [
        new(9_500, 3_500),
        new(8_200, 900),
        new(8_200, 2_500),
        new(8_200, 4_500),
        new(8_200, 6_100),
        new(6_500, 900),
        new(6_500, 2_600),
        new(6_500, 4_400),
        new(6_500, 6_100),
        new(6_000, 2_700),
        new(6_000, 4_300),
    ];

    [Fact]
    public void A_carrier_in_the_middle_of_the_pitch_is_given_two_clear_short_options_at_the_planned_distance_and_angle()
    {
        var scene = new Scene(isHome: true, carrier: 6, CentralMidfielderHasTheBall, CompactFourFourTwo, skill: 20);
        var orders = scene.Assign();
        var supports = Indices(orders, TickAttackingRole.Support).ToArray();

        supports.Should().BeEquivalentTo(new[] { 5, 7 }, "the two nearest midfielders, one either side");
        orders[6].Role.Should().Be(TickAttackingRole.Carrier);

        var carrier = scene.Own(orders[6].Target);
        var sides = new List<int>();

        foreach (var index in supports)
        {
            var target = scene.Own(orders[index].Target);
            long dx = target.X - carrier.X;
            long dy = target.Y - carrier.Y;
            var distance = SpatialMath.Sqrt((dx * dx) + (dy * dy));

            distance.Should().BeInRange(1_200, 2_500, "the plan puts a supporter 12 m to 25 m from the ball");
            dx.Should().BePositive("both corners are ahead of the ball");

            // The angle to the line of attack is between 30 and 60 degrees: tan 30 = 0.577, tan 60 = 1.732.
            (Math.Abs(dy) * 1_000).Should().BeInRange(dx * 577, dx * 1_732);
            TickOffBallSupport.LaneClear(scene.Defenders, true, carrier.X, carrier.Y, target.X, target.Y)
                .Should().BeTrue("the lane to a supporter is cleared");
            sides.Add(Math.Sign(dy));
        }

        sides.Should().BeEquivalentTo(new[] { -1, 1 }, "one on each side of the carrier");
    }

    [Fact]
    public void A_supporter_whose_lane_is_cut_by_a_defender_moves_to_an_angle_that_is_clear()
    {
        // The unblocked left corner is at 45 degrees; a defender stands on that lane, near its far end (a defender in the
        // middle of the lane would also cut the neighbouring angles, which is right, but is not what is tested here).
        var open = new Scene(isHome: true, carrier: 6, CentralMidfielderHasTheBall, CompactFourFourTwo, skill: 20).Assign();
        var blocker = (SpatialPoint[])CompactFourFourTwo.Clone();
        var openLeft = open[5].Target;

        blocker[10] = new SpatialPoint(4_500 + ((openLeft.X - 4_500) * 85 / 100), 3_500 + ((openLeft.Y - 3_500) * 85 / 100));

        var blocked = new Scene(isHome: true, carrier: 6, CentralMidfielderHasTheBall, blocker, skill: 20);
        var orders = blocked.Assign();

        orders[5].Role.Should().Be(TickAttackingRole.Support);
        orders[5].Target.Should().NotBe(openLeft, "the lane is cut, so he finds another angle");
        TickOffBallSupport.LaneClear(blocked.Defenders, true, 4_500, 3_500, orders[5].Target.X, orders[5].Target.Y)
            .Should().BeTrue();
    }

    [Fact]
    public void The_passing_style_sets_how_far_the_supporters_stand_from_the_ball()
    {
        int Distance(MatchPassingStyle style)
        {
            var orders = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, skill: 20, passing: style).Assign();
            var target = orders[5].Target;

            orders[5].Role.Should().Be(TickAttackingRole.Support);

            return (int)SpatialMath.Sqrt(((long)(target.X - 4_500) * (target.X - 4_500)) + ((long)(target.Y - 3_500) * (target.Y - 3_500)));
        }

        Distance(MatchPassingStyle.ShortPassing).Should().BeCloseTo(TickOffBallSupport.ShortSupportDistance, 40);
        Distance(MatchPassingStyle.MixedPassing).Should().BeCloseTo(TickOffBallSupport.MixedSupportDistance, 40);
        Distance(MatchPassingStyle.DirectPassing).Should().BeCloseTo(TickOffBallSupport.DirectSupportDistance, 40);
    }

    [Fact]
    public void A_carrier_hard_against_the_touchline_still_gets_both_corners_on_the_pitch()
    {
        var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        attackers[6] = new SpatialPoint(4_500, 200);

        var orders = new Scene(isHome: true, 6, attackers, CompactFourFourTwo, skill: 20).Assign();

        foreach (var index in Indices(orders, TickAttackingRole.Support))
        {
            orders[index].Target.Y.Should().BeInRange(TickTacticalGeometry.Margin, SpatialPitch.PitchWidth - TickTacticalGeometry.Margin);
        }

        Indices(orders, TickAttackingRole.Support).Should().HaveCount(2);
    }

    [Fact]
    public void The_slots_go_to_the_same_players_however_the_squad_is_listed()
    {
        var forward = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, skill: 12);
        var swapped = forward.WithPlayersSwapped(5, 7);

        var one = forward.Assign();
        var other = swapped.Assign();

        other[7].Should().Be(one[5], "the player listed seventh is the one who was fifth");
        other[5].Should().Be(one[7]);

        for (var index = 0; index < one.Length; index++)
        {
            if (index is not (5 or 7))
            {
                other[index].Should().Be(one[index], $"player {index}");
            }
        }
    }

    [Fact]
    public void The_player_who_held_a_slot_last_tick_keeps_it_unless_another_is_clearly_nearer()
    {
        var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        // The left corner is at about (5843, 2157). Players 5 and 8 both stand near it; 5 is 15% farther than 8.
        attackers[5] = new SpatialPoint(5_843, 2_457);
        attackers[8] = new SpatialPoint(5_843, 2_417);

        var scene = new Scene(isHome: true, 6, attackers, CompactFourFourTwo) { PreviousSupporters = 1 << 5 };

        Indices(scene.Assign(), TickAttackingRole.Support).Should().Contain(5).And.NotContain(8, "he was already on the job");

        attackers[8] = new SpatialPoint(5_843, 2_257);

        var clearer = new Scene(isHome: true, 6, attackers, CompactFourFourTwo) { PreviousSupporters = 1 << 5 };

        Indices(clearer.Assign(), TickAttackingRole.Support).Should().Contain(8).And.NotContain(5, "now the other is far nearer");
    }

    [Fact]
    public void A_supporter_never_stands_beyond_the_offside_line_and_the_better_his_reading_the_closer_he_gets_to_the_corner()
    {
        // The carrier is in the opponent's half, 12 m short of a line 18 m from goal: a 45 degree corner would be beyond it.
        var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        attackers[6] = new SpatialPoint(7_000, 3_500);
        attackers[9] = new SpatialPoint(7_600, 2_800);
        attackers[10] = new SpatialPoint(7_600, 4_200);

        var orders = new Scene(isHome: true, 6, attackers, CompactFourFourTwo, skill: 20).Assign();

        foreach (var index in Indices(orders, TickAttackingRole.Support))
        {
            orders[index].Target.X.Should().BeLessThanOrEqualTo(8_200 - TickOffBallSupport.OnsideMargin);
        }

        // Positioning and Anticipation set how much of the way from his shape position to the corner he goes.
        var sharp = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, skill: 20);
        var sloppy = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, skill: 20);

        sloppy.Skills[5] = sloppy.Skills[5] with { Positioning = 1, Anticipation = 1 };

        var corner = sharp.Assign()[5].Target;
        var anchor = sharp.Anchors[5];
        var loose = sloppy.Assign()[5].Target;

        Distance(loose, corner).Should().BeGreaterThan(Distance(corner, corner));
        Distance(loose, anchor).Should().BeLessThan(Distance(corner, anchor), "the sloppy player stays nearer his shape position");
    }

    [Fact]
    public void The_harder_a_supporter_works_the_faster_he_goes_to_his_corner()
    {
        var scene = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo);

        scene.Skills[5] = scene.Skills[5] with { WorkRate = 1 };
        scene.Skills[7] = scene.Skills[7] with { WorkRate = 20 };

        var orders = scene.Assign();

        orders[5].PaceBasisPoints.Should().Be(TickOffBallSupport.SupportPaceBasisPoints + TickOffBallSupport.SupportPaceStep);
        orders[7].PaceBasisPoints.Should().Be(TickOffBallSupport.SupportPaceBasisPoints + (20 * TickOffBallSupport.SupportPaceStep));
    }

    [Fact]
    public void A_carrier_who_is_closed_down_also_gets_a_safe_outlet_behind_him_away_from_the_presser()
    {
        var calm = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, skill: 20).Assign();

        Indices(calm, TickAttackingRole.Outlet).Should().BeEmpty("nobody is on him");

        // A defender 5 m in front of him.
        var defenders = (SpatialPoint[])CompactFourFourTwo.Clone();

        defenders[9] = new SpatialPoint(5_000, 3_500);

        var scene = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, defenders, skill: 20);
        var orders = scene.Assign();
        var outlet = Indices(orders, TickAttackingRole.Outlet).Single();
        var target = orders[outlet].Target;

        target.X.Should().BeLessThan(4_500, "away from the presser, which is backwards");
        Distance(target, new SpatialPoint(4_500, 3_500)).Should().BeCloseTo(TickOffBallSupport.OutletDistance, 30);
        TickOffBallSupport.LaneClear(scene.Defenders, true, 4_500, 3_500, target.X, target.Y).Should().BeTrue();
        Indices(orders, TickAttackingRole.Support).Should().HaveCount(2, "he still has the triangle ahead of him");
    }

    [Fact]
    public void With_time_on_the_ball_a_striker_sprints_into_a_gap_behind_the_back_line()
    {
        var scene = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo);
        var orders = scene.Assign();
        var runner = Indices(orders, TickAttackingRole.Runner).Single();
        var target = orders[runner].Target;

        scene.Specs[runner].Family.Should().Be(MatchPositionFamily.Attack);
        target.X.Should().BeGreaterThan(8_200, "behind the line, which stands 18 m from goal");
        target.X.Should().BeLessThanOrEqualTo(SpatialPitch.PitchLength - TickOffBallSupport.RunGoalGap);
        orders[runner].PaceBasisPoints.Should().Be(10_000, "flat out");

        // The gap is between the centre-backs, not on top of a defender.
        foreach (var back in new[] { 2_500, 4_500 })
        {
            Math.Abs(target.Y - back).Should().BeGreaterThan(TickOffBallSupport.MinimumGap / 2 - 10);
        }
    }

    [Theory]
    [InlineData(MatchMentality.Defensive, 0)]
    [InlineData(MatchMentality.Cautious, 1)]
    [InlineData(MatchMentality.Balanced, 1)]
    [InlineData(MatchMentality.Positive, 2)]
    [InlineData(MatchMentality.Attacking, 3)]
    public void The_mentality_sets_how_many_runs_a_side_makes_and_no_two_runners_go_into_the_same_gap(MatchMentality mentality, int runners)
    {
        var scene = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, mentality);
        var orders = scene.Assign();
        var running = Indices(orders, TickAttackingRole.Runner).ToArray();

        running.Should().HaveCount(runners);
        TickOffBallSupport.RunBudget(mentality).Should().Be(runners);
        running.Select(index => orders[index].Target.Y).Should().OnlyHaveUniqueItems();

        // Strikers are sent before wide midfielders.
        if (runners is > 0 and < 3)
        {
            running.Select(index => scene.Specs[index].Family).Should().OnlyContain(family => family == MatchPositionFamily.Attack || runners > 1);
            orders[9].Role.Should().Be(TickAttackingRole.Runner);
        }
    }

    [Fact]
    public void A_pressed_carrier_starts_no_run_but_one_already_under_way_carries_on_until_he_is_nearly_in_contact()
    {
        var defenders = (SpatialPoint[])CompactFourFourTwo.Clone();

        // A defender 8 m from the ball: not free enough to start a run, not close enough to cancel one.
        defenders[9] = new SpatialPoint(5_300, 3_500);

        var fresh = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, defenders);

        Indices(fresh.Assign(), TickAttackingRole.Runner).Should().BeEmpty();

        var running = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, defenders) { PreviousRunners = 1 << 10 };

        Indices(running.Assign(), TickAttackingRole.Runner).Should().Equal(10);

        // 4 m: he is closed down and the run is off.
        defenders[9] = new SpatialPoint(4_900, 3_500);

        var closed = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, defenders) { PreviousRunners = 1 << 10 };

        Indices(closed.Assign(), TickAttackingRole.Runner).Should().BeEmpty();
    }

    [Fact]
    public void A_forward_beyond_the_line_never_starts_a_run_and_one_held_back_is_kept_onside()
    {
        var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        // Both strikers are already past the last defender but one; the carrier is too deep for a run to be on anyway.
        attackers[9] = new SpatialPoint(8_600, 2_800);

        var offside = new Scene(isHome: true, 6, attackers, CompactFourFourTwo);
        var orders = offside.Assign();

        orders[9].Role.Should().NotBe(TickAttackingRole.Runner);
        orders[10].Role.Should().Be(TickAttackingRole.Runner, "the one still onside goes instead");

        // A player holding his shape with an anchor beyond the line is held level with it, 1 m short.
        var deep = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        deep[6] = new SpatialPoint(2_000, 3_500);

        var holding = new Scene(isHome: true, 6, deep, CompactFourFourTwo);

        holding.Anchors[10] = new SpatialPoint(9_000, 4_200);

        var held = holding.Assign();

        held[10].Role.Should().Be(TickAttackingRole.Holding);
        held[10].Target.X.Should().Be(8_200 - TickOffBallSupport.OnsideMargin);
        held[10].Target.Y.Should().Be(4_200);
    }

    [Fact]
    public void Against_a_deep_line_there_is_no_room_to_run_so_a_forward_checks_into_the_pocket_instead()
    {
        // The back four sits 7 m from its own goal line: nothing behind it to run into.
        var defenders = (SpatialPoint[])CompactFourFourTwo.Clone();

        for (var index = 1; index <= 4; index++)
        {
            defenders[index] = new SpatialPoint(9_300, defenders[index].Y);
        }

        var scene = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, defenders, MatchMentality.Attacking);
        var orders = scene.Assign();

        Indices(orders, TickAttackingRole.Runner).Should().BeEmpty();

        var pocket = Indices(orders, TickAttackingRole.Pocket).Single();

        scene.Specs[pocket].Family.Should().Be(MatchPositionFamily.Attack);
        orders[pocket].Target.X.Should().Be(9_300 - TickOffBallSupport.PocketDepth, "5 m in front of the back line");

        // He is drawn 40% of the way from his own side to the carrier's.
        var from = CentralMidfielderHasTheBall[pocket].Y;
        var expected = from + ((3_500 - from) * TickOffBallSupport.PocketPullPercent / 100);

        orders[pocket].Target.Y.Should().Be(expected);
        orders[pocket].PaceBasisPoints.Should().Be(TickOffBallSupport.PocketPaceBasisPoints);
    }

    [Fact]
    public void No_pocket_is_offered_once_the_carrier_is_already_on_the_line()
    {
        var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        attackers[6] = new SpatialPoint(7_000, 3_500);

        var orders = new Scene(isHome: true, 6, attackers, CompactFourFourTwo).Assign();

        Indices(orders, TickAttackingRole.Pocket).Should().BeEmpty();
    }

    [Fact]
    public void A_winger_cutting_inside_sends_the_full_back_on_his_flank_into_the_channel_he_has_left()
    {
        var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        attackers[5] = new SpatialPoint(6_000, 900);

        var scene = new Scene(isHome: true, carrier: 5, attackers, CompactFourFourTwo).Moving(5, heading: 128, centimetresPerSecond: 500);
        var orders = scene.Assign();

        orders[5].Role.Should().Be(TickAttackingRole.Carrier);
        Indices(orders, TickAttackingRole.Overlap).Should().Equal(1);
        orders[1].Target.Should().Be(new SpatialPoint(6_000 + TickOffBallSupport.OverlapAhead, TickOffBallSupport.OverlapLane));
        orders[1].PaceBasisPoints.Should().Be(TickOffBallSupport.OverlapPaceBasisPoints + (10 * TickOffBallSupport.OverlapPaceStep));
        orders[4].Role.Should().NotBe(TickAttackingRole.Overlap, "the far full-back stays home");
        orders[2].Role.Should().NotBe(TickAttackingRole.Overlap);
        orders[3].Role.Should().NotBe(TickAttackingRole.Overlap, "centre-backs never overlap");

        // The same on the other flank, mirrored.
        var rightWing = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        rightWing[8] = new SpatialPoint(6_000, 6_100);

        var right = new Scene(isHome: true, carrier: 8, rightWing, CompactFourFourTwo).Moving(8, heading: 1_024 - 128, centimetresPerSecond: 500);
        var rightOrders = right.Assign();

        Indices(rightOrders, TickAttackingRole.Overlap).Should().Equal(4);
        rightOrders[4].Target.Should().Be(new SpatialPoint(7_000, SpatialPitch.PitchWidth - TickOffBallSupport.OverlapLane));
    }

    [Fact]
    public void There_is_no_overlap_for_a_winger_going_down_the_line_a_slow_one_a_central_one_or_a_defensive_side()
    {
        var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        attackers[5] = new SpatialPoint(6_000, 900);

        TickAttackingOrder[] Overlaps(int heading, int speed, MatchMentality mentality = MatchMentality.Balanced, int y = 900)
        {
            var points = (SpatialPoint[])attackers.Clone();

            points[5] = new SpatialPoint(6_000, y);

            return new Scene(isHome: true, carrier: 5, points, CompactFourFourTwo, mentality).Moving(5, heading, speed).Assign();
        }

        Indices(Overlaps(heading: 0, speed: 500), TickAttackingRole.Overlap).Should().BeEmpty("he is running straight on");
        Indices(Overlaps(heading: 1_024 - 128, speed: 500), TickAttackingRole.Overlap).Should().BeEmpty("he is going out, not in");
        Indices(Overlaps(heading: 128, speed: 100), TickAttackingRole.Overlap).Should().BeEmpty("he is hardly moving");
        Indices(Overlaps(heading: 128, speed: 500, MatchMentality.Defensive), TickAttackingRole.Overlap).Should().BeEmpty("a defensive side holds its shape");
        Indices(Overlaps(heading: 256, speed: 500, y: 3_300), TickAttackingRole.Overlap).Should().BeEmpty("he is not wide");
        Indices(Overlaps(heading: 128, speed: 500), TickAttackingRole.Overlap).Should().Equal(1);
    }

    [Fact]
    public void The_away_side_attacks_the_same_way_mirrored()
    {
        var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

        attackers[5] = new SpatialPoint(6_000, 900);

        foreach (var (carrier, heading) in new[] { (6, 0), (5, 128) })
        {
            var home = new Scene(isHome: true, carrier, attackers, CompactFourFourTwo, MatchMentality.Attacking).Moving(carrier, heading, 500);
            var away = new Scene(isHome: false, carrier, attackers, CompactFourFourTwo, MatchMentality.Attacking).Moving(carrier, heading, 500);

            var homeOrders = home.Assign();
            var awayOrders = away.Assign();

            for (var index = 0; index < homeOrders.Length; index++)
            {
                awayOrders[index].Role.Should().Be(homeOrders[index].Role, $"player {index}, carrier {carrier}");
                awayOrders[index].PaceBasisPoints.Should().Be(homeOrders[index].PaceBasisPoints);
                awayOrders[index].Target.X.Should().Be(SpatialPitch.PitchLength - homeOrders[index].Target.X);
                awayOrders[index].Target.Y.Should().Be(SpatialPitch.PitchWidth - homeOrders[index].Target.Y);
            }
        }
    }

    [Fact]
    public void With_the_ball_loose_everyone_but_the_goalkeeper_simply_holds_his_position()
    {
        var scene = new Scene(isHome: true, carrier: -1, CentralMidfielderHasTheBall, CompactFourFourTwo);
        var orders = scene.Assign();

        orders[0].Role.Should().Be(TickAttackingRole.Keeper);
        orders.Skip(1).Should().OnlyContain(order => order.Role == TickAttackingRole.Holding);

        for (var index = 0; index < orders.Length; index++)
        {
            orders[index].Target.Should().Be(scene.Anchors[index]);
            orders[index].PaceBasisPoints.Should().Be(0);
        }
    }

    [Fact]
    public void The_masks_name_the_supporters_and_the_runners_for_the_next_tick()
    {
        var orders = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo).Assign();

        TickOffBallSupport.SupporterMask(orders).Should().Be((1 << 5) | (1 << 7));
        TickOffBallSupport.RunnerMask(orders).Should().Be(1 << 9);
    }

    [Fact]
    public void LaneClear_is_false_only_for_a_defender_inside_the_reach_of_the_lane()
    {
        TickPlayerState[] DefendersAt(params (int X, int Y)[] points) =>
            [.. points.Select(point => TickPlayerState.Standing(point.X, point.Y, 0, 10_000))];

        var on = DefendersAt((3_000, 3_100));
        var beside = DefendersAt((3_000, 3_300));
        var behind = DefendersAt((1_000, 3_000));
        var beyond = DefendersAt((6_000, 3_000));

        TickOffBallSupport.LaneClear(on, true, 2_000, 3_000, 4_000, 3_000).Should().BeFalse();
        TickOffBallSupport.LaneClear(beside, true, 2_000, 3_000, 4_000, 3_000).Should().BeTrue();
        TickOffBallSupport.LaneClear(behind, true, 2_000, 3_000, 4_000, 3_000).Should().BeTrue("behind the passer");
        TickOffBallSupport.LaneClear(beyond, true, 2_000, 3_000, 4_000, 3_000).Should().BeTrue("beyond the receiver");

        // The away side's coordinates are in its own point of view, so the same lane is mirrored.
        TickOffBallSupport.LaneClear(
            DefendersAt((SpatialPitch.PitchLength - 3_000, SpatialPitch.PitchWidth - 3_100)),
            false,
            2_000,
            3_000,
            4_000,
            3_000).Should().BeFalse();
    }

    [Fact]
    public void Wherever_a_midfielder_takes_the_ball_in_the_middle_third_he_has_two_supporters_and_most_lanes_are_clear()
    {
        foreach (var ballX in new[] { 3_400, 4_500, 5_500, 6_500 })
        {
            foreach (var ballY in new[] { 1_500, 3_500, 5_500 })
            {
                var attackers = (SpatialPoint[])CentralMidfielderHasTheBall.Clone();

                // The squad moves with the ball: two midfielders close by, either side and a little behind, the strikers ahead.
                attackers[6] = new SpatialPoint(ballX, ballY);
                attackers[5] = new SpatialPoint(ballX - 300, Math.Max(300, ballY - 1_300));
                attackers[7] = new SpatialPoint(ballX - 300, Math.Min(6_700, ballY + 1_300));
                attackers[9] = new SpatialPoint(ballX + 2_500, ballY - 700);
                attackers[10] = new SpatialPoint(ballX + 2_500, ballY + 700);

                var scene = new Scene(isHome: true, 6, attackers, CompactFourFourTwo, skill: 20);
                var orders = scene.Assign();
                var options = Indices(orders, TickAttackingRole.Support).ToArray();

                options.Should().HaveCount(2, $"ball at ({ballX}, {ballY})");

                var clear = options.Count(index => TickOffBallSupport.LaneClear(
                    scene.Defenders,
                    true,
                    ballX,
                    ballY,
                    orders[index].Target.X,
                    orders[index].Target.Y));

                clear.Should().BeGreaterThanOrEqualTo(2, $"both lanes are cleared with the ball at ({ballX}, {ballY})");
            }
        }
    }

    [Fact]
    public void A_whole_match_of_attacking_and_defending_is_deterministic_and_allocates_nothing()
    {
        var homeStyle = TickTeamStyle.From(new MatchInstructionsV1 { Mentality = MatchMentality.Attacking, Pressing = MatchPressing.HighPress });
        var awayStyle = TickTeamStyle.From(new MatchInstructionsV1 { Mentality = MatchMentality.Balanced, Pressing = MatchPressing.LowBlock });
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
            var homeSupport = 0;
            var homeRuns = 0;
            var awaySupport = 0;
            var awayRuns = 0;

            // Once, outside the loop: a stackalloc in a loop would grow the stack on every pass.
            Span<TickDefensiveOrder> defending = stackalloc TickDefensiveOrder[11];
            Span<TickAttackingOrder> attacking = stackalloc TickAttackingOrder[11];

            for (var tick = 0; tick < 54_000; tick++)
            {
                var homeHasBall = (tick / 200) % 2 == 0;
                var carrier = (homeHasBall ? home : away)[6];
                var ballX = TickSpatialUnits.ToUnits(carrier.X);
                var ballY = TickSpatialUnits.ToUnits(carrier.Y);

                TickTacticalGeometry.ResolveTeam(FourFourTwo, homeStyle, true, homeHasBall, ballX, ballY, homeAnchors);
                TickTacticalGeometry.ResolveTeam(FourFourTwo, awayStyle, false, !homeHasBall, ballX, ballY, awayAnchors);

                var attackers = homeHasBall ? home : away;
                var defenders = homeHasBall ? away : home;

                TickDefensiveAI.Assign(
                    new TickDefensiveSituation
                    {
                        IsHome = !homeHasBall,
                        Pressing = homeHasBall ? MatchPressing.LowBlock : MatchPressing.HighPress,
                        Defenders = defenders,
                        Specs = FourFourTwo,
                        Anchors = homeHasBall ? awayAnchors : homeAnchors,
                        Skills = skills,
                        Attackers = attackers,
                        BallX = ballX,
                        BallY = ballY,
                        CarrierIndex = 6,
                        PreviousPresser = homeHasBall ? awayPresser : homePresser,
                    },
                    defending);

                TickOffBallSupport.Assign(
                    new TickAttackingSituation
                    {
                        IsHome = homeHasBall,
                        Mentality = homeHasBall ? MatchMentality.Attacking : MatchMentality.Balanced,
                        Passing = MatchPassingStyle.MixedPassing,
                        Attackers = attackers,
                        Specs = FourFourTwo,
                        Anchors = homeHasBall ? homeAnchors : awayAnchors,
                        Skills = skills,
                        Defenders = defenders,
                        CarrierIndex = 6,
                        PreviousSupporters = homeHasBall ? homeSupport : awaySupport,
                        PreviousRunners = homeHasBall ? homeRuns : awayRuns,
                    },
                    attacking);

                if (homeHasBall)
                {
                    awayPresser = Presser(defending);
                    homeSupport = TickOffBallSupport.SupporterMask(attacking);
                    homeRuns = TickOffBallSupport.RunnerMask(attacking);
                }
                else
                {
                    homePresser = Presser(defending);
                    awaySupport = TickOffBallSupport.SupporterMask(attacking);
                    awayRuns = TickOffBallSupport.RunnerMask(attacking);
                }

                TickOffBallSupport.ToSteering(attacking, targets, paces);
                TickSteering.StepTeam(attackers, profiles, targets, paces);
                TickDefensiveAI.ToSteering(defending, targets, paces);
                TickSteering.StepTeam(defenders, profiles, targets, paces);

                hash = unchecked((hash * 31) + home[tick % 11].X);
                hash = unchecked((hash * 31) + away[tick % 11].Y);
                hash = unchecked((hash * 31) + homeSupport + (awayRuns * 7) + homeRuns);
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

    private static IEnumerable<int> Indices(TickAttackingOrder[] orders, TickAttackingRole role) =>
        orders.Select((order, index) => (order, index)).Where(pair => pair.order.Role == role).Select(pair => pair.index);

    private static int Distance(SpatialPoint one, SpatialPoint other) =>
        (int)SpatialMath.Sqrt(((long)(one.X - other.X) * (one.X - other.X)) + ((long)(one.Y - other.Y) * (one.Y - other.Y)));

    private static TickPlayerState[] Kickoff(bool isHome) =>
        [
            .. FourFourTwo.Select(spec => TickPlayerState.Standing(
                isHome ? spec.OwnX : SpatialPitch.PitchLength - spec.OwnX,
                isHome ? spec.OwnY : SpatialPitch.PitchWidth - spec.OwnY,
                isHome ? 0 : TickTrigonometry.HalfTurn,
                10_000)),
        ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_man_holding_his_place_looks_for_a_free_square_once_a_second_and_a_seat_at_a_time(bool isHome)
    {
        var scene = new Scene(isHome, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, MatchMentality.Defensive, skill: 10);
        var looked = new bool[11];

        for (var tick = 0; tick < 10; tick++)
        {
            scene.Tick = tick;

            var before = scene.HasOffset.ToArray();
            var orders = scene.Assign();

            for (var index = 0; index < 11; index++)
            {
                if (scene.HasOffset[index] && !before[index])
                {
                    looked[index] = true;
                    ((tick + (3 * index)) % 10).Should().Be(0, $"seat {index} looks at the tick his turn comes");
                    orders[index].Role.Should().Be(TickAttackingRole.Holding);
                }
            }
        }

        looked[0].Should().BeFalse("the goalkeeper stays in goal");
        looked[1].Should().BeFalse("a back-line man holds his line");
        looked[4].Should().BeFalse("a back-line man holds his line");
        looked[8].Should().BeTrue("a wide midfielder with nothing to do looks for room");
        (looked[9] || looked[10]).Should().BeTrue("the striker who is not dropping into the pocket looks for room");
    }

    [Fact]
    public void The_square_he_picks_lies_within_eight_metres_of_his_place_is_onside_and_is_walked_to_at_a_jog()
    {
        var scene = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, MatchMentality.Defensive, skill: 10);

        for (var tick = 0; tick < 10; tick++)
        {
            scene.Tick = tick;
            scene.Assign();
        }

        var line = 8_200;

        for (var index = 8; index <= 10; index++)
        {
            if (!scene.HasOffset[index])
            {
                continue;
            }

            var offset = scene.Offsets[index];
            var square = new SpatialPoint(scene.Anchors[index].X + offset.X, scene.Anchors[index].Y + offset.Y);

            SpatialMath.Sqrt((offset.X * (long)offset.X) + (offset.Y * (long)offset.Y)).Should().BeLessThanOrEqualTo(800, "within the outer ring");
            square.X.Should().BeLessThanOrEqualTo(line - TickOffBallSupport.OnsideMargin);
            scene.OffsetPaces[index].Should().BeInRange(3_000, 4_000);
        }
    }

    [Fact]
    public void He_keeps_the_square_he_has_when_nothing_has_changed_and_there_is_no_search_without_a_place_to_keep_it()
    {
        var scene = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, MatchMentality.Defensive, skill: 10);

        for (var tick = 0; tick < 10; tick++)
        {
            scene.Tick = tick;
            scene.Assign();
        }

        scene.HasOffset.Count(has => has).Should().BeGreaterThan(0);

        var first = scene.Offsets.ToArray();

        for (var tick = 10; tick < 30; tick++)
        {
            scene.Tick = tick;
            scene.Assign();
        }

        scene.Offsets.Should().Equal(first, "with the picture the same, no square beats the one he has by 15%");

        var blind = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, MatchMentality.Defensive, skill: 10);
        var orders = new TickAttackingOrder[11];

        TickOffBallSupport.Assign(
            new TickAttackingSituation
            {
                IsHome = true,
                Mentality = MatchMentality.Defensive,
                Passing = MatchPassingStyle.MixedPassing,
                Attackers = blind.Attackers,
                Specs = blind.Specs,
                Anchors = blind.Anchors,
                Skills = blind.Skills,
                Defenders = blind.Defenders,
                CarrierIndex = 6,
                Tick = 3,
            },
            orders);

        orders[8].Role.Should().Be(TickAttackingRole.Holding, "a search with nowhere to write the square is simply off");
    }

    [Fact]
    public void A_man_with_a_job_is_given_no_square()
    {
        var scene = new Scene(isHome: true, 6, CentralMidfielderHasTheBall, CompactFourFourTwo, skill: 10);

        for (var tick = 0; tick < 10; tick++)
        {
            scene.Tick = tick;

            var orders = scene.Assign();

            for (var index = 0; index < 11; index++)
            {
                if (orders[index].Role != TickAttackingRole.Holding)
                {
                    scene.HasOffset[index].Should().BeFalse($"seat {index} is a {orders[index].Role}");
                }
            }
        }
    }

    private static TickPlayerProfile[] Profiles() =>
        [.. Enumerable.Repeat(TickPlayerProfile.From(PlayerAttributesV1.From(Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray())), 11)];

    private static TickPlayerSkills[] Skills(int value = 10) =>
        [.. Enumerable.Repeat(TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(value, MatchAttributeNames.Count).ToArray())), 11)];

    /// <summary>One attacking side with the ball, the defenders it faces, and everything the support AI reads.</summary>
    private sealed class Scene
    {
        private readonly bool _isHome;

        public Scene(
            bool isHome,
            int carrier,
            SpatialPoint[] attackersOwn,
            SpatialPoint[] defendersOwn,
            MatchMentality mentality = MatchMentality.Balanced,
            int skill = 10,
            MatchPassingStyle passing = MatchPassingStyle.MixedPassing)
        {
            _isHome = isHome;
            Carrier = carrier;
            Mentality = mentality;
            Passing = passing;
            Specs = FourFourTwo;
            Skills = TickOffBallSupportTests.Skills(skill);
            Attackers = [.. attackersOwn.Select(point => TickPlayerState.Standing(Mirror(point).X, Mirror(point).Y, isHome ? 0 : TickTrigonometry.HalfTurn, 10_000))];
            Defenders = [.. defendersOwn.Select(point => TickPlayerState.Standing(Mirror(point).X, Mirror(point).Y, isHome ? TickTrigonometry.HalfTurn : 0, 10_000))];

            var ball = carrier < 0 ? Mirror(new SpatialPoint(5_000, 3_500)) : Mirror(attackersOwn[carrier]);
            var style = TickTeamStyle.From(new MatchInstructionsV1 { Mentality = mentality });

            Anchors = new SpatialPoint[Specs.Length];
            TickTacticalGeometry.ResolveTeam(Specs, style, isHome, hasPossession: true, ball.X, ball.Y, Anchors);
        }

        public TickAnchorSpec[] Specs { get; private set; }

        public SpatialPoint[] Anchors { get; private set; }

        public TickPlayerState[] Attackers { get; private set; }

        public TickPlayerState[] Defenders { get; }

        public TickPlayerSkills[] Skills { get; private set; }

        public int Carrier { get; private set; }

        public MatchMentality Mentality { get; }

        public MatchPassingStyle Passing { get; }

        public int PreviousSupporters { get; set; }

        public int PreviousRunners { get; set; }

        public int Tick { get; set; }

        public SpatialPoint[] Offsets { get; } = new SpatialPoint[11];

        public bool[] HasOffset { get; } = new bool[11];

        public int[] OffsetPaces { get; } = new int[11];

        /// <summary>Turns an absolute point into the side's own point of view (the conversion is its own inverse).</summary>
        public SpatialPoint Own(SpatialPoint point) => Mirror(point);

        /// <summary>Sets a player running: heading in the side's own point of view, speed in cm/s.</summary>
        public Scene Moving(int index, int heading, int centimetresPerSecond)
        {
            var player = Attackers[index];

            player.Heading = TickTrigonometry.Normalize(_isHome ? heading : heading + TickTrigonometry.HalfTurn);
            player.Speed = TickSpatialUnits.SpeedToFixedPerTick(centimetresPerSecond);
            Attackers[index] = player;

            return this;
        }

        /// <summary>Gets the same scene with two players listed in each other's places.</summary>
        public Scene WithPlayersSwapped(int one, int other)
        {
            var copy = (Scene)MemberwiseClone();
            var map = Enumerable.Range(0, Attackers.Length).ToArray();

            (map[one], map[other]) = (other, one);
            copy.Specs = [.. map.Select(index => Specs[index])];
            copy.Anchors = [.. map.Select(index => Anchors[index])];
            copy.Attackers = [.. map.Select(index => Attackers[index])];
            copy.Skills = [.. map.Select(index => Skills[index])];
            copy.Carrier = Array.IndexOf(map, Carrier);

            return copy;
        }

        public TickAttackingOrder[] Assign()
        {
            var orders = new TickAttackingOrder[Attackers.Length];

            TickOffBallSupport.Assign(
                new TickAttackingSituation
                {
                    IsHome = _isHome,
                    Mentality = Mentality,
                    Passing = Passing,
                    Attackers = Attackers,
                    Specs = Specs,
                    Anchors = Anchors,
                    Skills = Skills,
                    Defenders = Defenders,
                    CarrierIndex = Carrier,
                    PreviousSupporters = PreviousSupporters,
                    PreviousRunners = PreviousRunners,
                    Tick = Tick,
                    Offsets = Offsets,
                    HasOffset = HasOffset,
                    OffsetPaces = OffsetPaces,
                },
                orders);

            return orders;
        }

        private SpatialPoint Mirror(SpatialPoint point) =>
            _isHome ? point : new SpatialPoint(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);
    }
}
