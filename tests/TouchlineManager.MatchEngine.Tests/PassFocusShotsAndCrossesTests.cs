using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The pass focus moves the shots and the crosses as well as the passes (`engine-v9`).
/// </summary>
/// <remarks>
/// A side that asks for a lane takes its open-play shots from the zones on that side, crosses from the flank the
/// ball arrives in, and takes more or fewer shots to pay for where it shoots from. The shares are measured on
/// the events a match produces, which include the corners, free kicks and penalties that are always taken from
/// the middle, so the measured central share sits above the rules' open-play share.
/// </remarks>
public sealed class PassFocusShotsAndCrossesTests
{
    private const int Matches = 80;

    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    [Theory]
    [InlineData(MatchPassFocus.Balanced)]
    [InlineData(MatchPassFocus.Centre)]
    [InlineData(MatchPassFocus.CentreAndLeft)]
    [InlineData(MatchPassFocus.CentreAndRight)]
    [InlineData(MatchPassFocus.Wings)]
    public void Every_focus_shares_its_shots_across_the_five_zones_in_one_hundred_percent(MatchPassFocus focus)
    {
        var (central, insideLeft, wideLeft, insideRight, wideRight) = PassagePlanner.ShotZoneShares(focus, Rules);

        (central + insideLeft + wideLeft + insideRight + wideRight).Should().Be(100);
    }

    [Fact]
    public void No_preference_keeps_the_shot_zones_the_engine_always_had()
    {
        PassagePlanner.ShotZoneShares(MatchPassFocus.Balanced, Rules).Should().Be((40, 20, 10, 20, 10));
        PassagePlanner.ChanceVolume(MatchPassFocus.Balanced, Rules).Should().Be(EngineRulesV2.Certain);
    }

    [Fact]
    public void A_left_focus_and_a_right_focus_mirror_the_zones()
    {
        var left = PassagePlanner.ShotZoneShares(MatchPassFocus.CentreAndLeft, Rules);
        var right = PassagePlanner.ShotZoneShares(MatchPassFocus.CentreAndRight, Rules);

        right.Should().Be((left.Central, left.InsideRight, left.WideRight, left.InsideLeft, left.WideLeft));
        PassagePlanner.ChanceVolume(MatchPassFocus.CentreAndLeft, Rules)
            .Should().Be(PassagePlanner.ChanceVolume(MatchPassFocus.CentreAndRight, Rules));
    }

    [Fact]
    public void The_centre_shoots_less_often_and_the_wings_more_often_than_no_preference()
    {
        PassagePlanner.ChanceVolume(MatchPassFocus.Centre, Rules).Should().BeLessThan(EngineRulesV2.Certain);
        PassagePlanner.ChanceVolume(MatchPassFocus.Wings, Rules).Should().BeGreaterThan(EngineRulesV2.Certain);
    }

    [Fact]
    public void A_central_focus_takes_most_of_its_shots_from_the_middle()
    {
        var shots = Measure(MatchPassFocus.Centre).ShotLanes;
        var balanced = Measure(MatchPassFocus.Balanced).ShotLanes;

        shots[1].Should().BeGreaterThanOrEqualTo(balanced[1] + 8);
        shots[1].Should().BeGreaterThanOrEqualTo(shots[0] + 30);
        shots[1].Should().BeGreaterThanOrEqualTo(shots[2] + 30);
    }

    [Fact]
    public void A_left_focus_takes_more_of_its_shots_from_the_left_than_the_right()
    {
        var shots = Measure(MatchPassFocus.CentreAndLeft).ShotLanes;
        var balanced = Measure(MatchPassFocus.Balanced).ShotLanes;

        shots[0].Should().BeGreaterThanOrEqualTo(balanced[0] + 6);
        shots[0].Should().BeGreaterThanOrEqualTo(shots[2] + 10);
        shots[2].Should().BeLessThan(balanced[2]);
    }

    [Fact]
    public void A_right_focus_takes_more_of_its_shots_from_the_right_than_the_left()
    {
        var shots = Measure(MatchPassFocus.CentreAndRight).ShotLanes;
        var balanced = Measure(MatchPassFocus.Balanced).ShotLanes;

        shots[2].Should().BeGreaterThanOrEqualTo(balanced[2] + 6);
        shots[2].Should().BeGreaterThanOrEqualTo(shots[0] + 10);
        shots[0].Should().BeLessThan(balanced[0]);
    }

    [Fact]
    public void Both_wings_take_most_of_their_shots_from_the_flanks()
    {
        var shots = Measure(MatchPassFocus.Wings).ShotLanes;
        var balanced = Measure(MatchPassFocus.Balanced).ShotLanes;

        (shots[0] + shots[2]).Should().BeGreaterThanOrEqualTo(balanced[0] + balanced[2] + 15);
        shots[0].Should().BeCloseTo(shots[2], 5, "the wings are symmetric");
    }

