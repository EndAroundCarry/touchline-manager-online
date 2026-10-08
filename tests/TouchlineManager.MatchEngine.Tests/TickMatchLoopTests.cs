using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's match loop: a whole match plays at 10 Hz, the restarts chain the way the state machine
/// says, the trace is recorded, and the same seed plays the same match (`tick-engine-v1`, Milestone 8).
/// </summary>
public sealed class TickMatchLoopTests
{
    private const ulong Seed = 20_260_925;

    private static (MatchState State, TickMatchRecording Recording) Run(ulong seed = Seed)
    {
        var state = TickTestMatchState.Create(seed);
        var recorder = new TickMatchRecorder();

        TickMatchLoop.Run(state, recorder);

        return (state, recorder.Build());
    }

    [Fact]
    public void A_full_match_plays_to_full_time_with_both_halves()
    {
        var (state, recording) = Run();

        state.Events.Should().ContainSingle(matchEvent => matchEvent.Type == EngineEventType.KickOff);
        state.Events.Should().ContainSingle(matchEvent => matchEvent.Type == EngineEventType.HalfTime);
        state.Events.Should().ContainSingle(matchEvent => matchEvent.Type == EngineEventType.SecondHalfStart);
        state.Events.Should().ContainSingle(matchEvent => matchEvent.Type == EngineEventType.FullTime);
        state.ClockSeconds.Should().BeGreaterThanOrEqualTo(90 * 60, "both halves are played to their regulation end");
        recording.TickCount.Should().BeGreaterThan(54_000, "the match runs at ten ticks a second");
    }

