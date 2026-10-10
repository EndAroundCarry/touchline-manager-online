using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>A player named by his side (0 home, 1 away) and his slot number (1…11), which outlives a seat.</summary>
/// <param name="Side">0 for the home side, 1 for the away side, or -1 for nobody.</param>
/// <param name="Slot">The slot number, 1…11.</param>
internal readonly record struct TickRef(int Side, int Slot)
{
    /// <summary>Nobody.</summary>
    public static readonly TickRef None = new(-1, 0);

    /// <summary>Gets whether this names nobody.</summary>
    public bool IsNone => Side < 0;
}

/// <summary>
/// Plays a whole match at 10 ticks a second (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// <para>
/// This is the loop the earlier milestones were written for. Each tick, in order: the state machine says whether the ball is dead
/// (a restart being set up, a goal being celebrated) or in play; the dynamic anchors of M2 are resolved for both sides; the defending
/// side's jobs (M3) and the attacking side's (M4) are assigned from the same start-of-tick picture; the goalkeepers decide (M6); the
/// man with the ball decides what to do with it (M5) and, if he kicks, the kick leaves his foot; every body is steered and stepped
/// (M1, M2); the ball is carried or stepped, a challenge on the carrier is settled (M3), a shot is met by the keeper (M6), the ball
/// is received by whoever reaches it, or it leaves the pitch and a restart begins (M7).
/// </para>
/// <para>
/// The loop is the only thing in the tick engine that touches <see cref="MatchState"/>: it emits the events, keeps the goals,
/// assists, passes and dribbles each player is credited with, drives the possession engine's discipline, injury and substitution
/// planners at stoppages, and writes the minute-by-minute condition and rating curve. Because the legacy planners and the result
/// builder work on the same state, a tick match produces the same <see cref="MatchResultV1"/> a possession match does.
/// </para>
/// <para>
/// When the caller supplied a <see cref="MatchPassageRecorder"/> the loop also writes every tick into a
/// <see cref="TickMatchRecording"/> for the replay. Recording reads no draw and changes no state, so the result is identical with and
/// without it. Everything works on arrays allocated once, spans and stack buffers: the hot path allocates nothing except where it
/// appends to the event log.
/// </para>
/// </remarks>
internal sealed partial class TickMatchLoop
{
    /// <summary>The ticks a goalkeeper holds the ball before he plays it (2.5 s).</summary>
    public const int KeeperHoldTicks = 25;

    /// <summary>The seconds between injury rolls.</summary>
    public const int InjuryRollSeconds = 30;

    /// <summary>The ticks the man who kicked the ball may not take it back (0.4 s).</summary>
    public const int KickCooldownTicks = 4;

    /// <summary>The seconds past the stoppage a half may run on waiting for a quiet moment.</summary>
    public const int HalfOverrunSeconds = 90;

    private readonly MatchState _state;
    private readonly EngineRulesV2 _rules;
    private readonly Pcg32 _random;
    private readonly TickTeam[] _teams;
    private readonly TickBallPhysics _ball = new();
    private readonly TickBallPhysics _scratch = new();
    private readonly TickMatchStateMachine _machine = new();
    private readonly TickMatchRecording? _recording;

    private int _tick;
    private int _period = 1;
    private int _secondTicks;
    private int _eventsSeen;
    private TickFrameFlags _pendingFlags;
    private bool _celebrationScorerIsHome;

    private TickMatchLoop(MatchState state)
    {
        _state = state;
        _rules = state.Rules;
        _random = state.Random;
        _teams = [new TickTeam(state.Home), new TickTeam(state.Away)];
        _recording = state.Passages is { } recorder ? recorder.StartTickRecording() : null;

        if (_recording is not null)
        {
            foreach (var team in _teams)
            {
                for (var seat = 0; seat < team.Count; seat++)
                {
                    _recording.Starters[team.Entity(seat)] = team.Id[seat];
                }
            }
        }
    }

    /// <summary>Plays the whole match on a state: both halves and their stoppage.</summary>
    /// <param name="state">The match state, with nothing yet emitted.</param>
    public static void Play(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        new TickMatchLoop(state).Run();
    }

