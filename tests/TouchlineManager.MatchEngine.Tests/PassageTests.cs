using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The continuous passage model of `engine-v4`: the ball begins where the last possession left it, every
/// point is on the pitch, the outcome events are located where the play put them, and the recorder is a
/// by-product that can never change the result.
/// </summary>
public sealed class PassageTests
{
    /// <summary>Every event type that is a shot at goal, whatever its outcome.</summary>
    private static readonly EngineEventType[] ShotTypes =
    [
        EngineEventType.Goal,
        EngineEventType.PenaltyGoal,
        EngineEventType.PenaltyMissed,
        EngineEventType.ShotSaved,
        EngineEventType.ShotBlocked,
        EngineEventType.ShotOffTarget,
        EngineEventType.Woodwork,
        EngineEventType.FreeKickShot,
    ];

    [Fact]
    public void A_match_records_one_passage_per_possession()
    {
        var (result, passages) = SimulateWithRecorder(TestMatchFactory.Even());

        passages.Should().NotBeEmpty();
        passages.Select(passage => passage.Ordinal).Should().BeInAscendingOrder();
        passages.Should().OnlyHaveUniqueItems(passage => passage.Ordinal);

        // A possession that produced no event still has a passage, so there are at least as many passages as
        // events that happen between period boundaries.
        var inPlayEvents = result.Events.Count(matchEvent => !matchEvent.IsPeriodBoundary);
        passages.Count.Should().BeGreaterThanOrEqualTo(inPlayEvents);
    }

    [Fact]
    public void Every_waypoint_and_touch_is_on_the_pitch_and_in_order()
    {
        for (var seed = 1UL; seed <= 30; seed++)
        {
            var (_, passages) = SimulateWithRecorder(TestMatchFactory.Even(seed));

            foreach (var passage in passages)
            {
                passage.Waypoints.Should().NotBeEmpty();

                var previousFraction = -1;

                foreach (var waypoint in passage.Waypoints)
                {
                    waypoint.X.Should().BeInRange(0, SpatialPitch.PitchLength);
                    waypoint.Y.Should().BeInRange(0, SpatialPitch.PitchWidth);
                    waypoint.Z.Should().BeInRange(0, 100);
                    waypoint.FractionBasisPoints.Should().BeInRange(0, EngineRulesV2.Certain);
                    waypoint.FractionBasisPoints.Should().BeGreaterThanOrEqualTo(previousFraction);
                    previousFraction = waypoint.FractionBasisPoints;
                }

                foreach (var touch in passage.Touches)
                {
                    touch.X.Should().BeInRange(0, SpatialPitch.PitchLength);
                    touch.Y.Should().BeInRange(0, SpatialPitch.PitchWidth);
                    touch.Z.Should().BeInRange(0, 100);
                    touch.FractionBasisPoints.Should().BeInRange(0, EngineRulesV2.Certain);
                }
            }
        }
    }

    [Fact]
    public void A_passage_begins_where_the_last_one_ended_or_at_a_restart()
    {
        for (var seed = 1UL; seed <= 30; seed++)
        {
            var (_, passages) = SimulateWithRecorder(TestMatchFactory.Even(seed));

            for (var index = 1; index < passages.Count; index++)
            {
                var previous = passages[index - 1];
                var next = passages[index];

                var continues = next.StartPoint == previous.EndPoint;
                var fromCentre = next.StartPoint == SpatialPoint.Center;
                var fromGoalArea = AttackingX(next.StartPoint, next.Side) == EngineRulesV2.Default.GoalAreaXBasisPoints;

                (continues || fromCentre || fromGoalArea)
                    .Should().BeTrue(
                        $"possession {next.Ordinal} must begin where {previous.Ordinal} ended, at the centre "
                        + "spot, or at a goal-area restart");
            }
        }
    }