    [Fact]
    public void Crosses_come_from_the_flanks_even_when_the_side_has_no_preference()
    {
        var crosses = Measure(MatchPassFocus.Balanced);
        var flank = crosses.CrossLanes[0] + crosses.CrossLanes[2];

        flank.Should().BeGreaterThanOrEqualTo(85, "a ball that arrives in the middle is rarely crossed");
        crosses.CrossesPerMatch.Should().BeInRange(10.0, 18.0);
    }

    [Fact]
    public void Both_wings_cross_more_often_and_almost_only_from_the_flanks()
    {
        var wings = Measure(MatchPassFocus.Wings);
        var balanced = Measure(MatchPassFocus.Balanced);

        wings.CrossesPerMatch.Should().BeGreaterThan(balanced.CrossesPerMatch * 1.1);
        (wings.CrossLanes[0] + wings.CrossLanes[2]).Should().BeGreaterThanOrEqualTo(95);
    }

    [Fact]
    public void A_left_focus_crosses_more_from_the_left_than_the_right()
    {
        var left = Measure(MatchPassFocus.CentreAndLeft);
        var balanced = Measure(MatchPassFocus.Balanced);

        left.CrossLanes[0].Should().BeGreaterThanOrEqualTo(left.CrossLanes[2] + 8);
        (left.CrossesPerMatch * left.CrossLanes[0])
            .Should().BeGreaterThan(balanced.CrossesPerMatch * balanced.CrossLanes[0] * 1.1);
    }

    [Fact]
    public void A_centre_focus_crosses_from_the_middle_as_much_as_from_both_flanks_together()
    {
        // Measured about 24/52/24: the side plays in the middle, so a good part of its crosses come from there.
        var lanes = Measure(MatchPassFocus.Centre).CrossLanes;

        lanes[1].Should().BeInRange(46, 58);
        lanes[0].Should().BeInRange(19, 30);
        lanes[2].Should().BeInRange(19, 30);
        lanes[0].Should().BeCloseTo(lanes[2], 5, "the centre favours neither flank");
    }

    [Fact]
    public void A_left_focus_crosses_most_from_the_left_then_the_middle_and_least_from_the_right()
    {
        // Measured about 54/31/15.
        var lanes = Measure(MatchPassFocus.CentreAndLeft).CrossLanes;

        lanes[0].Should().BeInRange(48, 59);
        lanes[1].Should().BeInRange(25, 36);
        lanes[2].Should().BeInRange(10, 21);
    }

    [Fact]
    public void A_right_focus_crosses_most_from_the_right_then_the_middle_and_least_from_the_left()
    {
        var lanes = Measure(MatchPassFocus.CentreAndRight).CrossLanes;

        lanes[2].Should().BeInRange(48, 59);
        lanes[1].Should().BeInRange(25, 36);
        lanes[0].Should().BeInRange(10, 21);
    }

    [Fact]
    public void The_share_crossed_follows_the_lane_and_the_focus_and_mirrors_for_left_and_right()
    {
        PassagePlanner.CrossShare(MatchPassFocus.Balanced, PassLane.Left, Rules).Should().Be(Rules.CrossShareFlankLaneBasisPoints);
        PassagePlanner.CrossShare(MatchPassFocus.Balanced, PassLane.Centre, Rules).Should().Be(Rules.CrossShareCentreLaneBasisPoints);
        PassagePlanner.CrossShare(MatchPassFocus.Wings, PassLane.Right, Rules).Should().Be(Rules.CrossShareFlankLaneBasisPoints);

        foreach (var lane in Enum.GetValues<PassLane>())
        {
            var mirrored = lane switch { PassLane.Left => PassLane.Right, PassLane.Right => PassLane.Left, _ => PassLane.Centre };

            PassagePlanner.CrossShare(MatchPassFocus.CentreAndLeft, lane, Rules)
                .Should().Be(PassagePlanner.CrossShare(MatchPassFocus.CentreAndRight, mirrored, Rules), lane.ToString());
        }

        PassagePlanner.CrossShare(MatchPassFocus.Centre, PassLane.Centre, Rules)
            .Should().BeGreaterThan(PassagePlanner.CrossShare(MatchPassFocus.Centre, PassLane.Left, Rules));
    }

    [Fact]
    public void A_centre_focus_does_not_cross_more_than_no_preference()
    {
        Measure(MatchPassFocus.Centre).CrossesPerMatch
            .Should().BeLessThan(Measure(MatchPassFocus.Balanced).CrossesPerMatch * 1.02);
    }