    [Fact]
    public void The_recording_carries_a_frame_for_every_tick_and_entity_inside_the_pitch()
    {
        var (_, recording) = Run();

        recording.Entities.Should().HaveCount(TickMatchRecording.EntityCount);

        var minX = int.MaxValue;
        var maxX = int.MinValue;
        var minY = int.MaxValue;
        var maxY = int.MinValue;

        for (var tick = 0; tick < recording.TickCount; tick++)
        {
            for (var entity = 0; entity < TickMatchRecording.EntityCount; entity++)
            {
                var x = recording.XAt(tick, entity);
                var y = recording.YAt(tick, entity);

                recording.ZAt(tick, entity).Should().BeInRange(0, 100);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        minX.Should().BeGreaterThanOrEqualTo(0, "nothing leaves the pitch");
        maxX.Should().BeLessThanOrEqualTo(SpatialPitch.PitchLength);
        minY.Should().BeGreaterThanOrEqualTo(0);
        maxY.Should().BeLessThanOrEqualTo(SpatialPitch.PitchWidth);
    }

    [Fact]
    public void The_same_seed_plays_the_same_match_tick_for_tick()
    {
        var first = Run(Seed);
        var second = Run(Seed);

        Describe(first.State).Should().BeEquivalentTo(Describe(second.State), "the engine is deterministic");
        first.Recording.TickCount.Should().Be(second.Recording.TickCount);

        for (var tick = 0; tick < first.Recording.TickCount; tick += 97)
        {
            for (var entity = 0; entity < TickMatchRecording.EntityCount; entity++)
            {
                first.Recording.XAt(tick, entity).Should().Be(second.Recording.XAt(tick, entity));
                first.Recording.YAt(tick, entity).Should().Be(second.Recording.YAt(tick, entity));
                first.Recording.ZAt(tick, entity).Should().Be(second.Recording.ZAt(tick, entity));
            }
        }

        first.Recording.Touches.Should().Equal(second.Recording.Touches);
        first.Recording.Restarts.Should().Equal(second.Recording.Restarts);
    }

    [Fact]
    public void Attaching_a_recorder_changes_nothing()
    {
        var withRecorder = Run(Seed);

        var state = TickTestMatchState.Create(Seed);
        TickMatchLoop.Run(state);

        Describe(state).Should().BeEquivalentTo(Describe(withRecorder.State), "the recorder is a by-product of the run");
    }

    [Fact]
    public void Play_moves_the_ball_between_both_halves_and_both_ends()
    {
        var (_, recording) = Run();
        var ballMinX = int.MaxValue;
        var ballMaxX = int.MinValue;

        for (var tick = 0; tick < recording.TickCount; tick++)
        {
            var x = recording.XAt(tick, TickMatchRecording.BallEntityIndex);

            ballMinX = Math.Min(ballMinX, x);
            ballMaxX = Math.Max(ballMaxX, x);
        }

        ballMinX.Should().BeLessThan(2_500, "the ball is played into the home third");
        ballMaxX.Should().BeGreaterThan(7_500, "the ball is played into the away third");
    }

    [Fact]
    public void The_match_produces_touches_and_the_dead_balls_of_open_play()
    {
        var (_, recording) = Run();

        recording.Touches.Should().Contain(touch => touch.Action == PassageAction.Pass, "the ball is passed");
        recording.Touches.Should().Contain(touch => touch.Action == PassageAction.Receive, "passes are received");
        recording.Touches.Should().Contain(touch => touch.Action == PassageAction.Shot, "shots are taken");

        var phases = recording.Restarts.Select(stamp => stamp.Phase).Distinct().ToArray();

        phases.Should().Contain(TickMatchPhase.KickOffPending, "the match and the second half start with kick-offs");
        phases.Should().Contain(TickMatchPhase.ThrowInPending, "the ball goes out over the touchline");
        phases.Should().Contain(TickMatchPhase.GoalKickPending, "the ball goes out over the goal line");
    }

    [Fact]
    public void Corners_are_won_and_set_up_as_a_corner()
    {
        // A corner needs a defender to put the ball behind his own line; whether one falls in a single match is the
        // model's business, so the check ranges over a few matches and requires corners to happen at all.
        var corners = 0;

        foreach (var seed in new ulong[] { 1, 3, 7, 12, 25 })
        {
            var (_, recording) = Run(seed);

            corners += recording.Restarts.Count(stamp => stamp.Phase == TickMatchPhase.CornerPending);
        }

        corners.Should().BePositive("a ball put behind by a defender is a corner");
    }

    [Fact]
    public void Every_restart_that_is_taken_hands_play_on_to_a_touch()
    {
        var (_, recording) = Run();

        // The very last dead ball of the match can still be waiting when the whistle goes.
        var pending = recording.Restarts.Where(stamp => stamp.Tick <= recording.TickCount - 60).ToArray();

        pending.Should().NotBeEmpty();

        foreach (var stamp in pending)
        {
            recording.Touches.Should().Contain(touch => touch.Tick > stamp.Tick, $"the {stamp.Phase} at tick {stamp.Tick} is taken");
        }
    }

    [Fact]
    public void A_goal_is_celebrated_and_the_conceding_side_kicks_off()
    {
        var (state, recording) = Run();
        var goals = state.Events.Where(matchEvent => matchEvent.IsGoal).ToArray();

        goals.Should().NotBeEmpty("the seed's match has goals to check the chain of");

        foreach (var goal in goals)
        {
            var goalTick = recording.Events.Single(stamp => stamp.Sequence == goal.Sequence).Tick;
            var conceding = goal.Side == MatchSide.Home ? MatchSide.Away : MatchSide.Home;

            recording.Restarts.Should().Contain(
                stamp => stamp.Tick > goalTick && stamp.Phase == TickMatchPhase.GoalCelebration,
                "a goal is celebrated");
            recording.Restarts.Should().Contain(
                stamp => stamp.Tick > goalTick && stamp.Phase == TickMatchPhase.KickOffPending && stamp.Side == conceding,
                "the conceding side kicks off again");
        }
    }

    [Fact]
    public void The_event_log_is_ordered_and_every_event_is_stamped()
    {
        var (state, recording) = Run();

        state.Events.Select(matchEvent => matchEvent.Sequence).Should().BeInAscendingOrder();
        recording.Events.Should().HaveCount(state.Events.Count);

        foreach (var stamp in recording.Events)
        {
            stamp.Tick.Should().BeInRange(0, recording.TickCount);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void A_match_plays_to_full_time_whatever_the_seed(ulong seed)
    {
        var (state, _) = Run(seed);

        state.Events.Should().Contain(matchEvent => matchEvent.Type == EngineEventType.FullTime);
    }

    /// <summary>Describes a match as the facts a determinism check compares: the events and the player lines.</summary>
    private static List<string> Describe(MatchState state) =>
    [
        .. state.Events.Select(matchEvent =>
            $"{matchEvent.Sequence}:{matchEvent.Minute}:{matchEvent.StoppageMinute}:{matchEvent.Type}:{matchEvent.Side}:{matchEvent.ParticipantId}"),
    ];
}