    private void Run()
    {
        var state = _state;

        state.BeginHalf(firstHalf: true);
        _period = 1;
        Emit(MatchSide.Home, EngineEventType.KickOff);
        state.CaptureLiveMetrics();
        StartHalf(homeKicksOff: true);
        PlayHalf();
        state.EndHalf();
        Emit(MatchSide.Home, EngineEventType.HalfTime);
        TakeInterval();

        state.BeginHalf(firstHalf: false);
        _period = 2;
        state.MoveBall(SpatialPoint.Center);
        Emit(MatchSide.Away, EngineEventType.SecondHalfStart);
        state.CaptureLiveMetrics();
        StartHalf(homeKicksOff: false);
        PlayHalf();
        state.EndHalf();
        PushConditions();
        Emit(MatchSide.Home, EngineEventType.FullTime);
        state.CaptureLiveMetrics();
        FlushEvents();
        state.MoveBall(new SpatialPoint(_ball.UnitX, _ball.UnitY));
    }

    /// <summary>Puts both sides on their kick-off positions and sets the kick-off up.</summary>
    private void StartHalf(bool homeKicksOff)
    {
        foreach (var team in _teams)
        {
            team.PlaceForKickOff(homeKicksOff);
            team.SupporterMask = 0;
            team.RunnerMask = 0;
            team.PreviousPresser = -1;
        }

        _pendingFlags |= TickFrameFlags.PlayersPlaced;
        _ball.PlaceAt(SpatialPitch.PitchLength / 2, SpatialPitch.GoalYCenter);
        _pendingFlags |= TickFrameFlags.BallPlaced;
        ResetPlay();

        var restart = TickRestart.KickOff(homeKicksOff);

        _machine.Begin(restart);
        _recording?.AddStoppage(TickPlayState.KickOffPending, TickRestartKind.KickOff, homeKicksOff);
        LayOut(restart);
    }

