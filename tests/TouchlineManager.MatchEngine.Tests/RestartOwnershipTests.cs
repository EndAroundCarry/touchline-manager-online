using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using static TouchlineManager.MatchEngine.Tests.PassageTestHelpers;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Dead balls belong to somebody (`MAT-12`, `engine-v5`): the rules decide which side takes a kick-off, a goal
/// kick, a keeper's ball, or a free kick and from where, and the very next possession is played from it.
/// </summary>
public sealed class RestartOwnershipTests
{
    private const int Seeds = 60;

    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    /// <summary>The home side defends the low end of the pitch, so its goal area is near X = 0.</summary>
    private static readonly SpatialPoint HomeGoalArea =
        new(Rules.GoalAreaXBasisPoints, SpatialPitch.PitchWidth / 2);

    /// <summary>The away side defends the high end of the pitch, so its goal area is near X = 10,000.</summary>
    private static readonly SpatialPoint AwayGoalArea =
        new(SpatialPitch.PitchLength - Rules.GoalAreaXBasisPoints, SpatialPitch.PitchWidth / 2);

    [Fact]
    public void MAT_12_Each_half_is_kicked_off_from_the_centre_by_the_side_the_rules_name()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            var first = match.Passages.First(passage => passage.Period == 1);
            var second = match.Passages.First(passage => passage.Period == 2);

            first.Restart.Should().Be(PassageRestartKind.KickOff);
            first.Side.Should().Be(MatchSide.Home, "the home side kicks off the first half");
            first.StartPoint.Should().Be(SpatialPoint.Center);

