using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using static TouchlineManager.MatchEngine.Tests.PassageTestHelpers;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The continuous passage model of `engine-v4` and the complete recorder of `engine-v5`: the ball begins where
/// the last possession left it, every point is on the pitch, the outcome events are located where the play put
/// them, a shot travels to the target its outcome decides, and the recorder is a by-product that can never
/// change the result.
/// </summary>
public sealed class PassageTests
{
    private const int Seeds = 60;

    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

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
            var attackingX = AttackingX(matchEvent.X!.Value, matchEvent.Side);

            attackingX.Should().BeGreaterThanOrEqualTo(
                Rules.FreeKickShootingRangeX,
                $"{matchEvent.Type} must be taken in the attacking third, never from the shooter's own half");

            var passage = passageOfSequence[matchEvent.Sequence];
            var fromFreeKick = passage.EventSequences.Any(sequence =>
                result.Events.Single(candidate => candidate.Sequence == sequence).Type == EngineEventType.FreeKickWon);

            if (!fromFreeKick)
            {
                attackingX.Should().BeGreaterThanOrEqualTo(
                    Rules.ShotFinalThirdXMinBasisPoints,
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
            left.Period.Should().Be(right.Period);
            left.StartClockSeconds.Should().Be(right.StartClockSeconds);
            left.EndClockSeconds.Should().Be(right.EndClockSeconds);
            left.Outcome.Should().Be(right.Outcome);
            left.Restart.Should().Be(right.Restart);
            left.Events.Should().Equal(right.Events);
            left.EventSequences.Should().Equal(right.EventSequences);
            left.Waypoints.Should().Equal(right.Waypoints);
            left.Touches.Should().Equal(right.Touches);
        }
    }

    [Fact]
    public void Recording_the_film_never_changes_the_result()
    {
        // The guard the whole side-channel design rests on: geometry is drawn from a stream of its own, so a
        // recorder attached to a simulation cannot move a single play draw — and since engine-v5 the play
        // reads the ball's geometry (where a restart is taken from, how deep the next possession starts), so
        // it is also what proves the geometry does not depend on a recorder being there.
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

    // ---- The recorder is complete (engine-v5) ----------------------------------------------------

    [Fact]
    public void MAT_3_Every_possession_has_real_length_and_the_possessions_tile_each_half()
    {
        for (var seed = 1UL; seed <= 30; seed++)
        {
            var (_, passages) = SimulateWithRecorder(TestMatchFactory.Even(seed));

            foreach (var period in new[] { 1, 2 })
            {
                var half = passages.Where(passage => passage.Period == period).ToList();

                half.Should().NotBeEmpty();
                half[0].StartClockSeconds.Should().Be(
                    period == 1 ? 0 : Rules.HalfTimeMinute * Rules.SecondsPerMinute,
                    "each half starts at its own kick-off");

                foreach (var passage in half)
                {
                    passage.EndClockSeconds.Should().BeGreaterThan(
                        passage.StartClockSeconds,
                        "a possession takes time: it was zero seconds long when the start was read after the clock moved");
                }

                for (var index = 1; index < half.Count; index++)
                {
                    half[index].StartClockSeconds.Should().Be(
                        half[index - 1].EndClockSeconds,
                        "possessions tile the half with no gap and no overlap");
                }
            }
        }
    }

    [Fact]
    public void The_period_starts_at_one_and_changes_once_at_half_time()
    {
        for (var seed = 1UL; seed <= 30; seed++)
        {
            var (_, passages) = SimulateWithRecorder(TestMatchFactory.Even(seed));

            passages[0].Period.Should().Be(1);
            passages[^1].Period.Should().Be(2);
            passages.Select(passage => passage.Period).Should().BeInAscendingOrder();
            passages.Select(passage => passage.Period).Distinct().Should().Equal(1, 2);
        }
    }

    [Fact]
    public void Events_are_ordered_within_a_passage_and_positioned_among_its_facts()
    {
        var lastSequence = 0;

        for (var seed = 1UL; seed <= 30; seed++)
        {
            var (_, passages) = SimulateWithRecorder(TestMatchFactory.Even(seed));
            lastSequence = 0;

            foreach (var passage in passages)
            {
                passage.EventSequences.Should().Equal(
                    passage.Events.Select(matchEvent => matchEvent.Sequence),
                    "the sequence list is derived from the positioned events");

                var previousFraction = -1;

                foreach (var matchEvent in passage.Events)
                {
                    matchEvent.Sequence.Should().BeGreaterThan(lastSequence, "events are in the order they happened");
                    matchEvent.FractionBasisPoints.Should().BeInRange(0, EngineRulesV2.Certain);
                    matchEvent.FractionBasisPoints.Should().BeGreaterThanOrEqualTo(
                        previousFraction,
                        "an event never sits before the one that preceded it");

                    lastSequence = matchEvent.Sequence;
                    previousFraction = matchEvent.FractionBasisPoints;
                }
            }
        }
    }

    [Fact]
    public void A_passages_outcome_tells_the_truth_about_what_happened_in_it()
    {
        var seen = new HashSet<PassageOutcome>();

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            foreach (var passage in match.Passages)
            {
                var types = EventsOf(match, passage).Select(pair => pair.Event.Type).ToList();
                var hasShot = types.Any(type => OutcomeEvents.Contains(type) && type != EngineEventType.Offside);

                seen.Add(passage.Outcome);

                switch (passage.Outcome)
                {
                    case PassageOutcome.OpenPlayShot:
                        hasShot.Should().BeTrue();
                        types.Should().NotContain(EngineEventType.Foul).And.NotContain(EngineEventType.Corner);
                        break;

                    case PassageOutcome.Penalty:
                        types.Should().Contain(EngineEventType.Foul).And.Contain(EngineEventType.PenaltyAwarded);
                        break;

                    case PassageOutcome.FreeKickStruck:
                        types.Should().Contain(EngineEventType.FreeKickWon).And.Contain(EngineEventType.FreeKickShot);
                        break;

                    case PassageOutcome.FreeKickCrossed:
                        types.Should().Contain(EngineEventType.FreeKickWon).And.NotContain(EngineEventType.FreeKickShot);
                        hasShot.Should().BeFalse();
                        break;

                    case PassageOutcome.Foul:
                        types.Should().Contain(EngineEventType.Foul);
                        hasShot.Should().BeFalse();
                        types.Should().NotContain(EngineEventType.FreeKickWon);
                        break;

                    case PassageOutcome.Offside:
                        types.Should().Contain(EngineEventType.Offside);
                        break;

                    case PassageOutcome.CornerHeaded:
                        types.Should().Contain(EngineEventType.Corner);
                        hasShot.Should().BeTrue();
                        break;

                    case PassageOutcome.CornerCleared:
                        types.Should().Contain(EngineEventType.Corner);
                        hasShot.Should().BeFalse();
                        break;

                    default:
                        // A possession that was lost or broke down produced no foul, no corner, and no shot.
                        hasShot.Should().BeFalse($"{passage.Outcome} never reaches a shot");
                        types.Should().NotContain(EngineEventType.Foul).And.NotContain(EngineEventType.Corner);
                        break;
                }
            }
        }

        seen.Should().BeEquivalentTo(Enum.GetValues<PassageOutcome>(), "every way a possession can end is exercised");
    }