    private void PlayHalf()
    {
        while (true)
        {
            StepTick();

            if (_state.HalfIsOver && IsQuiet())
            {
                break;
            }

            if (_state.ClockSeconds >= _state.HalfRegulationEndSeconds + _state.HalfStoppageSeconds + HalfOverrunSeconds)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Tells whether the referee can blow for the end of a half: play is not in the middle of a chance. A corner, a free kick or a
    /// penalty being taken is played out, as it is on a real pitch.
    /// </summary>
    private bool IsQuiet()
    {
        switch (_machine.State)
        {
            case TickPlayState.GoalCelebration:
            case TickPlayState.KickOffPending:
            case TickPlayState.GoalKickPending:
            case TickPlayState.ThrowInPending:
                return true;

            case TickPlayState.OpenPlay:
                return !_shotLive && _ball.UnitX is > 2_500 and < 7_500;

            default:
                return false;
        }
    }

    /// <summary>The interval: the legs and the fatigue recover and the bench is looked at.</summary>
    private void TakeInterval()
    {
        _machine.BeginHalfTime();
        PushConditions();

        foreach (var team in _teams)
        {
            team.Rest(_rules.HalfTimeConditionRecoveryBasisPoints);
            team.Runtime.ApplyHalfTimeRecovery(_rules);
        }

        PushConditions();
        FlushEvents();
    }

    // ---- One tick --------------------------------------------------------------------------------------------------------------

    private void StepTick()
    {
        _tick++;

        foreach (var team in _teams)
        {
            Array.Clear(team.HasIntent);
        }

        if (_machine.IsDeadBall)
        {
            StepDeadBall();
        }
        else
        {
            StepOpenPlay();

            if (_recording is not null && !_machine.IsDeadBall)
            {
                TrackBypassed();
            }
        }

        Record();
        AdvanceClock();
    }

    private void AdvanceClock()
    {
        if (++_secondTicks < TickSpatialUnits.TicksPerSecond)
        {
            return;
        }

        _secondTicks = 0;
        _state.ClockSeconds++;

        if (!_machine.IsDeadBall)
        {
            var side = _ball.Mode == TickBallMode.Controlled ? _controllerSide : (_lastTouch.IsNone ? 0 : _lastTouch.Side);

            _teams[side].Runtime.PossessionSeconds++;
        }

        if (_state.ClockSeconds % InjuryRollSeconds == 0)
        {
            _state.MoveBall(new SpatialPoint(_ball.UnitX, _ball.UnitY), _ball.UnitZ);
            InjurySimulator.TryResolveInjury(_state);
        }

        // The end of a minute: the legs, the ratings and the curve the match center draws.
        if (_state.ClockSeconds % _rules.SecondsPerMinute == _rules.SecondsPerMinute - 1)
        {
            EndOfMinute();
        }
    }

    private void EndOfMinute()
    {
        PushConditions();
        AddMinuteLoad();
        _state.RefreshRatings();
        _state.CaptureLiveMetrics();
    }

    /// <summary>Writes the legs the bodies have into the runtime's condition, and adds a minute's fatigue and sharpness.</summary>
    private void PushConditions()
    {
        foreach (var team in _teams)
        {
            team.PushCondition();
        }
    }

    /// <summary>
    /// Adds a minute of fatigue and sharpness to everybody on the pitch, at the rate the possession engine added them per possession
    /// (about two possessions a minute).
    /// </summary>
    private void AddMinuteLoad()
    {
        foreach (var team in _teams)
        {
            var active = team.Runtime.Active;

            for (var index = 0; index < active.Count; index++)
            {
                active[index] = active[index] with
                {
                    Condition = active[index].Condition
                        .WithFatigueDelta(_rules.FatigueGainPerPossessionBasisPoints * 2)
                        .WithSharpnessDelta(_rules.SharpnessGainPerPossessionBasisPoints * 2),
                };
            }
        }
    }

    // ---- Events and the recording -------------------------------------------------------------------------------------------------

    private EngineEventV1 Emit(
        MatchSide side,
        EngineEventType type,
        Guid? participantId = null,
        Guid? secondaryParticipantId = null,
        ShotZone? zone = null,
        int? qualityBasisPoints = null,
        SpatialPoint? at = null)
    {
        _state.MoveBall(new SpatialPoint(_ball.UnitX, _ball.UnitY), _ball.UnitZ);

        return _state.Emit(side, type, participantId, secondaryParticipantId, zone, qualityBasisPoints, at: at);
    }

    /// <summary>Notes the events the legacy planners emitted straight onto the log, against the tick they happened in.</summary>
    private void FlushEvents()
    {
        for (; _eventsSeen < _state.Events.Count; _eventsSeen++)
        {
            _recording?.AddEvent(_state.Events[_eventsSeen].Sequence);
        }
    }

    private void Tag(TickRef who, PassageAction action) => _recording?.AddAction(EntityOf(who), action);

    private static int EntityOf(TickRef who) => who.IsNone ? -1 : (who.Side == 0 ? 0 : TickMatchRecording.Entities / 2) + who.Slot - 1;

    private void Record()
    {
        FlushEvents();

        if (_recording is null)
        {
            _pendingFlags = TickFrameFlags.None;

            return;
        }

        var controller = -1;

        if (_ball.Mode == TickBallMode.Controlled && _ball.ControllerIndex >= 0 && _ball.ControllerIndex < _teams[_controllerSide].Count)
        {
            controller = _teams[_controllerSide].Entity(_ball.ControllerIndex);
        }

        _recording.BeginFrame(_state.ClockSeconds, _period, _machine.State, controller, _pendingFlags);
        _pendingFlags = TickFrameFlags.None;

        foreach (var team in _teams)
        {
            for (var seat = 0; seat < team.Count; seat++)
            {
                _recording.SetPlayer(team.Entity(seat), TickSpatialUnits.ToUnits(team.Body[seat].X), TickSpatialUnits.ToUnits(team.Body[seat].Y));
            }
        }

        _recording.SetBall(_ball.UnitX, _ball.UnitY, _ball.UnitZ);
    }

    // ---- Stoppages: the planners ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Play has stopped: the possession engine's planners get their chance. A card has already taken a player off (the discipline
    /// rules remove him), an injured player is replaced or leaves, and a tired one is changed if a window has opened.
    /// </summary>
    private void Stoppage()
    {
        PushConditions();
        _state.MoveBall(new SpatialPoint(_ball.UnitX, _ball.UnitY), _ball.UnitZ);
        SubstitutionPlanner.ConsiderBothSides(_state);
        _state.LastPlannerMinute = _state.Minute;

        foreach (var team in _teams)
        {
            team.Sync(_recording);
        }
    }
}
