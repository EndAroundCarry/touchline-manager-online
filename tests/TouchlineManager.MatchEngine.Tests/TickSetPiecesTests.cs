using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies where the 22 players stand for each restart, who takes it and what the taker does (Milestone 7).
/// </summary>
/// <remarks>
/// Positions are read back in the taker's point of view (he attacks towards X = 10,000, his goal is at 0), whichever end the
/// side really plays; a scene stands the players where they really are, so the away side is mirrored.
/// </remarks>
public sealed class TickSetPiecesTests
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

    // ---- Kick-off ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void At_a_kick_off_each_side_is_in_its_own_half()
    {
        var scene = new Scene(TickRestart.KickOff(homeTakes: true));

        var plan = scene.Place();

        scene.Own.Where((_, index) => index != plan.TakerIndex).Should().OnlyContain(point => point.X <= TickSetPieces.OwnHalfLimit);
        scene.Own[plan.TakerIndex].X.Should().BeLessThanOrEqualTo(5_000, "and the taker is on the spot, not past it");
        scene.Opp.Should().OnlyContain(point => point.X >= 10_000 - TickSetPieces.OwnHalfLimit);
    }

    [Fact]
    public void The_forward_nearest_the_middle_takes_the_kick_off_from_the_centre_spot_with_a_partner_just_behind()
    {
        var specs = (TickAnchorSpec[])FourFourTwo.Clone();

        specs[10] = new TickAnchorSpec(7_500, 3_300, MatchPositionFamily.Attack);

        var scene = new Scene(TickRestart.KickOff(homeTakes: true), takerSpecs: specs);
        var plan = scene.Place();

        plan.TakerIndex.Should().Be(10, "he starts nearest the middle");
        plan.OptionIndex.Should().Be(9, "the other forward is the partner");
        Distance(scene.Own[10], new SpatialPoint(5_000, 3_500)).Should().BeLessThan(TickSetPieces.ReadyDistance);
        scene.Own[9].X.Should().BeLessThan(5_000, "the partner is in his own half, behind the taker");
        Distance(scene.Own[9], new SpatialPoint(5_000, 3_500)).Should().BeLessThan(500, "two players at the centre spot");
        scene.Own[9].Y.Should().BeLessThan(3_500, "he stands on the side he came from (the left)");
    }

    [Fact]
    public void The_opposition_stay_outside_the_centre_circle_at_a_kick_off()
    {
        var closeUp = new TickAnchorSpec[11];

        for (var index = 0; index < closeUp.Length; index++)
        {
            closeUp[index] = new TickAnchorSpec(4_900, 3_000 + (index * 100), index == 0 ? MatchPositionFamily.Goalkeeper : MatchPositionFamily.Attack);
        }

        var scene = new Scene(TickRestart.KickOff(homeTakes: true), otherSpecs: closeUp);

        scene.Place();

        foreach (var point in scene.Opp)
        {
            Distance(point, new SpatialPoint(5_000, 3_500)).Should().BeGreaterThanOrEqualTo(TickSetPieces.CentreCircleRadius - 5);
            point.X.Should().BeGreaterThanOrEqualTo(5_000, "and still in their own half");
        }
    }

    [Fact]
    public void The_kick_off_is_a_short_ball_back_to_the_partner()
    {
        var scene = new Scene(TickRestart.KickOff(homeTakes: true));
        var plan = scene.Place();
        var decision = scene.Decide(plan);

        decision.Action.Should().Be(TickCarrierAction.Pass);
        decision.Receiver.Should().Be(plan.OptionIndex);
        decision.Target.Should().Be(new SpatialPoint(scene.Takers[plan.OptionIndex].X / 1_000, scene.Takers[plan.OptionIndex].Y / 1_000), "to where he stands");
    }

    // ---- Goal kick -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_goalkeeper_takes_the_goal_kick_standing_just_behind_the_ball()
    {
        var restart = TickRestart.GoalKick(homeTakes: true, ballY: 3_500);
        var scene = new Scene(restart);
        var plan = scene.Place();

        plan.TakerIndex.Should().Be(0);
        scene.Own[0].Should().Be(new SpatialPoint(restart.SpotX - TickSetPieces.KeeperStandBack, 3_500));
    }

    [Fact]
    public void At_a_goal_kick_the_centre_backs_split_wide_of_the_area_and_the_full_backs_stay_put()
    {
        var scene = new Scene(TickRestart.GoalKick(homeTakes: true, ballY: 3_500));

        scene.Place();

        scene.Own[2].Should().Be(new SpatialPoint(TickSetPieces.SplitCentreBackX, 3_500 - TickSetPieces.SplitCentreBackOffset));
        scene.Own[3].Should().Be(new SpatialPoint(TickSetPieces.SplitCentreBackX, 3_500 + TickSetPieces.SplitCentreBackOffset));
        scene.Own[1].X.Should().BeGreaterThan(TickSetPieces.SplitCentreBackX - 200, "a full-back is not a centre-back");
    }

    [Fact]
    public void Two_centre_backs_on_the_same_side_of_the_board_still_split_to_opposite_sides()
    {
        var specs = (TickAnchorSpec[])FourFourTwo.Clone();

        specs[2] = new TickAnchorSpec(2_000, 4_200, MatchPositionFamily.Defence);
        specs[3] = new TickAnchorSpec(2_000, 4_600, MatchPositionFamily.Defence);

        var scene = new Scene(TickRestart.GoalKick(homeTakes: true, ballY: 3_500), takerSpecs: specs);

        scene.Place();

        Math.Sign(scene.Own[2].Y - 3_500).Should().NotBe(Math.Sign(scene.Own[3].Y - 3_500));
    }

    [Fact]
    public void The_better_passer_of_the_two_centre_backs_is_the_short_option()
    {
        var scene = new Scene(
            TickRestart.GoalKick(homeTakes: true, ballY: 3_500),
            takerSkills: [(2, MatchAttributeName.Passing, 7), (3, MatchAttributeName.Passing, 16)]);

        scene.Place().OptionIndex.Should().Be(3);
    }

    [Fact]
    public void The_opposition_are_kept_out_of_the_goal_kicks_penalty_area()
    {
        var deep = (TickAnchorSpec[])FourFourTwo.Clone();

        deep[9] = new TickAnchorSpec(9_700, 3_500, MatchPositionFamily.Attack);
        deep[10] = new TickAnchorSpec(9_400, 2_500, MatchPositionFamily.Attack);

        var scene = new Scene(TickRestart.GoalKick(homeTakes: true, ballY: 3_500), otherSpecs: deep);

        scene.Place();

        foreach (var point in scene.Opp)
        {
            (point.X <= SpatialPitch.PenaltyBoxWidth && point.Y >= SpatialPitch.PenaltyBoxYMin && point.Y <= SpatialPitch.PenaltyBoxYMax)
                .Should().BeFalse($"{point} is in the area");
        }
    }

    [Theory]
    [InlineData("ShortPassing", false, true)]
    [InlineData("DirectPassing", false, false)]
    [InlineData("MixedPassing", false, true)]
    [InlineData("MixedPassing", true, false)]
    public void The_goal_kick_is_short_or_long_by_passing_style_and_by_how_hard_the_other_side_press(string style, bool pressed, bool expectShort)
    {
        var otherSpecs = (TickAnchorSpec[])FourFourTwo.Clone();

        // Their forwards stand either on the edge of the box (their own view 9,000 is 10 m from the taker's goal) or in their own half.
        otherSpecs[9] = new TickAnchorSpec(pressed ? 9_000 : 6_000, 2_600, MatchPositionFamily.Attack);
        otherSpecs[10] = new TickAnchorSpec(pressed ? 9_000 : 6_000, 4_400, MatchPositionFamily.Attack);

        var scene = new Scene(
            TickRestart.GoalKick(homeTakes: true, ballY: 3_500),
            otherSpecs: otherSpecs,
            passing: Enum.Parse<MatchPassingStyle>(style));
        var plan = scene.Place();
        var decision = scene.Decide(plan);

        decision.Action.Should().Be(TickCarrierAction.Pass);

        if (expectShort)
        {
            decision.Receiver.Should().Be(plan.OptionIndex, "the split centre-back");
        }
        else
        {
            scene.Specs[decision.Receiver].Family.Should().Be(MatchPositionFamily.Attack, "a long ball goes to a forward");
        }
    }

    [Fact]
    public void A_long_goal_kick_goes_to_the_best_header_of_the_forwards()
    {
        var scene = new Scene(
            TickRestart.GoalKick(homeTakes: true, ballY: 3_500),
            takerSkills: [(10, MatchAttributeName.Heading, 18), (10, MatchAttributeName.JumpingReach, 18)],
            passing: MatchPassingStyle.DirectPassing);
        var plan = scene.Place();

        scene.Decide(plan).Receiver.Should().Be(10);
    }

    // ---- Throw-in ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_nearest_outfield_player_takes_the_throw_in_on_the_line()
    {
        var restart = TickRestart.ThrowIn(homeTakes: true, ballX: 4_800, ballY: 0);
        var scene = new Scene(restart);
        var plan = scene.Place();
        var nearest = Enumerable.Range(1, 10).OrderBy(index => Distance(scene.Anchor(index), new SpatialPoint(4_800, 0))).First();

        plan.TakerIndex.Should().Be(nearest);
        scene.Own[nearest].X.Should().Be(4_800);
        scene.Own[nearest].Y.Should().BeLessThanOrEqualTo(TickSetPieces.ThrowerInset);
    }

    [Fact]
    public void Two_teammates_offer_outlets_infield_and_the_nearest_opponents_mark_them_goal_side()
    {
        var scene = new Scene(TickRestart.ThrowIn(homeTakes: true, ballX: 4_800, ballY: 0));
        var plan = scene.Place();
        var option = scene.Own[plan.OptionIndex];

        option.Should().Be(new SpatialPoint(4_800 + TickSetPieces.ShortOutlet.X, TickSetPieces.ShortOutlet.Y));

        var markers = scene.Opp.Where(point => Distance(point, option) <= TickSetPieces.OutletMarkerGap + 5).ToArray();

        markers.Should().ContainSingle("one opponent marks the nearer outlet");
        markers[0].X.Should().BeGreaterThan(option.X, "goal-side of him");
    }

    [Fact]
    public void Each_outlet_has_its_own_marker()
    {
        var scene = new Scene(TickRestart.ThrowIn(homeTakes: true, ballX: 4_800, ballY: 0));

        scene.Place();

        var far = new SpatialPoint(4_800 + TickSetPieces.LongOutlet.X + TickSetPieces.OutletMarkerGap, TickSetPieces.LongOutlet.Y);
        var near = new SpatialPoint(4_800 + TickSetPieces.ShortOutlet.X + TickSetPieces.OutletMarkerGap, TickSetPieces.ShortOutlet.Y);

        scene.Opp.Count(point => point == far).Should().Be(1);
        scene.Opp.Count(point => point == near).Should().Be(1);
    }

    [Fact]
    public void Nobody_stands_within_two_and_a_half_metres_of_the_thrower()
    {
        var scene = new Scene(TickRestart.ThrowIn(homeTakes: true, ballX: 5_000, ballY: 7_000));

        scene.Place();

        foreach (var point in scene.Opp)
        {
            Distance(point, scene.Spot).Should().BeGreaterThanOrEqualTo(TickSetPieces.ThrowInClearance - 5);
        }
    }

    [Fact]
    public void The_thrower_picks_the_outlet_with_more_space_and_switches_when_a_marker_closes_it()
    {
        var restart = TickRestart.ThrowIn(homeTakes: true, ballX: 4_800, ballY: 0);
        var open = new Scene(restart);
        var plan = open.Place();

        open.StandAttackers();
        open.StandOpponents();

        var first = open.Decide(plan);

        first.Action.Should().Be(TickCarrierAction.Pass);
        first.Receiver.Should().NotBe(plan.TakerIndex);

        // Now an opponent stands on top of the receiver the thrower chose.
        var covered = new Scene(restart);

        covered.Place();
        covered.StandAttackers();
        covered.StandOpponents();
        covered.CrowdOpponent(0, first.Receiver);
        covered.CrowdOpponent(1, first.Receiver);

        var second = covered.Decide(plan);

        second.Receiver.Should().NotBe(first.Receiver);
    }

    // ---- Corner --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_best_dead_ball_striker_of_the_nearest_four_takes_the_corner_and_stands_at_the_flag()
    {
        var restart = TickRestart.Corner(homeTakes: true, ballY: 100);
        var plain = new Scene(restart);
        var four = plain.Nearest(4);

        // Whoever is best of those four takes it; make the third nearest the best.
        var scene = new Scene(restart, takerSkills: [(four[2], MatchAttributeName.SetPieces, 19), (four[2], MatchAttributeName.Crossing, 17)]);
        var plan = scene.Place();

        plan.TakerIndex.Should().Be(four[2]);
        Distance(scene.Own[four[2]], scene.Spot).Should().BeLessThan(TickSetPieces.ReadyDistance);
    }

    [Fact]
    public void A_great_crosser_who_is_far_from_the_flag_does_not_take_the_corner()
    {
        var restart = TickRestart.Corner(homeTakes: true, ballY: 100);
        var scene = new Scene(restart, takerSkills: [(2, MatchAttributeName.SetPieces, 20), (2, MatchAttributeName.Crossing, 20)]);
        var plan = scene.Place();

        scene.Nearest(4).Should().NotContain(2);
        plan.TakerIndex.Should().NotBe(2);
    }

    [Fact]
    public void The_five_best_headers_go_into_the_box_best_on_the_penalty_spot()
    {
        var scene = new Scene(
            TickRestart.Corner(homeTakes: true, ballY: 100),
            takerSkills:
            [
                (10, MatchAttributeName.Heading, 20),
                (10, MatchAttributeName.JumpingReach, 20),
                (2, MatchAttributeName.Heading, 17),
                (3, MatchAttributeName.Heading, 16),
                (6, MatchAttributeName.Heading, 15),
                (7, MatchAttributeName.Heading, 14),
            ]);
        var plan = scene.Place();

        plan.TakerIndex.Should().NotBe(10);
        scene.Own[10].Should().Be(new SpatialPoint(8_900, 3_500), "the best header takes the penalty spot");

        var inBox = Enumerable.Range(1, 10)
            .Where(index => index != plan.TakerIndex && index != plan.OptionIndex && scene.Own[index].X >= 8_350 && scene.Own[index].Y is >= 1_500 and <= 5_500)
            .ToArray();

        inBox.Should().HaveCount(TickSetPieces.CornerHeaders);
        inBox.Should().Contain(new[] { 10, 2, 3 });
    }

    [Fact]
    public void The_attackers_not_sent_up_wait_at_the_edge_of_the_area()
    {
        var scene = new Scene(TickRestart.Corner(homeTakes: true, ballY: 100));
        var plan = scene.Place();

        for (var index = 1; index <= 10; index++)
        {
            if (index == plan.TakerIndex || index == plan.OptionIndex || scene.Own[index].X >= 8_350)
            {
                continue;
            }

            scene.Own[index].X.Should().BeLessThanOrEqualTo(TickSetPieces.RestDefenceLimit);
        }
    }

    [Fact]
    public void One_attacker_offers_the_short_corner_along_the_touchline()
    {
        var scene = new Scene(TickRestart.Corner(homeTakes: true, ballY: 100));
        var plan = scene.Place();

        plan.OptionIndex.Should().BeGreaterThan(0);
        scene.Own[plan.OptionIndex].Should().Be(new SpatialPoint(scene.Spot.X - TickSetPieces.ShortCornerOffset.X, scene.Spot.Y + TickSetPieces.ShortCornerOffset.Y));
    }

    [Fact]
    public void The_defending_side_keeps_two_men_up_two_on_the_posts_and_marks_the_headers_goal_side_best_on_best()
    {
        var scene = new Scene(
            TickRestart.Corner(homeTakes: true, ballY: 100),
            otherSkills:
            [
                (2, MatchAttributeName.Heading, 19),
                (2, MatchAttributeName.JumpingReach, 19),
                (3, MatchAttributeName.Heading, 18),
                (9, MatchAttributeName.Heading, 2),
                (10, MatchAttributeName.Heading, 3),
            ],
            takerSkills: [(10, MatchAttributeName.Heading, 20), (10, MatchAttributeName.JumpingReach, 20)]);

        scene.Place();

        // The best attacking header stands on the penalty spot; the best defender marks him from 1.8 m goal-side.
        scene.Opp[2].Should().Be(new SpatialPoint(8_900 + TickSetPieces.BoxMarkerGap, 3_500));

        scene.Opp.Count(point => point.X == 10_000 - 120).Should().Be(2, "two men on the posts");
        scene.Opp.Count(point => point.X <= 6_500 && point.X != 10_000 - 120).Should().BeGreaterThanOrEqualTo(TickSetPieces.CounterReserve);
        scene.Opp[0].X.Should().BeGreaterThanOrEqualTo(9_750, "the keeper is on his line");
    }

    [Fact]
    public void The_defenders_stay_nine_metres_from_the_corner_flag()
    {
        var scene = new Scene(TickRestart.Corner(homeTakes: true, ballY: 6_900));

        scene.Place();

        foreach (var point in scene.Opp)
        {
            Distance(point, scene.Spot).Should().BeGreaterThanOrEqualTo(TickSetPieces.ClearanceRadius - 5);
        }
    }

    [Fact]
    public void A_corner_from_the_other_flank_mirrors_the_slots()
    {
        var upper = new Scene(TickRestart.Corner(homeTakes: true, ballY: 100));
        var lower = new Scene(TickRestart.Corner(homeTakes: true, ballY: 6_900));
        var upperPlan = upper.Place();
        var lowerPlan = lower.Place();

        upper.Own[upperPlan.OptionIndex].Y.Should().BeLessThan(1_000);
        lower.Own[lowerPlan.OptionIndex].Y.Should().BeGreaterThan(6_000);

        // The near-post slot (offset towards the flank) is on the corner's side.
        var near = upper.Own.First(point => point.X == 9_450 && point.Y < 3_500);
        var nearLower = lower.Own.First(point => point.X == 9_450 && point.Y > 3_500);

        (3_500 - near.Y).Should().Be(nearLower.Y - 3_500);
    }

    [Fact]
    public void The_corner_is_crossed_to_the_header_with_the_fewest_defenders_on_him()
    {
        var scene = new Scene(
            TickRestart.Corner(homeTakes: true, ballY: 100),
            takerSkills: [(10, MatchAttributeName.Heading, 20), (10, MatchAttributeName.JumpingReach, 20)]);
        var plan = scene.Place();

        scene.StandAttackers();
        scene.ClearOpponents();

        var open = scene.Decide(plan);

        open.Action.Should().Be(TickCarrierAction.Cross);
        open.Receiver.Should().Be(10, "nobody is within reach of anybody, so the best header wins");

        // Three defenders crowd him: the cross goes to another header.
        scene.CrowdOpponent(7, 10);
        scene.CrowdOpponent(8, 10);
        scene.CrowdOpponent(9, 10);

        var crowded = scene.Decide(plan);

        crowded.Action.Should().Be(TickCarrierAction.Cross);
        crowded.Receiver.Should().NotBe(10);
    }

    [Fact]
    public void A_short_passing_side_plays_the_corner_short_when_the_short_man_is_free()
    {
        var scene = new Scene(TickRestart.Corner(homeTakes: true, ballY: 100), passing: MatchPassingStyle.ShortPassing);
        var plan = scene.Place();

        scene.StandAttackers();
        scene.StandOpponents();

        var free = scene.Decide(plan);

        free.Action.Should().Be(TickCarrierAction.Pass);
        free.Receiver.Should().Be(plan.OptionIndex);

        scene.CrowdOpponent(5, plan.OptionIndex);

        scene.Decide(plan).Action.Should().Be(TickCarrierAction.Cross, "a marked short man is not worth it");
    }

    [Fact]
    public void With_nobody_in_the_area_the_corner_goes_to_the_penalty_spot()
    {
        var scene = new Scene(TickRestart.Corner(homeTakes: true, ballY: 100));
        var plan = scene.Place();
        var decision = scene.Decide(plan);

        decision.Action.Should().Be(TickCarrierAction.Cross);
        decision.Receiver.Should().Be(-1);
        decision.Target.Should().Be(new SpatialPoint(8_900, 3_500));
    }

    // ---- Free kick -----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(7_300, 3_500, true)]
    [InlineData(8_200, 5_000, true)]
    [InlineData(7_000, 3_500, true)]
    [InlineData(6_900, 3_500, false)]
    [InlineData(8_700, 5_200, false)]
    [InlineData(5_000, 3_500, false)]
    public void A_free_kick_is_a_shot_within_31_metres_and_45_degrees_of_the_goals_axis(int x, int y, bool shot) =>
        TickSetPieces.IsShootingRange(x, y).Should().Be(shot);

    [Theory]
    [InlineData(7_300, 3_500, 3)]
    [InlineData(8_000, 3_500, 4)]
    [InlineData(8_600, 5_100, 3)]
    [InlineData(8_000, 5_100, 2)]
    [InlineData(7_600, 5_100, 2)]
    public void The_wall_is_three_men_one_more_close_in_and_one_fewer_wide(int x, int y, int size) =>
        TickSetPieces.WallSize(x, y).Should().Be(size);

    [Fact]
    public void A_shot_is_taken_by_the_best_striker_of_the_ball_among_those_near_it()
    {
        var restart = TickRestart.FreeKick(homeTakes: true, 7_300, 3_500);
        var scene = new Scene(restart, takerSkills: [(6, MatchAttributeName.SetPieces, 19), (6, MatchAttributeName.Technique, 18), (6, MatchAttributeName.Finishing, 12)]);
        var plan = scene.Place();

        plan.TakerIndex.Should().Be(6);
        Distance(scene.Own[6], scene.Spot).Should().BeInRange(TickSetPieces.FreeKickStandBack - 5, TickSetPieces.FreeKickStandBack + 5);
        scene.Own[6].X.Should().BeLessThan(scene.Spot.X, "behind the ball");
    }

    [Fact]
    public void A_distant_specialist_does_not_take_the_free_kick()
    {
        var scene = new Scene(
            TickRestart.FreeKick(homeTakes: true, 7_300, 3_500),
            takerSkills: [(2, MatchAttributeName.SetPieces, 20), (2, MatchAttributeName.Technique, 20), (2, MatchAttributeName.Finishing, 20)]);

        scene.Place().TakerIndex.Should().NotBe(2, "he is further than 37 m from the ball");
    }

    [Fact]
    public void A_wall_stands_nine_metres_out_on_the_line_to_goal_covering_the_near_post_and_the_keeper_takes_the_far_side()
    {
        var restart = TickRestart.FreeKick(homeTakes: true, 7_300, 2_600);
        var scene = new Scene(restart);

        scene.Place();

        var size = TickSetPieces.WallSize(7_300, 2_600);
        var wall = scene.Opp.Skip(1).Where(point => Math.Abs(Distance(point, scene.Spot) - TickSetPieces.WallDistance) <= 90).ToArray();

        wall.Should().HaveCount(size);

        // The wall's middle lies on the line from the ball towards the near post's side of the goal.
        var middleY = wall.Average(point => point.Y);
        var lineY = 2_600 + ((3_500 - TickSetPieces.WallCover - 2_600) * (double)TickSetPieces.WallDistance / (10_000 - 7_300));

        middleY.Should().BeApproximately(lineY, 120);
        scene.Opp[0].Y.Should().BeGreaterThan(3_500, "the ball is on the low side, so the keeper covers the high side");
        scene.Opp[0].X.Should().BeGreaterThan(9_800);
    }

    [Fact]
    public void Nobody_else_defending_a_free_kick_is_inside_nine_metres_of_the_ball()
    {
        var scene = new Scene(TickRestart.FreeKick(homeTakes: true, 6_000, 3_500));

        scene.Place();

        foreach (var point in scene.Opp)
        {
            Distance(point, scene.Spot).Should().BeGreaterThanOrEqualTo(TickSetPieces.ClearanceRadius - 5);
        }
    }

    [Fact]
    public void A_free_kick_shot_goes_for_the_far_corner_away_from_the_wall()
    {
        var low = new Scene(TickRestart.FreeKick(homeTakes: true, 7_300, 2_600));
        var high = new Scene(TickRestart.FreeKick(homeTakes: true, 7_300, 4_400));
        var lowDecision = low.Decide(low.Place());
        var highDecision = high.Decide(high.Place());

        lowDecision.Action.Should().Be(TickCarrierAction.Shoot);
        lowDecision.Target.Y.Should().Be(3_500 + (TickSpatialUnits.GoalMouthMaxUnits - 3_500 - TickBallCarrierBrain.ShotAimOffset), "far post from the low side");
        highDecision.Target.Y.Should().Be(3_500 - (TickSpatialUnits.GoalMouthMaxUnits - 3_500 - TickBallCarrierBrain.ShotAimOffset));
        lowDecision.Target.X.Should().BeGreaterThan(10_000, "the aim is beyond the line");
    }

    [Fact]
    public void A_free_kick_shot_is_lofted_over_the_wall_and_into_the_goal()
    {
        var restart = TickRestart.FreeKick(homeTakes: true, 7_300, 3_500);
        var scene = new Scene(restart);
        var plan = scene.Place();
        var decision = scene.Decide(plan);
        var skills = Skill(20);
        var goals = 0;
        var cleared = true;

        for (ulong seed = 1; seed <= 30; seed++)
        {
            var ball = new TickBallPhysics();

            ball.PlaceAt(restart.SpotX, restart.SpotY);
            TickSetPieces.Execute(TickRestartKind.FreeKick, decision, skills, ball, new Pcg32(seed)).Should().BeTrue();

            var wallX = restart.SpotX + TickSetPieces.WallDistance;
            var wallHeight = -1;
            var boundary = TickBallBoundary.InPlay;

            for (var tick = 0; tick < 80 && boundary == TickBallBoundary.InPlay; tick++)
            {
                boundary = ball.Step();

                if (wallHeight < 0 && ball.UnitX >= wallX)
                {
                    wallHeight = ball.UnitZ;
                }
            }

            cleared &= wallHeight > TickBallPhysics.HeadReachZUnits;
            goals += boundary == TickBallBoundary.GoalAwayEnd ? 1 : 0;
        }

        cleared.Should().BeTrue("the ball is above a jumping wall player's reach as it passes the wall");
        goals.Should().BeGreaterThanOrEqualTo(20, "a 20-rated striker's free kick from 27 m is on target most of the time");
    }

    [Fact]
    public void A_free_kick_is_crossed_from_wide_and_far_to_the_header_with_the_fewest_defenders_on_him()
    {
        var restart = TickRestart.FreeKick(homeTakes: true, 7_000, 800);
        var scene = new Scene(restart);
        var plan = scene.Place();

        TickSetPieces.IsShootingRange(7_000, 800).Should().BeFalse();

        var inBox = Enumerable.Range(1, 10).Where(index => index != plan.TakerIndex && scene.Own[index].X >= 8_350 && scene.Own[index].Y is >= 1_500 and <= 5_500).ToArray();

        inBox.Should().HaveCount(TickSetPieces.FreeKickHeaders);

        scene.StandAttackers();
        scene.StandOpponents();

        var decision = scene.Decide(plan);

        decision.Action.Should().Be(TickCarrierAction.Cross);
        inBox.Should().Contain(decision.Receiver);
    }

    [Fact]
    public void A_deep_free_kick_is_played_short_to_a_free_teammate()
    {
        var scene = new Scene(TickRestart.FreeKick(homeTakes: true, 4_000, 3_500));
        var plan = scene.Place();

        scene.StandAttackers();
        scene.StandOpponents();

        var decision = scene.Decide(plan);

        decision.Action.Should().Be(TickCarrierAction.Pass);
        decision.Receiver.Should().BeGreaterThan(0).And.NotBe(plan.TakerIndex);
    }

    // ---- Penalty -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_best_penalty_taker_stands_behind_the_spot_and_the_keeper_on_his_line()
    {
        var restart = TickRestart.Penalty(homeTakes: true);
        var scene = new Scene(
            restart,
            takerSkills: [(9, MatchAttributeName.SetPieces, 18), (9, MatchAttributeName.Finishing, 17), (9, MatchAttributeName.Composure, 16)]);
        var plan = scene.Place();

        plan.TakerIndex.Should().Be(9);
        scene.Own[9].Should().Be(new SpatialPoint(restart.SpotX - TickSetPieces.PenaltyStandBack, 3_500));
        scene.Opp[0].Should().Be(new SpatialPoint(9_950, 3_500));
    }

    [Fact]
    public void A_goalkeeper_never_takes_a_penalty_however_good_his_feet()
    {
        var scene = new Scene(
            TickRestart.Penalty(homeTakes: true),
            takerSkills: [(0, MatchAttributeName.SetPieces, 20), (0, MatchAttributeName.Finishing, 20), (0, MatchAttributeName.Composure, 20)]);

        scene.Place().TakerIndex.Should().NotBe(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Everybody_else_is_outside_the_area_and_the_arc_for_a_penalty(bool homeTakes)
    {
        var restart = TickRestart.Penalty(homeTakes);
        var scene = new Scene(restart);
        var plan = scene.Place();
        var spot = new SpatialPoint(8_900, 3_500);

        for (var index = 1; index <= 10; index++)
        {
            foreach (var point in new[] { index == plan.TakerIndex ? (SpatialPoint?)null : scene.Own[index], scene.Opp[index] })
            {
                if (point is not { } p)
                {
                    continue;
                }

                (p.X >= 8_350 && p.Y is >= 1_500 and <= 5_500).Should().BeFalse($"{p} is in the area");
                Distance(p, spot).Should().BeGreaterThanOrEqualTo(TickSetPieces.ClearanceRadius - 5, $"{p} is inside the arc");
            }
        }
    }

    [Fact]
    public void The_penalty_goes_into_the_corner_the_keeper_is_not_standing_towards()
    {
        var scene = new Scene(TickRestart.Penalty(homeTakes: true));
        var plan = scene.Place();

        scene.StandOpponent(0, new TickPlayerState { X = 9_950_000, Y = 3_300_000 });
        scene.Decide(plan).Target.Y.Should().Be(TickSpatialUnits.GoalMouthMaxUnits - TickSetPieces.PenaltyAimOffset, "he is on the low side, so it goes high");

        scene.StandOpponent(0, new TickPlayerState { X = 9_950_000, Y = 3_700_000 });
        scene.Decide(plan).Target.Y.Should().Be(TickSpatialUnits.GoalMouthMinUnits + TickSetPieces.PenaltyAimOffset);
    }

    [Fact]
    public void A_composed_taker_feels_the_penalty_less_and_a_good_one_scores_it_nearly_always()
    {
        var restart = TickRestart.Penalty(homeTakes: true);
        var calm = new Scene(restart, takerSkills: [(9, MatchAttributeName.Composure, 20), (9, MatchAttributeName.SetPieces, 20), (9, MatchAttributeName.Finishing, 20)]);
        var nervous = new Scene(restart, takerSkills: [(9, MatchAttributeName.Composure, 2), (9, MatchAttributeName.SetPieces, 20), (9, MatchAttributeName.Finishing, 20)]);
        var calmPlan = calm.Place();
        var nervousPlan = nervous.Place();
        var calmDecision = calm.Decide(calmPlan);

        calmPlan.TakerIndex.Should().Be(9);
        nervousPlan.TakerIndex.Should().Be(9);
        calmDecision.EffectivePressure.Should().BeLessThan(nervous.Decide(nervousPlan).EffectivePressure);

        var scored = 0;

        for (ulong seed = 1; seed <= 40; seed++)
        {
            var ball = new TickBallPhysics();

            ball.PlaceAt(restart.SpotX, restart.SpotY);
            TickSetPieces.Execute(TickRestartKind.Penalty, calmDecision, Skill(20), ball, new Pcg32(seed));

            var boundary = TickBallBoundary.InPlay;

            for (var tick = 0; tick < 40 && boundary == TickBallBoundary.InPlay; tick++)
            {
                boundary = ball.Step();
            }

            scored += boundary == TickBallBoundary.GoalAwayEnd ? 1 : 0;
        }

        scored.Should().BeGreaterThanOrEqualTo(34);
    }

    // ---- Every restart -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("KickOff")]
    [InlineData("GoalKick")]
    [InlineData("Corner")]
    [InlineData("ThrowIn")]
    [InlineData("FreeKick")]
    [InlineData("Penalty")]
    public void The_away_side_is_the_home_side_turned_half_way_round(string kindName)
    {
        var kind = Enum.Parse<TickRestartKind>(kindName);
        var home = new Scene(Restart(kind, homeTakes: true));
        var away = new Scene(Restart(kind, homeTakes: false));
        var homePlan = home.Place();
        var awayPlan = away.Place();

        awayPlan.Should().Be(homePlan);
        away.Own.Should().Equal(home.Own);
        away.Opp.Should().Equal(home.Opp);
    }

    [Theory]
    [InlineData("KickOff")]
    [InlineData("GoalKick")]
    [InlineData("Corner")]
    [InlineData("ThrowIn")]
    [InlineData("FreeKick")]
    [InlineData("Penalty")]
    public void The_placement_is_the_same_on_every_tick_of_the_hold_and_every_target_is_on_the_pitch(string kindName)
    {
        var scene = new Scene(Restart(Enum.Parse<TickRestartKind>(kindName), homeTakes: true));
        var plan = scene.Place();
        var first = scene.Own.Concat(scene.Opp).ToArray();

        scene.MovePlayersAbout();

        scene.Place().Should().Be(plan, "the taker does not change his mind as the players move");
        scene.Own.Concat(scene.Opp).Should().Equal(first);

        foreach (var point in first)
        {
            point.X.Should().BeInRange(0, 10_000);
            point.Y.Should().BeInRange(0, 7_000);
        }
    }

    [Theory]
    [InlineData("KickOff")]
    [InlineData("GoalKick")]
    [InlineData("Corner")]
    [InlineData("ThrowIn")]
    [InlineData("FreeKick")]
    [InlineData("Penalty")]
    public void Executing_a_restart_takes_exactly_one_draw_and_launches_the_ball(string kindName)
    {
        var kind = Enum.Parse<TickRestartKind>(kindName);
        var restart = Restart(kind, homeTakes: true);
        var scene = new Scene(restart);
        var plan = scene.Place();

        scene.StandAttackers();
        scene.StandOpponents();

        var decision = scene.Decide(plan);
        var ball = new TickBallPhysics();
        var used = new Pcg32(5);
        var fresh = new Pcg32(5);

        ball.PlaceAt(restart.SpotX, restart.SpotY);
        TickSetPieces.Execute(kind, decision, Skill(12), ball, used).Should().BeTrue();
        fresh.NextRange(-1, 1);

        used.NextUInt32().Should().Be(fresh.NextUInt32(), "one draw, as the brain's kicks take");
        (ball.VelocityX != 0 || ball.VelocityY != 0).Should().BeTrue();
    }

    [Fact]
    public void A_taker_is_ready_when_he_is_at_the_ball_and_has_stopped()
    {
        var ballX = 5_000_000;
        var ballY = 3_500_000;
        var at = new TickPlayerState { X = ballX - 100_000, Y = ballY, Speed = 0 };
        var far = new TickPlayerState { X = ballX - 900_000, Y = ballY, Speed = 0 };
        var running = new TickPlayerState { X = ballX - 100_000, Y = ballY, Speed = TickSpatialUnits.SpeedToFixedPerTick(600) };

        TickSetPieces.IsTakerReady(at, ballX, ballY).Should().BeTrue();
        TickSetPieces.IsTakerReady(far, ballX, ballY).Should().BeFalse();
        TickSetPieces.IsTakerReady(running, ballX, ballY).Should().BeFalse("he is still running on to it");
    }

    [Fact]
    public void Placing_and_deciding_every_restart_allocates_nothing()
    {
        var kinds = Enum.GetValues<TickRestartKind>();
        var scenes = kinds.Select(kind => new Scene(Restart(kind, homeTakes: true))).ToArray();

        static int Run(Scene[] scenes, int repeats)
        {
            var hash = 0;

            for (var repeat = 0; repeat < repeats; repeat++)
            {
                foreach (var scene in scenes)
                {
                    var plan = scene.Place();
                    var decision = scene.Decide(plan);

                    hash = unchecked((hash * 31) + plan.TakerIndex + decision.Target.X + (int)decision.Action + scene.OwnTarget(3).Y + scene.OppTarget(4).X);
                }
            }

            return hash;
        }

        var first = Run(scenes, 200);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run(scenes, 200);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);
        allocated.Should().BeLessThan(256);
    }

    private static TickRestart Restart(TickRestartKind kind, bool homeTakes)
    {
        var home = kind switch
        {
            TickRestartKind.KickOff => TickRestart.KickOff(true),
            TickRestartKind.GoalKick => TickRestart.GoalKick(true, 3_300),
            TickRestartKind.Corner => TickRestart.Corner(true, 100),
            TickRestartKind.ThrowIn => TickRestart.ThrowIn(true, 6_200, 0),
            TickRestartKind.FreeKick => TickRestart.FreeKick(true, 7_300, 2_800),
            _ => TickRestart.Penalty(true),
        };

        return homeTakes
            ? home
            : home with { TakerIsHome = false, SpotX = 10_000 - home.SpotX, SpotY = 7_000 - home.SpotY };
    }

    private static long Distance(SpatialPoint one, SpatialPoint other)
    {
        long dx = one.X - other.X;
        long dy = one.Y - other.Y;

        return SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    private static TickPlayerSkills Skill(int value) =>
        TickPlayerSkills.From(PlayerAttributesV1.From(Enumerable.Repeat(value, MatchAttributeNames.Count).ToArray()));

    /// <summary>One restart with both sides on the pitch, and everything the set-piece code reads.</summary>
    private sealed class Scene
    {
        private readonly TickAnchorSpec[] _otherSpecs;
        private readonly TickPlayerSkills[] _otherSkills;
        private readonly SpatialPoint[] _ownTargets = new SpatialPoint[11];
        private readonly SpatialPoint[] _oppTargets = new SpatialPoint[11];
        private readonly MatchPassingStyle _passing;
        private readonly TickTeamStyle _style = TickTeamStyle.From(new MatchInstructionsV1());

        public Scene(
            TickRestart restart,
            TickAnchorSpec[]? takerSpecs = null,
            TickAnchorSpec[]? otherSpecs = null,
            (int Index, MatchAttributeName Attribute, int Value)[]? takerSkills = null,
            (int Index, MatchAttributeName Attribute, int Value)[]? otherSkills = null,
            MatchPassingStyle passing = MatchPassingStyle.MixedPassing)
        {
            Restart = restart;
            Specs = takerSpecs ?? FourFourTwo;
            _otherSpecs = otherSpecs ?? FourFourTwo;
            TakerSkills = Skills(takerSkills);
            _otherSkills = Skills(otherSkills);
            _passing = passing;
            Takers = [.. Specs.Select(spec => Stand(restart.TakerIsHome, spec.OwnX, spec.OwnY))];
            Others = [.. _otherSpecs.Select(spec => Stand(!restart.TakerIsHome, spec.OwnX, spec.OwnY))];
        }

        public TickRestart Restart { get; }

        public TickAnchorSpec[] Specs { get; }

        public TickPlayerSkills[] TakerSkills { get; }

        public TickPlayerState[] Takers { get; }

        public TickPlayerState[] Others { get; }

        public SpatialPoint Spot => Pov(new SpatialPoint(Restart.SpotX, Restart.SpotY));

        /// <summary>Gets the taking side's targets in the taker's point of view.</summary>
        public SpatialPoint[] Own => [.. _ownTargets.Take(Specs.Length).Select(Pov)];

        /// <summary>Gets the other side's targets in the taker's point of view.</summary>
        public SpatialPoint[] Opp => [.. _oppTargets.Take(_otherSpecs.Length).Select(Pov)];

        public SpatialPoint OwnTarget(int index) => _ownTargets[index];

        public SpatialPoint OppTarget(int index) => _oppTargets[index];

        public TickSetPiecePlan Place() =>
            TickSetPieces.Place(Situation(), _ownTargets, _oppTargets);

        public TickCarrierDecision Decide(TickSetPiecePlan plan) =>
            TickSetPieces.Decide(Situation(), plan);

        /// <summary>Gets the taking side's formation anchor for a player, in the taker's point of view.</summary>
        public SpatialPoint Anchor(int index) => new(Specs[index].OwnX, Specs[index].OwnY);

        /// <summary>Gets the indices of the n outfield players whose anchors lie nearest the restart spot (resolved with the ball there).</summary>
        public int[] Nearest(int count)
        {
            var style = TickTeamStyle.From(new MatchInstructionsV1());

            return [.. Enumerable.Range(1, Specs.Length - 1)
                .OrderBy(index =>
                {
                    var anchor = TickTacticalGeometry.Resolve(Specs[index], style, Restart.TakerIsHome, true, Restart.SpotX, Restart.SpotY);

                    return Distance(Pov(anchor), Spot);
                })
                .ThenBy(index => index)
                .Take(count)];
        }

        /// <summary>Puts the taking side where the last <see cref="Place"/> sent it.</summary>
        public void StandAttackers()
        {
            for (var index = 0; index < Takers.Length; index++)
            {
                Takers[index] = TickPlayerState.Standing(_ownTargets[index].X, _ownTargets[index].Y, 0, 10_000);
            }
        }

        /// <summary>Puts the other side where the last <see cref="Place"/> sent it.</summary>
        public void StandOpponents()
        {
            for (var index = 0; index < Others.Length; index++)
            {
                Others[index] = TickPlayerState.Standing(_oppTargets[index].X, _oppTargets[index].Y, 0, 10_000);
            }
        }

        /// <summary>Takes every opponent away from the box.</summary>
        public void ClearOpponents()
        {
            for (var index = 0; index < Others.Length; index++)
            {
                Others[index] = TickPlayerState.Standing(Restart.TakerIsHome ? 2_000 : 8_000, 3_500, 0, 10_000);
            }
        }

        /// <summary>Stands an opponent on the spot a player stands, so the crowd round him is counted.</summary>
        public void StandOpponent(int index, TickPlayerState where) => Others[index] = where;

        /// <summary>Stands an opponent on top of a teammate.</summary>
        public void CrowdOpponent(int opponent, int teammate) =>
            Others[opponent] = TickPlayerState.Standing(Takers[teammate].X / 1_000, Takers[teammate].Y / 1_000, 0, 10_000);

        /// <summary>Shuffles the players, as a few ticks of the hold would.</summary>
        public void MovePlayersAbout()
        {
            for (var index = 0; index < Takers.Length; index++)
            {
                Takers[index].X = Math.Clamp(Takers[index].X + ((index * 137_000) % 900_000) - 300_000, 0, 10_000_000);
                Takers[index].Y = Math.Clamp(Takers[index].Y + ((index * 91_000) % 700_000) - 200_000, 0, 7_000_000);
            }

            for (var index = 0; index < Others.Length; index++)
            {
                Others[index].X = Math.Clamp(Others[index].X - ((index * 113_000) % 800_000) + 250_000, 0, 10_000_000);
                Others[index].Y = Math.Clamp(Others[index].Y + ((index * 77_000) % 600_000) - 100_000, 0, 7_000_000);
            }
        }

        private static TickPlayerState Stand(bool home, int ownX, int ownY) =>
            TickPlayerState.Standing(home ? ownX : 10_000 - ownX, home ? ownY : 7_000 - ownY, 0, 10_000);

        private static TickPlayerSkills[] Skills((int Index, MatchAttributeName Attribute, int Value)[]? overrides)
        {
            var values = new int[11][];

            for (var index = 0; index < values.Length; index++)
            {
                values[index] = Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray();
            }

            foreach (var (index, attribute, value) in overrides ?? [])
            {
                values[index][(int)attribute] = value;
            }

            return [.. values.Select(row => TickPlayerSkills.From(PlayerAttributesV1.From(row)))];
        }

        private SpatialPoint Pov(SpatialPoint point) =>
            Restart.TakerIsHome ? point : new SpatialPoint(10_000 - point.X, 7_000 - point.Y);

        private TickSetPieceSituation Situation() => new()
        {
            Restart = Restart,
            Takers = Takers,
            TakerSpecs = Specs,
            TakerSkills = TakerSkills,
            TakerStyle = _style,
            TakerPassing = _passing,
            Others = Others,
            OtherSpecs = _otherSpecs,
            OtherSkills = _otherSkills,
            OtherStyle = _style,
        };
    }
}