    // ---- A strike travels to the target its outcome decides (engine-v5) --------------------------

    [Fact]
    public void A_goal_ends_inside_the_goal_mouth_under_the_bar_and_nothing_follows_it()
    {
        var goals = 0;

        foreach (var (match, passage, position, matchEvent) in EventsAcross(match => match.IsGoal))
        {
            goals++;

            var end = BallAt(passage, position);

            end.Kind.Should().Be(PassageWaypointKind.Shot);
            AttackingX(end.X, matchEvent.Side).Should().Be(SpatialPitch.PitchLength, "the ball is on the goal line");
            end.Y.Should().BeInRange(SpatialPitch.GoalYMin, SpatialPitch.GoalYMax, "between the posts");
            end.Z.Should().BeLessThan(Rules.ShotAltitude, "under the bar");

            passage.Waypoints[^1].Should().Be(end, "nothing is played after a goal: there is no clearance");
            match.Result.Events.Should().Contain(candidate => candidate.Sequence == matchEvent.Sequence);
        }

        goals.Should().BeGreaterThan(100);
    }

    [Fact]
    public void A_save_is_made_by_the_keeper_near_the_line_in_front_of_the_goal_not_where_the_shooter_stood()
    {
        var saves = 0;

        foreach (var (_, passage, position, matchEvent) in EventsAcross(match => match.Type == EngineEventType.ShotSaved))
        {
            saves++;

            var end = BallAt(passage, position);

            end.Kind.Should().Be(PassageWaypointKind.Shot);
            AttackingX(end.X, matchEvent.Side).Should().BeInRange(
                SpatialPitch.PitchLength - Rules.SaveDepthMaxBasisPoints,
                SpatialPitch.PitchLength - Rules.SaveDepthMinBasisPoints,
                "the keeper gets to it a few metres off the line");
            end.Y.Should().BeInRange(SpatialPitch.GoalYMin, SpatialPitch.GoalYMax);

            if (matchEvent.SecondaryParticipantId is Guid keeper)
            {
                var save = passage.Touches.Single(touch => touch.Action == PassageAction.Save);

                save.ParticipantId.Should().Be(keeper);
                new SpatialPoint(save.X, save.Y).Should().Be(
                    new SpatialPoint(end.X, end.Y),
                    "the keeper's touch is where the ball arrives");

                // The shooter's position is the event's: the save is further up the pitch than where the shot was
                // struck from, instead of being stamped on top of the shooter.
                AttackingX(save.X, matchEvent.Side).Should().BeGreaterThan(
                    AttackingX(matchEvent.X!.Value, matchEvent.Side),
                    "the ball has travelled by the time the keeper gets to it");
            }
        }

        saves.Should().BeGreaterThan(100);
    }