    [Fact]
    public void Every_shot_is_taken_from_the_attacking_third_and_in_free_kick_range()
    {
        var (result, passages) = SimulateWithRecorder(TestMatchFactory.Even());
        var passageOfSequence = new Dictionary<int, MatchPassageV1>();

        foreach (var passage in passages)
        {
            foreach (var sequence in passage.EventSequences)
            {
                passageOfSequence[sequence] = passage;
            }
        }

        var sawOpenPlayShot = false;

        foreach (var matchEvent in result.Events.Where(matchEvent => ShotTypes.Contains(matchEvent.Type)))
        {
            matchEvent.X.Should().NotBeNull();
            var attackingX = AttackingX(new SpatialPoint(matchEvent.X!.Value, matchEvent.Y!.Value), matchEvent.Side);

            attackingX.Should().BeGreaterThanOrEqualTo(
                EngineRulesV2.Default.FreeKickShootingRangeX,
                $"{matchEvent.Type} must be taken in the attacking third, never from the shooter's own half");

            var passage = passageOfSequence[matchEvent.Sequence];
            var fromFreeKick = passage.EventSequences.Any(sequence =>
                result.Events.Single(candidate => candidate.Sequence == sequence).Type == EngineEventType.FreeKickWon);

            if (!fromFreeKick)
            {
                attackingX.Should().BeGreaterThanOrEqualTo(
                    EngineRulesV2.Default.ShotFinalThirdXMinBasisPoints,
                    "an open-play shot is taken from the final third the passage aimed at");
                sawOpenPlayShot = true;
            }
        }

        sawOpenPlayShot.Should().BeTrue("a match contains open-play shots");
    }

    [Fact]
    public void Every_touch_names_a_player_who_was_in_the_match()
    {
        var input = TestMatchFactory.Even();
        var (_, passages) = SimulateWithRecorder(input);

        var known = input.Home.Squad.Concat(input.Away.Squad)
            .Select(participant => participant.ParticipantId)
            .ToHashSet();

        var touches = passages.SelectMany(passage => passage.Touches).ToList();

        touches.Should().NotBeEmpty();

        foreach (var touch in touches)
        {
            known.Should().Contain(touch.ParticipantId);
        }
    }

    [Fact]
    public void The_recorded_film_is_deterministic()
    {
        var input = TestMatchFactory.Even();

        var first = new MatchPassageRecorder();
        var second = new MatchPassageRecorder();

        MatchSimulator.Simulate(input, EngineRulesV2.Default, passages: first);
        MatchSimulator.Simulate(input, EngineRulesV2.Default, passages: second);

        first.Passages.Count.Should().Be(second.Passages.Count);

        for (var index = 0; index < first.Passages.Count; index++)
        {
            var left = first.Passages[index];
            var right = second.Passages[index];

            left.Ordinal.Should().Be(right.Ordinal);
            left.Side.Should().Be(right.Side);
            left.StartClockSeconds.Should().Be(right.StartClockSeconds);
            left.EndClockSeconds.Should().Be(right.EndClockSeconds);
            left.EventSequences.Should().Equal(right.EventSequences);
            left.Waypoints.Should().Equal(right.Waypoints);
            left.Touches.Should().Equal(right.Touches);
        }
    }

    [Fact]
    public void Recording_the_film_never_changes_the_result()
    {
        // The guard the whole side-channel design rests on: geometry is drawn from a stream of its own, so a
        // recorder attached to a simulation cannot move a single play draw.
        for (var seed = 1UL; seed <= 25; seed++)
        {
            var input = TestMatchFactory.Even(seed);

            var plain = MatchSimulator.Simulate(input);
            var recorded = MatchSimulator.Simulate(input, EngineRulesV2.Default, passages: new MatchPassageRecorder());
            var fullyRecorded = MatchSimulator.Simulate(
                input,
                EngineRulesV2.Default,
                liveMetrics: new PlayerLiveMetricsRecorder(),
                passages: new MatchPassageRecorder());

            recorded.OutputHash.Should().Be(plain.OutputHash);
            fullyRecorded.OutputHash.Should().Be(plain.OutputHash);
        }
    }

    [Fact]
    public void A_goal_restarts_the_next_passage_from_the_centre_spot()
    {
        for (var seed = 1UL; seed <= 40; seed++)
        {
            var (result, passages) = SimulateWithRecorder(TestMatchFactory.Even(seed));

            var goals = result.Events
                .Where(matchEvent => matchEvent.IsGoal)
                .Select(matchEvent => matchEvent.Sequence)
                .ToHashSet();

            if (goals.Count == 0)
            {
                continue;
            }

            for (var index = 0; index < passages.Count - 1; index++)
            {
                if (passages[index].EventSequences.Any(goals.Contains))
                {
                    passages[index + 1].StartPoint.Should().Be(
                        SpatialPoint.Center,
                        "a goal is a restart from the centre spot");
                }
            }
        }
    }

    private static (MatchResultV1 Result, IReadOnlyList<MatchPassageV1> Passages) SimulateWithRecorder(MatchInputV1 input)
    {
        var recorder = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, EngineRulesV2.Default, passages: recorder);

        return (result, recorder.Passages);
    }

    private static int AttackingX(SpatialPoint point, MatchSide side) =>
        side == MatchSide.Home ? point.X : SpatialPitch.PitchLength - point.X;
}
