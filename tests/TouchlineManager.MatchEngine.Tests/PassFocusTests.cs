using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The pass focus a manager sets steers the ball through the lanes asked for (`engine-v8`).
/// </summary>
/// <remarks>
/// The instruction moves where each possession's approach ends, so it is measured where the ball goes: the
/// lateral position of every pass and cross the side made, on its own scale. A possession starts where the last
/// one finished, which is mostly in the middle, so even a side with no preference sends about half its ball
/// down the centre; the focus is therefore measured against that baseline, not against thirds.
/// </remarks>
public sealed class PassFocusTests
{
    private const int Matches = 60;

    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    [Fact]
    public void No_preference_leaves_the_draw_unchanged()
    {
        for (var draw = 0; draw < SpatialPitch.PitchWidth; draw += 7)
        {
            PassagePlanner.FocusLateral(draw, MatchPassFocus.Balanced, Rules).Should().Be(draw);
        }
    }

    [Theory]
    [InlineData(MatchPassFocus.Centre)]
    [InlineData(MatchPassFocus.CentreAndLeft)]
    [InlineData(MatchPassFocus.CentreAndRight)]
    [InlineData(MatchPassFocus.Wings)]
    public void A_focused_draw_stays_on_the_pitch_and_moves_with_the_draw(MatchPassFocus focus)
    {
        var previous = -1;

        for (var draw = 0; draw < SpatialPitch.PitchWidth; draw++)
        {
            var lateral = PassagePlanner.FocusLateral(draw, focus, Rules);

            lateral.Should().BeInRange(0, SpatialPitch.PitchWidth - 1);
            lateral.Should().BeGreaterThanOrEqualTo(previous, "a larger draw never lands further left");

            previous = lateral;
        }
    }

    [Theory]
    [InlineData(MatchPassFocus.Centre, 30, 40, 30)]
    [InlineData(MatchPassFocus.CentreAndLeft, 42, 27, 31)]
    [InlineData(MatchPassFocus.CentreAndRight, 31, 27, 42)]
    [InlineData(MatchPassFocus.Wings, 46, 8, 46)]
    public void A_uniform_draw_lands_in_the_lanes_in_the_rules_shares(MatchPassFocus focus, int left, int centre, int right)
    {
        var counts = new int[3];

        for (var draw = 0; draw < SpatialPitch.PitchWidth; draw++)
        {
            counts[LaneOf(PassagePlanner.FocusLateral(draw, focus, Rules))]++;
        }

        Share(counts[0]).Should().BeApproximately(left, 1);
        Share(counts[1]).Should().BeApproximately(centre, 1);
        Share(counts[2]).Should().BeApproximately(right, 1);
    }

    [Fact]
    public void A_central_focus_sends_more_of_the_ball_through_the_middle()
    {
        var focused = Shares(MatchPassFocus.Centre);
        var balanced = Shares(MatchPassFocus.Balanced);

        focused[1].Should().BeGreaterThanOrEqualTo(balanced[1] + 6);
        focused[0].Should().BeLessThan(balanced[0]);
        focused[2].Should().BeLessThan(balanced[2]);
    }

    [Fact]
    public void A_left_focus_sends_more_down_the_left_than_the_right()
    {
        var focused = Shares(MatchPassFocus.CentreAndLeft);
        var balanced = Shares(MatchPassFocus.Balanced);

        focused[0].Should().BeGreaterThanOrEqualTo(balanced[0] + 4);
        focused[0].Should().BeGreaterThanOrEqualTo(focused[2] + 8);
        focused[2].Should().BeLessThan(balanced[2]);
    }

    [Fact]
    public void A_right_focus_sends_more_down_the_right_than_the_left()
    {
        var focused = Shares(MatchPassFocus.CentreAndRight);
        var balanced = Shares(MatchPassFocus.Balanced);

        focused[2].Should().BeGreaterThanOrEqualTo(balanced[2] + 4);
        focused[2].Should().BeGreaterThanOrEqualTo(focused[0] + 8);
        focused[0].Should().BeLessThan(balanced[0]);
    }

    [Fact]
    public void A_left_focus_and_a_right_focus_are_mirror_images()
    {
        var left = Shares(MatchPassFocus.CentreAndLeft);
        var right = Shares(MatchPassFocus.CentreAndRight);

        left[0].Should().BeCloseTo(right[2], 4);
        left[2].Should().BeCloseTo(right[0], 4);
        left[1].Should().BeCloseTo(right[1], 4);
    }