    [Fact]
    public void The_wings_take_more_shots_than_the_centre_but_score_about_as_often()
    {
        var wings = Measure(MatchPassFocus.Wings);
        var centre = Measure(MatchPassFocus.Centre);
        var balanced = Measure(MatchPassFocus.Balanced);

        wings.ShotsPerMatch.Should().BeGreaterThan(centre.ShotsPerMatch * 1.1);
        wings.ShotsPerMatch.Should().BeGreaterThan(balanced.ShotsPerMatch);
        centre.ShotsPerMatch.Should().BeLessThan(balanced.ShotsPerMatch);

        // Fewer shots from the best place, more from the worst: the goals come out near where they began. The sample is
        // small enough for a goals-per-match figure to move by about 5% on its own, so the band is a fifth either way.
        wings.GoalsPerMatch.Should().BeInRange(balanced.GoalsPerMatch * 0.80, balanced.GoalsPerMatch * 1.20);
        centre.GoalsPerMatch.Should().BeInRange(balanced.GoalsPerMatch * 0.80, balanced.GoalsPerMatch * 1.20);

        // Each shot is worth more from the middle than from wide.
        (centre.GoalsPerMatch / centre.ShotsPerMatch)
            .Should().BeGreaterThan(wings.GoalsPerMatch / wings.ShotsPerMatch);
    }

    [Fact]
    public void A_sides_focus_does_not_move_the_other_sides_shots()
    {
        var steered = Measure(
            new MatchInstructionsV1 { PassFocus = MatchPassFocus.Wings },
            new MatchInstructionsV1(),
            MatchSide.Away);
        var untouched = Measure(new MatchInstructionsV1(), new MatchInstructionsV1(), MatchSide.Away);

        for (var lane = 0; lane < 3; lane++)
        {
            steered.ShotLanes[lane].Should().BeCloseTo(untouched.ShotLanes[lane], 6);
        }
    }

    private static Measurement Measure(MatchPassFocus focus) =>
        Measure(new MatchInstructionsV1 { PassFocus = focus }, new MatchInstructionsV1(), MatchSide.Home);

    private static Measurement Measure(MatchInstructionsV1 home, MatchInstructionsV1 away, MatchSide measured)
    {
        var shotLanes = new int[3];
        var crossLanes = new int[3];
        long shots = 0;
        long goals = 0;
        long crosses = 0;

        for (var seed = 1; seed <= Matches; seed++)
        {
            var input = TestMatchFactory.WithInstructions(home, away, (ulong)seed * 7_919UL);
            var recorder = new MatchPassageRecorder();
            var result = MatchSimulator.Simulate(input, Rules, passages: recorder);
            var team = measured == MatchSide.Home ? result.Home : result.Away;

            shots += team.Shots;
            goals += measured == MatchSide.Home ? result.HomeGoals : result.AwayGoals;

            foreach (var matchEvent in result.Events.Where(candidate => candidate.Side == measured && IsShot(candidate)))
            {
                shotLanes[matchEvent.Zone is ShotZone.InsideLeft or ShotZone.WideLeft
                    ? 0
                    : matchEvent.Zone is ShotZone.Central ? 1 : 2]++;
            }

            // The approach's cross is the one struck at the cross altitude; a corner is delivered at a header's.
            foreach (var passage in recorder.Passages.Where(candidate => candidate.Side == measured))
            {
                foreach (var waypoint in passage.Waypoints.Where(candidate =>
                    candidate.Kind == PassageWaypointKind.Cross && candidate.Z == Rules.CrossAltitude))
                {
                    crosses++;
                    crossLanes[LaneOf(PassageTestHelpers.AttackingY(waypoint.Y, measured))]++;
                }
            }
        }

        return new Measurement(
            Percentages(shotLanes),
            Percentages(crossLanes),
            (double)crosses / Matches,
            (double)shots / Matches,
            (double)goals / Matches);
    }

    private static bool IsShot(EngineEventV1 matchEvent) =>
        matchEvent.Zone is not null
        && matchEvent.Type is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked
            or EngineEventType.ShotOffTarget or EngineEventType.Woodwork;

    private static int LaneOf(int attackingY) =>
        attackingY < Rules.PassLeftLaneMaxYBasisPoints ? 0 : attackingY < Rules.PassRightLaneMinYBasisPoints ? 1 : 2;

    private static int[] Percentages(int[] counts)
    {
        var total = Math.Max(1, counts.Sum());

        return [.. counts.Select(count => (int)(100L * count / total))];
    }

    private sealed record Measurement(
        int[] ShotLanes,
        int[] CrossLanes,
        double CrossesPerMatch,
        double ShotsPerMatch,
        double GoalsPerMatch);
}