    [Fact]
    public void A_miss_ends_out_of_play_wide_of_a_post_or_over_the_bar()
    {
        var misses = 0;
        var wide = 0;
        var over = 0;

        foreach (var (_, passage, position, matchEvent) in EventsAcross(match => match.Type == EngineEventType.ShotOffTarget))
        {
            misses++;

            var end = BallAt(passage, position);

            end.Kind.Should().Be(PassageWaypointKind.Shot);
            AttackingX(end.X, matchEvent.Side).Should().Be(SpatialPitch.PitchLength, "it crosses the goal line");

            var beside = end.Y < SpatialPitch.GoalYMin || end.Y > SpatialPitch.GoalYMax;
            var above = end.Z > Rules.ShotAltitude;

            (beside || above).Should().BeTrue("a miss is wide of a post or over the bar, never into the goal");

            wide += beside ? 1 : 0;
            over += above ? 1 : 0;
        }

        misses.Should().BeGreaterThan(100);
        wide.Should().BeGreaterThan(0);
        over.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_woodwork_is_struck_on_the_line_and_the_ball_comes_back_into_the_box()
    {
        var hits = 0;

        foreach (var (_, passage, position, matchEvent) in EventsAcross(match => match.Type == EngineEventType.Woodwork))
        {
            hits++;

            var frame = BallAt(passage, position);

            frame.Kind.Should().Be(PassageWaypointKind.Shot);
            AttackingX(frame.X, matchEvent.Side).Should().Be(SpatialPitch.PitchLength);

            var post = frame.Y == SpatialPitch.GoalYMin || frame.Y == SpatialPitch.GoalYMax;
            var bar = frame.Z == Rules.ShotAltitude && frame.Y > SpatialPitch.GoalYMin && frame.Y < SpatialPitch.GoalYMax;

            (post || bar).Should().BeTrue("the woodwork is a post or the crossbar");

            var rebound = passage.Waypoints.First(waypoint => waypoint.FractionBasisPoints > position.FractionBasisPoints);

            rebound.Kind.Should().Be(PassageWaypointKind.Clearance);
            AttackingX(rebound.X, matchEvent.Side).Should().BeInRange(
                SpatialPitch.PitchLength - Rules.ReboundDistanceMaxBasisPoints,
                SpatialPitch.PitchLength - Rules.ReboundDistanceMinBasisPoints);
            passage.Waypoints[^1].Should().Be(rebound, "the rebound is where play continues");
        }

        hits.Should().BeGreaterThan(5);
    }

    [Fact]
    public void A_block_is_made_two_to_six_metres_in_front_of_the_shooter()
    {
        var blocks = 0;

        foreach (var (_, passage, position, matchEvent) in EventsAcross(match => match.Type == EngineEventType.ShotBlocked))
        {
            blocks++;

            var block = BallAt(passage, position);
            var distance = AttackingX(block.X, matchEvent.Side) - AttackingX(matchEvent.X!.Value, matchEvent.Side);

            block.Kind.Should().Be(PassageWaypointKind.Shot);
            distance.Should().BeInRange(
                Rules.BlockDistanceMinBasisPoints,
                Rules.BlockDistanceMaxBasisPoints,
                "a block is two to six metres in front of the shooter");
            passage.Waypoints[^1].Should().Be(block, "the ball stays where it was stopped");
        }

        blocks.Should().BeGreaterThan(50);
    }

    [Fact]
    public void A_penalty_is_a_placement_on_the_spot_and_then_a_strike()
    {
        var penalties = 0;

        foreach (var (match, passage, position, matchEvent) in EventsAcross(match => match.Type == EngineEventType.PenaltyAwarded))
        {
            penalties++;

            var placement = BallAt(passage, position);

            placement.Kind.Should().Be(PassageWaypointKind.Restart);
            AttackingX(placement.X, matchEvent.Side).Should().Be(SpatialPitch.PenaltySpotAwayX);
            placement.Y.Should().Be(SpatialPitch.PenaltySpotY);

            var taker = passage.Touches.Single(touch => touch.Action == PassageAction.Penalty);

            taker.ParticipantId.Should().Be(matchEvent.ParticipantId!.Value);
            new SpatialPoint(taker.X, taker.Y).Should().Be(new SpatialPoint(placement.X, placement.Y));

            var strike = passage.Waypoints.First(waypoint => waypoint.FractionBasisPoints > position.FractionBasisPoints);

            strike.Kind.Should().Be(PassageWaypointKind.Shot, "the penalty is struck from the spot to a target");
            passage.Outcome.Should().Be(PassageOutcome.Penalty);

            var outcome = EventsOf(match, passage).Single(pair => pair.Event.Type is EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed);

            if (outcome.Event.Type == EngineEventType.PenaltyGoal)
            {
                strike.Y.Should().BeInRange(SpatialPitch.GoalYMin, SpatialPitch.GoalYMax);
            }
            else
            {
                var stopped = passage.Touches.Any(touch => touch.Action == PassageAction.Save);
                var beside = strike.Y < SpatialPitch.GoalYMin || strike.Y > SpatialPitch.GoalYMax || strike.Z > Rules.ShotAltitude;

                (stopped || beside).Should().BeTrue("a missed penalty is stopped by the keeper or put out of play");
            }
        }

        penalties.Should().BeGreaterThan(3);
    }

    [Fact]
    public void A_corner_goes_out_is_set_down_at_the_flag_and_delivered_with_no_clearance_after_a_goal()
    {
        var corners = 0;
        var headed = 0;

        foreach (var (match, passage, position, matchEvent) in EventsAcross(match => match.Type == EngineEventType.Corner))
        {
            corners++;

            var flag = BallAt(passage, position);
            var index = passage.Waypoints.ToList().IndexOf(flag);

            flag.Kind.Should().Be(PassageWaypointKind.Restart, "the ball is set down at the flag");
            AttackingX(flag.X, matchEvent.Side).Should().Be(SpatialPitch.PitchLength);
            flag.Y.Should().BeOneOf(0, SpatialPitch.PitchWidth);

            var outOfPlay = passage.Waypoints[index - 1];

            AttackingX(outOfPlay.X, matchEvent.Side).Should().Be(SpatialPitch.PitchLength, "the ball went out over the goal line");

            var delivery = passage.Waypoints[index + 1];

            delivery.Kind.Should().Be(PassageWaypointKind.Cross, "the corner is delivered into the box");
            AttackingX(delivery.X, matchEvent.Side).Should().BeInRange(Rules.BoxXMinBasisPoints, Rules.BoxXMaxBasisPoints);
            AttackingY(delivery.Y, matchEvent.Side).Should().BeInRange(Rules.BoxYMinBasisPoints, Rules.BoxYMaxBasisPoints);

            if (passage.Outcome == PassageOutcome.CornerHeaded)
            {
                headed++;
                passage.Touches.Count(touch => touch.Action == PassageAction.Header).Should().Be(1, "the winner of the header");
            }

            if (EventsOf(match, passage).Any(pair => pair.Event.IsGoal))
            {
                passage.Waypoints[^1].Kind.Should().Be(PassageWaypointKind.Shot, "there is no clearance after a goal");
            }
        }

        corners.Should().BeGreaterThan(50);
        headed.Should().BeGreaterThan(10);
    }

    [Fact]
    public void A_free_kick_is_a_placement_and_then_a_strike_or_a_cross()
    {
        var struck = 0;
        var crossed = 0;

        foreach (var (match, passage, position, matchEvent) in EventsAcross(match => match.Type == EngineEventType.FreeKickWon))
        {
            var placement = BallAt(passage, position);

            placement.Kind.Should().Be(PassageWaypointKind.Restart, "the ball is set down where the foul was");

            var taker = passage.Touches.Single(touch => touch.Action == PassageAction.FreeKick);

            taker.ParticipantId.Should().Be(matchEvent.ParticipantId!.Value);

            var next = passage.Waypoints.First(waypoint => waypoint.FractionBasisPoints > position.FractionBasisPoints);
            var strikeEvent = EventsOf(match, passage).Any(pair => pair.Event.Type == EngineEventType.FreeKickShot);

            if (strikeEvent)
            {
                struck++;
                next.Kind.Should().Be(PassageWaypointKind.Shot);
                passage.Outcome.Should().Be(PassageOutcome.FreeKickStruck);
            }
            else
            {
                crossed++;
                next.Kind.Should().Be(PassageWaypointKind.Cross);
                passage.Outcome.Should().Be(PassageOutcome.FreeKickCrossed);
                passage.Waypoints[^1].Should().Be(next, "the possession ends with the ball in the box");
            }
        }

        struck.Should().BeGreaterThan(10);
        crossed.Should().BeGreaterThan(10);
    }

    [Fact]
    public void The_fouler_and_the_fouled_player_are_both_at_the_ball_when_the_foul_is_committed()
    {
        var fouls = 0;

        foreach (var (match, passage, position, matchEvent) in EventsAcross(match => match.Type == EngineEventType.Foul))
        {
            fouls++;

            var ball = BallAt(passage, position);
            var spot = new SpatialPoint(ball.X, ball.Y);

            var fouler = passage.Touches.Single(touch => touch.ParticipantId == matchEvent.ParticipantId && touch.Action == PassageAction.Tackle);

            new SpatialPoint(fouler.X, fouler.Y).Should().Be(spot);

            // The fouled player is on the other side, and was carrying the ball where the foul was committed.
            var foulingSquad = match.Input.SideOf(matchEvent.Side).Squad.Select(participant => participant.ParticipantId).ToHashSet();
            var fouled = passage.Touches.Last(touch => touch.Action == PassageAction.Carry && !foulingSquad.Contains(touch.ParticipantId));

            new SpatialPoint(fouled.X, fouled.Y).Should().Be(spot);
        }

        fouls.Should().BeGreaterThan(100);
    }

    [Fact]
    public void A_lost_scramble_names_both_contestants_at_the_ball()
    {
        var scrambles = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            foreach (var passage in match.Passages.Where(passage => passage.Outcome == PassageOutcome.ScrambleLost))
            {
                scrambles++;

                var interception = passage.Touches.Single(touch => touch.Action == PassageAction.Interception);
                var chaser = passage.Touches.Single(touch => touch.Action == PassageAction.Run);

                new SpatialPoint(interception.X, interception.Y).Should().Be(new SpatialPoint(chaser.X, chaser.Y));

                var possessionSquad = match.Input.SideOf(passage.Side).Squad.Select(participant => participant.ParticipantId).ToHashSet();

                possessionSquad.Should().Contain(chaser.ParticipantId, "the chaser is from the side that lost the ball");
                possessionSquad.Should().NotContain(interception.ParticipantId, "the defender won it");
            }
        }

        scrambles.Should().BeGreaterThan(50);
    }