    [Fact]
    public void A_sides_focus_does_not_steer_the_other_sides_ball()
    {
        var steered = LaneShares(new MatchInstructionsV1 { PassFocus = MatchPassFocus.Centre }, new MatchInstructionsV1(), MatchSide.Away);
        var untouched = LaneShares(new MatchInstructionsV1(), new MatchInstructionsV1(), MatchSide.Away);

        for (var lane = 0; lane < 3; lane++)
        {
            steered[lane].Should().BeCloseTo(untouched[lane], 4);
        }
    }

    [Fact]
    public void The_away_side_is_steered_towards_its_own_left()
    {
        var focused = LaneShares(new MatchInstructionsV1(), new MatchInstructionsV1 { PassFocus = MatchPassFocus.CentreAndLeft }, MatchSide.Away);

        focused[0].Should().BeGreaterThanOrEqualTo(focused[2] + 8, "left is the side's own left, whichever end it attacks");
    }

    [Theory]
    [InlineData(MatchPassFocus.Balanced, 23, 53, 23)]
    [InlineData(MatchPassFocus.Centre, 20, 60, 20)]
    [InlineData(MatchPassFocus.Wings, 38, 22, 38)]
    [InlineData(MatchPassFocus.CentreAndLeft, 37, 43, 20)]
    [InlineData(MatchPassFocus.CentreAndRight, 20, 43, 37)]
    public void The_ball_is_measured_in_the_lane_shares_the_focus_is_calibrated_to(
        MatchPassFocus focus,
        int left,
        int centre,
        int right)
    {
        var shares = Shares(focus);

        shares[0].Should().BeCloseTo(left, 3);
        shares[1].Should().BeCloseTo(centre, 3);
        shares[2].Should().BeCloseTo(right, 3);
    }

    [Fact]
    public void Both_wings_send_more_of_the_ball_wide_than_either_single_flank_focus()
    {
        var wings = Shares(MatchPassFocus.Wings);
        var left = Shares(MatchPassFocus.CentreAndLeft);

        (wings[0] + wings[2]).Should().BeGreaterThan(left[0] + left[2]);
        wings[1].Should().BeLessThan(left[1]);
        wings[0].Should().BeCloseTo(wings[2], 3, "the wings are symmetric");
    }

    [Fact]
    public void The_same_focus_replays_the_same_match()
    {
        var instructions = new MatchInstructionsV1 { PassFocus = MatchPassFocus.CentreAndLeft };
        var input = TestMatchFactory.WithInstructions(instructions, new MatchInstructionsV1(), 4_211);

        MatchSimulator.Simulate(input).OutputHash.Should().Be(MatchSimulator.Simulate(input).OutputHash);
    }

    [Fact]
    public void The_focus_is_part_of_the_snapshots_identity()
    {
        var balanced = TestMatchFactory.Even(4_211);
        var focused = TestMatchFactory.WithInstructions(
            new MatchInstructionsV1 { PassFocus = MatchPassFocus.Centre },
            new MatchInstructionsV1(),
            4_211);

        MatchSimulator.Simulate(focused).InputHash.Should().NotBe(MatchSimulator.Simulate(balanced).InputHash);
    }

    private static int[] Shares(MatchPassFocus focus) =>
        LaneShares(new MatchInstructionsV1 { PassFocus = focus }, new MatchInstructionsV1(), MatchSide.Home);

    /// <summary>The percentage of a side's possessions whose approach ended in each lane: left, centre, right.</summary>
    private static int[] LaneShares(MatchInstructionsV1 home, MatchInstructionsV1 away, MatchSide measured)
    {
        var counts = new int[3];

        for (var seed = 1; seed <= Matches; seed++)
        {
            var input = TestMatchFactory.WithInstructions(home, away, (ulong)seed * 4_211UL);
            var recorder = new MatchPassageRecorder();

            MatchSimulator.Simulate(input, Rules, passages: recorder);

            foreach (var passage in recorder.Passages.Where(passage => passage.Side == measured))
            {
                foreach (var waypoint in passage.Waypoints.Where(waypoint => waypoint.Kind is PassageWaypointKind.Pass or PassageWaypointKind.Cross))
                {
                    counts[LaneOf(PassageTestHelpers.AttackingY(waypoint.Y, measured))]++;
                }
            }
        }

        var total = counts.Sum();

        return [.. counts.Select(count => (int)(100L * count / total))];
    }

    private static int LaneOf(int attackingY) =>
        attackingY < Rules.PassLeftLaneMaxYBasisPoints ? 0 : attackingY < Rules.PassRightLaneMinYBasisPoints ? 1 : 2;

    private static double Share(int count) => 100.0 * count / SpatialPitch.PitchWidth;
}