            second.Restart.Should().Be(PassageRestartKind.KickOff);
            second.Side.Should().Be(MatchSide.Away, "the away side kicks off the second half");
            second.StartPoint.Should().Be(SpatialPoint.Center);
        }
    }

    [Fact]
    public void MAT_12_Every_dead_ball_is_taken_by_the_side_that_owns_it_and_by_nobody_else()
    {
        var kindsSeen = new HashSet<PassageRestartKind>();

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            for (var index = 1; index < match.Passages.Count; index++)
            {
                var previous = match.Passages[index - 1];
                var next = match.Passages[index];

                if (previous.Period != next.Period)
                {
                    continue;
                }

                var (kinds, side) = ExpectedRestart(match, previous);

                next.Restart.Should().BeOneOf(
                    kinds,
                    $"possession {previous.Ordinal} ended as {previous.Outcome}, so possession {next.Ordinal} begins that way");

                if (side is { } owner)
                {
                    next.Side.Should().Be(owner, $"possession {next.Ordinal}'s {next.Restart} belongs to the side the rules name");
                }

                kindsSeen.Add(next.Restart);
            }
        }

        // The assertion above is empty if a kind never happens, so every kind must have been exercised.
        kindsSeen.Should().Contain(
        [
            PassageRestartKind.None,
            PassageRestartKind.KickOff,
            PassageRestartKind.GoalKick,
            PassageRestartKind.KeeperBall,
            PassageRestartKind.FreeKick,
        ]);
    }

    [Fact]
    public void MAT_12_A_goal_is_followed_by_a_kick_off_for_the_side_that_conceded()
    {
        var goalsSeen = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            for (var index = 0; index < match.Passages.Count - 1; index++)
            {
                var passage = match.Passages[index];
                var next = match.Passages[index + 1];

                foreach (var goal in EventsOf(match, passage).Select(pair => pair.Event).Where(matchEvent => matchEvent.IsGoal))
                {
                    goalsSeen++;

                    next.Restart.Should().Be(PassageRestartKind.KickOff);
                    next.StartPoint.Should().Be(SpatialPoint.Center);

                    if (next.Period == passage.Period)
                    {
                        next.Side.Should().Be(Opponent(goal.Side), "the side that conceded kicks off, not the one that scored");
                    }
                }
            }
        }

        goalsSeen.Should().BeGreaterThan(50, "the sweep must contain goals for this to mean anything");
    }

    [Fact]
    public void MAT_12_A_save_is_the_keepers_ball_and_a_miss_is_a_goal_kick_for_the_defending_side()
    {
        var saves = 0;
        var misses = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            for (var index = 0; index < match.Passages.Count - 1; index++)
            {
                var passage = match.Passages[index];
                var next = match.Passages[index + 1];

                if (passage.Period != next.Period)
                {
                    continue;
                }

                foreach (var matchEvent in EventsOf(match, passage).Select(pair => pair.Event))
                {
                    var defending = Opponent(matchEvent.Side);
                    var goalArea = defending == MatchSide.Home ? HomeGoalArea : AwayGoalArea;

                    switch (matchEvent.Type)
                    {
                        case EngineEventType.ShotSaved:
                            saves++;
                            next.Restart.Should().Be(PassageRestartKind.KeeperBall);
                            next.Side.Should().Be(defending);
                            next.StartPoint.Should().Be(goalArea, "the defending side restarts from its own goal area");
                            break;

                        case EngineEventType.ShotOffTarget:
                            misses++;
                            next.Restart.Should().Be(PassageRestartKind.GoalKick);
                            next.Side.Should().Be(defending);
                            next.StartPoint.Should().Be(goalArea);
                            break;

                        case EngineEventType.PenaltyMissed:
                            next.Restart.Should().BeOneOf(PassageRestartKind.GoalKick, PassageRestartKind.KeeperBall);
                            next.Side.Should().Be(defending);
                            next.StartPoint.Should().Be(goalArea);
                            break;

                        default:
                            break;
                    }
                }
            }
        }

        saves.Should().BeGreaterThan(50);
        misses.Should().BeGreaterThan(50);
    }

    [Fact]
    public void MAT_12_A_foul_with_no_shot_is_the_fouled_sides_free_kick_and_an_offside_is_the_defenders()
    {
        var fouls = 0;
        var offsides = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            for (var index = 0; index < match.Passages.Count - 1; index++)
            {
                var passage = match.Passages[index];
                var next = match.Passages[index + 1];

                if (passage.Period != next.Period)
                {
                    continue;
                }

                if (passage.Outcome == PassageOutcome.Foul)
                {
                    fouls++;

                    next.Restart.Should().Be(PassageRestartKind.FreeKick);
                    next.Side.Should().Be(passage.Side, "the free kick is the fouled side's, not the fouling side's");
                    next.StartPoint.Should().Be(passage.EndPoint, "a free kick is taken where the foul was committed");
                }

                if (passage.Outcome == PassageOutcome.Offside)
                {
                    offsides++;

                    next.Restart.Should().Be(PassageRestartKind.FreeKick);
                    next.Side.Should().Be(Opponent(passage.Side), "the defending side takes the free kick for an offside");
                    next.StartPoint.Should().Be(passage.EndPoint, "it is taken from where the offside was given");
                }
            }
        }

        fouls.Should().BeGreaterThan(50);
        offsides.Should().BeGreaterThan(10);
    }

    [Fact]
    public void MAT_12_A_loose_ball_is_not_a_restart()
    {
        // A block, a woodwork rebound, a cleared corner, a crossed free kick, and a turnover leave the ball in
        // play: the next possession is contested, which is to say it begins from play, and no flag is left
        // behind for a later one.
        var looseOutcomes = new[]
        {
            PassageOutcome.ScrambleLost,
            PassageOutcome.ProgressionFailed,
            PassageOutcome.CreationFailed,
            PassageOutcome.CornerCleared,
            PassageOutcome.FreeKickCrossed,
        };

        var seen = new HashSet<PassageOutcome>();

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            for (var index = 0; index < match.Passages.Count - 1; index++)
            {
                var passage = match.Passages[index];
                var next = match.Passages[index + 1];

                if (passage.Period != next.Period || !looseOutcomes.Contains(passage.Outcome))
                {
                    continue;
                }

                seen.Add(passage.Outcome);
                next.Restart.Should().Be(PassageRestartKind.None, $"a {passage.Outcome} leaves the ball in play");
                next.StartPoint.Should().Be(passage.EndPoint);
            }
        }

        seen.Should().BeEquivalentTo(looseOutcomes);
    }

    [Fact]
    public void MAT_12_A_goal_area_start_happens_only_immediately_after_a_keepers_ball_or_a_goal_kick()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            for (var index = 0; index < match.Passages.Count; index++)
            {
                var passage = match.Passages[index];
                var atGoalArea = passage.StartPoint == HomeGoalArea || passage.StartPoint == AwayGoalArea;
                var isGoalAreaRestart = passage.Restart is PassageRestartKind.GoalKick or PassageRestartKind.KeeperBall;

                // No stale flag: a ball in a goal area with no restart behind it is a teleport, and a restart
                // is only ever a restart from the goal area of the side that takes it.
                atGoalArea.Should().Be(isGoalAreaRestart, $"possession {passage.Ordinal} began at {passage.StartPoint}");

                if (isGoalAreaRestart)
                {
                    index.Should().BePositive();

                    var previous = match.Passages[index - 1];
                    var (kinds, _) = ExpectedRestart(match, previous);

                    kinds.Should().Contain(passage.Restart, "the shot before it was saved or missed");
                }
            }
        }
    }

    [Fact]
    public void MAT_12_Consecutive_possessions_join_except_at_a_restart_placement()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = Play(seed);

            for (var index = 1; index < match.Passages.Count; index++)
            {
                var previous = match.Passages[index - 1];
                var next = match.Passages[index];

                if (previous.Period != next.Period)
                {
                    continue;
                }

                var joins = next.StartPoint == previous.EndPoint;

                // A kick-off and a goal-area restart set the ball down somewhere else; a free kick is taken
                // where play stopped, so it joins.
                var placed = next.Restart is PassageRestartKind.KickOff
                    or PassageRestartKind.GoalKick
                    or PassageRestartKind.KeeperBall;

                (joins || placed).Should().BeTrue(
                    $"possession {next.Ordinal} began at {next.StartPoint}, not where {previous.Ordinal} ended ({previous.EndPoint}), and was not a placement");

                if (next.Restart == PassageRestartKind.FreeKick)
                {
                    joins.Should().BeTrue("a free kick is taken from where it was given");
                }
            }
        }
    }

    [Fact]
    public void MAT_12_A_restart_possession_opens_with_the_ball_set_down_not_picked_up()
    {
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var match = Play(seed);

            foreach (var passage in match.Passages)
            {
                var opening = passage.Waypoints[0].Kind;

                if (passage.Restart == PassageRestartKind.None)
                {
                    opening.Should().Be(PassageWaypointKind.Carry);
                }
                else
                {
                    opening.Should().Be(PassageWaypointKind.Restart);
                }
            }
        }
    }

    /// <summary>
    /// What the possession after this one must begin as: the kinds it may be, and the side that must take it
    /// when the rules name one.
    /// </summary>
    private static (PassageRestartKind[] Kinds, MatchSide? Side) ExpectedRestart(RecordedMatch match, MatchPassageV1 passage)
    {
        var outcome = EventsOf(match, passage)
            .Select(pair => pair.Event)
            .LastOrDefault(matchEvent => OutcomeEvents.Contains(matchEvent.Type));

        if (outcome is not null)
        {
            var defending = Opponent(outcome.Side);

            return outcome.Type switch
            {
                EngineEventType.Goal or EngineEventType.PenaltyGoal => ([PassageRestartKind.KickOff], defending),
                EngineEventType.ShotSaved => ([PassageRestartKind.KeeperBall], defending),
                EngineEventType.ShotOffTarget => ([PassageRestartKind.GoalKick], defending),
                EngineEventType.PenaltyMissed => ([PassageRestartKind.GoalKick, PassageRestartKind.KeeperBall], defending),
                EngineEventType.Offside => ([PassageRestartKind.FreeKick], defending),
                _ => ([PassageRestartKind.None], null),
            };
        }

        return passage.Outcome == PassageOutcome.Foul
            ? ([PassageRestartKind.FreeKick], passage.Side)
            : ([PassageRestartKind.None], null);
    }
}