    [Fact]
    public void A_header_contest_records_the_winner_with_a_header_and_the_loser_as_having_gone_for_it()
    {
        var contests = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            foreach (var passage in match.Passages.Where(passage => passage.Outcome is PassageOutcome.CornerHeaded or PassageOutcome.CornerCleared))
            {
                var headers = passage.Touches.Where(touch => touch.Action == PassageAction.Header).ToList();

                if (headers.Count == 0)
                {
                    continue; // The delivery was cleared with no contest drawn.
                }

                contests++;
                headers.Should().ContainSingle("only the winner touches it with a header");

                // A kept scramble earlier in the possession leaves its own Run touch, so the jump is the last one.
                var jump = passage.Touches.Last(touch => touch.Action == PassageAction.Run);

                new SpatialPoint(jump.X, jump.Y).Should().Be(new SpatialPoint(headers[0].X, headers[0].Y));
                jump.ParticipantId.Should().NotBe(headers[0].ParticipantId);
            }
        }

        contests.Should().BeGreaterThan(20);
    }

    private static (MatchResultV1 Result, IReadOnlyList<MatchPassageV1> Passages) SimulateWithRecorder(MatchInputV1 input)
    {
        var recorder = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, EngineRulesV2.Default, passages: recorder);

        return (result, recorder.Passages);
    }

    /// <summary>Every event a predicate selects, across the sweep, with the passage and position it sits at.</summary>
    private static IEnumerable<(RecordedMatch Match, MatchPassageV1 Passage, PassageEventV1 Position, EngineEventV1 Event)> EventsAcross(
        Func<EngineEventV1, bool> select)
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            foreach (var passage in match.Passages)
            {
                foreach (var (position, matchEvent) in EventsOf(match, passage))
                {
                    if (select(matchEvent))
                    {
                        yield return (match, passage, position, matchEvent);
                    }
                }
            }
        }
    }
}
