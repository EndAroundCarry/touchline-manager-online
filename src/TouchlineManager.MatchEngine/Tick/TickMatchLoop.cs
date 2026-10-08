using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// The tick engine's match loop: it drives the twenty-two autonomous players and the ball through a whole match at
/// 10 Hz, and records the continuous trace the replay synthesizer turns into a film (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// <para>
/// The loop is the one place that knows the match as a flow rather than a moment: it decides who is in possession,
/// hands the attacking side to <see cref="TickOffBallSupport"/>, the defending side to <see cref="TickDefensiveAI"/>,
/// the man on the ball to <see cref="TickBallCarrierBrain"/>, both goalkeepers to <see cref="TickGoalkeeperAI"/> and
/// the dead balls to <see cref="TickMatchStateMachine"/>, then steps every body and the ball one tick. Everything the
/// components decide is a target or a kick; the loop is what makes them a match.
/// </para>
/// <para><b>Per tick, in open play, in order:</b></para>
/// <list type="number">
/// <item><description>Who has the ball, from <see cref="TickBallPhysics.ControllerIndex"/> or, while it is loose and travelling, from who touched it last.</description></item>
/// <item><description>Both sides' dynamic anchors (<see cref="TickTacticalGeometry.ResolveTeam"/>).</description></item>
/// <item><description>The attacking side's off-the-ball orders (<see cref="TickOffBallSupport.Assign"/>), and the defending side's (<see cref="TickDefensiveAI.Assign"/>) — press, mark, screen, hold the line.</description></item>
/// <item><description>The carrier's decision on his own interval (<see cref="TickBallCarrierBrain.Decide"/>) and, when it is a kick, the kick (<see cref="TickBallCarrierBrain.Execute"/>); a dribble or a shield becomes the carrier's movement.</description></item>
/// <item><description>Both goalkeepers' arcs and rushes (<see cref="TickGoalkeeperAI.Decide"/>), or the dive at a shot already in flight (<see cref="TickShotStopper.DiveIntent"/>).</description></item>
/// <item><description>Every player moves one tick (<see cref="TickSteering.Steer"/> and <see cref="TickPlayerPhysics.Step"/>), and a carried ball moves with its player.</description></item>
/// <item><description>A challenge within contact range (<see cref="TickTackleResolver"/>), or the goalkeeper's smother at an attacker's feet (<see cref="TickGoalkeeperAI.Smother"/>).</description></item>
/// <item><description>The ball: a save is settled at the goalkeeper's plane (<see cref="TickShotStopper"/>), otherwise the ball steps and its boundary — goal, touchline, goal line, woodwork — is resolved.</description></item>
/// <item><description>Reception: the nearest player within reach takes a loose ball (<see cref="TickBallPhysics.IsReceivableFrom"/>), so passes are completed, intercepted or blocked by geometry rather than by a roll.</description></item>
/// </list>
/// <para><b>Dead balls.</b> The match state machine decides which restart is being set up; while it holds, all
/// twenty-two are steered into the set-piece picture (<see cref="TickSetPieces.Assign"/>), and on the tick the hold
/// ends the kick is taken (<see cref="TickSetPieces.Kick"/>) and play continues from it. A goal is celebrated and
/// chains into the conceding side's kick-off, exactly as the machine specifies.</para>
/// <para><b>Facts on the log.</b> The loop emits the events the match result and the commentary are built from:
/// kick-offs, half-time, goals, shots saved, off target or blocked, woodwork, fouls and cards (through
/// <see cref="DisciplineSimulator"/>), offsides, corners and penalties. Free kicks, penalties and cards take their
/// draws from the match stream, so a match is deterministic; the order of draws is fixed by the order of play.</para>
/// <para><b>Conditions and ratings.</b> Each new match minute the players' condition bars are written from the
/// physical energy the tick physics spent, the sides' unit ratings are refreshed, and the minute's live metrics are
/// captured, so the replay's condition and rating curves are the same run that produced the score.</para>
/// <para><b>What is deferred.</b> Substitutions, injuries, morale drift and fatigue accumulation are not part of
/// this loop yet: they belong to the match-day model the plug-in milestone wires in, and nothing in the loop reads
/// or depends on them. Reading the recording changes nothing: a match run with and without one is identical.</para>
/// </remarks>
internal sealed class TickMatchLoop
{
    private const int Home = 0;
    private const int Away = 1;

    private readonly MatchState _state;
    private readonly TickMatchRecorder? _recorder;
    private readonly TickBallPhysics _ball = new();
    private readonly TickBallPhysics _scratch = new();
    private readonly TickMatchStateMachine _machine = new();

    private readonly TickPlayerState[][] _players = [new TickPlayerState[TickTacticalGeometry.TeamSize], new TickPlayerState[TickTacticalGeometry.TeamSize]];
    private readonly TickPlayerProfile[][] _profiles = [new TickPlayerProfile[TickTacticalGeometry.TeamSize], new TickPlayerProfile[TickTacticalGeometry.TeamSize]];
    private readonly TickPlayerSkills[][] _skills = [new TickPlayerSkills[TickTacticalGeometry.TeamSize], new TickPlayerSkills[TickTacticalGeometry.TeamSize]];
    private readonly TickAnchorSpec[][] _specs = [new TickAnchorSpec[TickTacticalGeometry.TeamSize], new TickAnchorSpec[TickTacticalGeometry.TeamSize]];
    private readonly ActiveSlot[][] _slots = [new ActiveSlot[TickTacticalGeometry.TeamSize], new ActiveSlot[TickTacticalGeometry.TeamSize]];
    private readonly int[][] _entities = [new int[TickTacticalGeometry.TeamSize], new int[TickTacticalGeometry.TeamSize]];
    private readonly TickTeamStyle[] _style = new TickTeamStyle[2];
    private readonly int[] _count = [TickTacticalGeometry.TeamSize, TickTacticalGeometry.TeamSize];

    private readonly SpatialPoint[][] _setupTargets = [new SpatialPoint[TickTacticalGeometry.TeamSize], new SpatialPoint[TickTacticalGeometry.TeamSize]];
    private readonly int[] _supporters = new int[2];
    private readonly int[] _runners = new int[2];
    private readonly int[] _presser = [-1, -1];
    private readonly bool[] _keeperRushing = new bool[2];
    private readonly int[] _sideLastKickerLocal = [-1, -1];

    private readonly int[] _entityX = new int[TickMatchRecording.EntityCount];
    private readonly int[] _entityY = new int[TickMatchRecording.EntityCount];
    private readonly int[] _entityZ = new int[TickMatchRecording.EntityCount];

    private TickRestartPlan _setupPlan;
    private TickMatchPhase _setupPhase = TickMatchPhase.OpenPlay;

    private int _tick;
    private int _ticksInSecond;
    private bool _halfOver;
    private int _lastMetricMinute = -1;
    private int _stampedEvents;

    private MatchSide? _controllerSide;
    private MatchSide? _lastTouchSide;
    private bool _lastKickWasPass;
    private int _lastPasserSide = -1;
    private int _lastPasserLocal = -1;

    private int _guardSide = -1;
    private int _guardLocal = -1;
    private int _guardEntity = -1;

    private int _receiverSide = -1;
    private int _receiverLocal = -1;

    private TickCarrierDecision _decision;
    private int _decisionIn = 1;
    private int _shieldTicks;

    private TickShotForecast _shotForecast = TickShotForecast.None;
    private int _shotTicks;
    private int _shotShooterLocal = -1;
    private MatchSide? _shotSide;
    private bool _penaltyInFlight;
    private bool _offsideFreeKick;

    /// <summary>Runs a whole match: both halves, stoppage, and every dead ball.</summary>
    /// <param name="state">The match state, with its two sides already built and its clock at zero.</param>
    /// <param name="recorder">
    /// The recorder the tick trace is captured into, or null when no replay is being derived. Like the passage
    /// recorder, it is a by-product of the same run: it reads state and writes nothing, so the match is identical
    /// with and without one.
    /// </param>
    public static void Run(MatchState state, TickMatchRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        new TickMatchLoop(state, recorder).Play();
    }

    private TickMatchLoop(MatchState state, TickMatchRecorder? recorder)
    {
        _state = state;
        _recorder = recorder;
        _style[Home] = TickTeamStyle.From(state.Home.Instructions);
        _style[Away] = TickTeamStyle.From(state.Away.Instructions);

        BuildSide(Home);
        BuildSide(Away);

        RegisterEntities();
    }

    /// <summary>Takes a side's eleven into the loop, the goalkeeper first as every tick module expects.</summary>
    private void BuildSide(int side)
    {
        var runtime = _state.SideOf(SideOfIndex(side));
        var ordered = new ActiveSlot[TickTacticalGeometry.TeamSize];
        var keeperAt = -1;

        for (var index = 0; index < runtime.Active.Count; index++)
        {
            if (runtime.Active[index].Slot.Family == MatchPositionFamily.Goalkeeper)
            {
                keeperAt = index;
                break;
            }
        }

        if (keeperAt < 0)
        {
            keeperAt = 0;
        }

        var next = 1;

        for (var index = 0; index < runtime.Active.Count; index++)
        {
            if (index != keeperAt)
            {
                ordered[next++] = runtime.Active[index];
            }
        }

        ordered[0] = runtime.Active[keeperAt];

        var isHome = side == Home;

        for (var index = 0; index < TickTacticalGeometry.TeamSize; index++)
        {
            var slot = ordered[index];
            var spec = TickAnchorSpec.From(slot.Slot);
            var ownX = spec.OwnX;
            var ownY = spec.OwnY;
            var x = isHome ? ownX : SpatialPitch.PitchLength - ownX;
            var y = isHome ? ownY : SpatialPitch.PitchWidth - ownY;

            _slots[side][index] = slot;
            _specs[side][index] = spec;
            _skills[side][index] = TickPlayerSkills.From(slot.Participant.Attributes);
            _profiles[side][index] = TickPlayerProfile.From(slot.Participant.Attributes);
            _players[side][index] = TickPlayerState.Standing(
                x,
                y,
                isHome ? 0 : TickTrigonometry.HalfTurn,
                slot.Condition.ConditionBasisPoints);
            _entities[side][index] = (side * TickTacticalGeometry.TeamSize) + index;
        }

        _count[side] = TickTacticalGeometry.TeamSize;
    }

    /// <summary>Registers the twenty-two players and the ball with the recorder, when one is attached.</summary>
    private void RegisterEntities()
    {
        if (_recorder is null)
        {
            return;
        }

        for (var side = 0; side < 2; side++)
        {
            for (var index = 0; index < TickTacticalGeometry.TeamSize; index++)
            {
                var slot = _slots[side][index];

                _recorder.AddEntity(new TickRecordingEntity(
                    _entities[side][index],
                    IsBall: false,
                    SideOfIndex(side),
                    slot.Participant.ParticipantId,
                    slot.Participant.ShirtNumber,
                    slot.Slot.Family,
                    slot.Participant.DisplayName));
            }
        }

        _recorder.AddEntity(new TickRecordingEntity(
            TickMatchRecording.BallEntityIndex,
            IsBall: true,
            MatchSide.Home,
            Guid.Empty,
            0,
            MatchPositionFamily.Goalkeeper,
            string.Empty));
    }

    /// <summary>Plays both halves, with the interval between them.</summary>
    private void Play()
    {
        _state.BeginHalf(firstHalf: true);
        _state.Emit(MatchSide.Home, EngineEventType.KickOff);
        _state.CaptureLiveMetrics();
        StampEvents();
        _machine.KickOff(MatchSide.Home);
        _ball.PlaceAt(SpatialPoint.Center.X, SpatialPoint.Center.Y);
        SyncBall();

        RunHalf();

        _state.EndHalf();
        _state.Emit(MatchSide.Home, EngineEventType.HalfTime);
        StampEvents();

        _state.Home.ApplyHalfTimeRecovery(_state.Rules);
        _state.Away.ApplyHalfTimeRecovery(_state.Rules);

        // The away side kicks the second half off from the centre spot, and whatever dead ball the first half
        // ended on is forgotten (`MAT-12`).
        _state.BeginHalf(firstHalf: false);
        _ball.PlaceAt(SpatialPoint.Center.X, SpatialPoint.Center.Y);
        SyncBall();
        _state.Emit(MatchSide.Away, EngineEventType.SecondHalfStart);
        _state.CaptureLiveMetrics();
        StampEvents();
        _machine.HalfTime();

        RunHalf();

        _state.EndHalf();
        _state.Emit(MatchSide.Home, EngineEventType.FullTime);
        _state.CaptureLiveMetrics();
        StampEvents();
    }

    private void RunHalf()
    {
        _halfOver = false;

        while (!_halfOver)
        {
            Tick();
        }
    }

    /// <summary>Advances the match one tick (100 ms).</summary>
    private void Tick()
    {
        if (_machine.IsDeadBall)
        {
            TickDeadBall();
        }
        else
        {
            TickOpenPlay();
        }

        _ticksInSecond++;

        if (_ticksInSecond >= TickSpatialUnits.TicksPerSecond)
        {
            _ticksInSecond = 0;
            _state.ClockSeconds++;
            SecondWork();

            if (_state.HalfIsOver)
            {
                _halfOver = true;
            }
        }

        RecordFrame();
        StampEvents();
        _tick++;
    }

    /// <summary>Every new match second: the possession ledger and, each new minute, the bars and the metrics.</summary>
    private void SecondWork()
    {
        _state.SideOf(_lastTouchSide ?? MatchSide.Home).PossessionSeconds++;

        var minute = _state.Minute;

        if (minute != _lastMetricMinute)
        {
            _lastMetricMinute = minute;
            WriteConditions();
            _state.RefreshRatings();
            _state.CaptureLiveMetrics();
        }
    }

    /// <summary>Writes each player's condition bar from the energy his legs have left (`tick-engine-v1`).</summary>
    private void WriteConditions()
    {
        for (var side = 0; side < 2; side++)
        {
            var runtime = _state.SideOf(SideOfIndex(side));

            for (var index = 0; index < _count[side]; index++)
            {
                var energy = _players[side][index].Energy;
                var condition = (int)((long)energy * 10_000 / TickPlayerPhysics.EnergyFull);
                var participantId = _slots[side][index].Participant.ParticipantId;

                for (var active = 0; active < runtime.Active.Count; active++)
                {
                    if (runtime.Active[active].Participant.ParticipantId != participantId)
                    {
                        continue;
                    }

                    var existing = runtime.Active[active].Condition;

                    runtime.Active[active] = runtime.Active[active] with
                    {
                        Condition = new PlayerCondition(
                            condition,
                            existing.FatigueBasisPoints,
                            existing.MoraleBasisPoints,
                            existing.SharpnessBasisPoints),
                    };

                    break;
                }
            }
        }
    }

    // ---- Dead balls ---------------------------------------------------------------------------------------------

    /// <summary>One tick of a dead ball: hold the set-piece picture, count the hold down, take the kick when it ends.</summary>
    private void TickDeadBall()
    {
        if (_machine.Phase != _setupPhase)
        {
            BeginSetup(_machine.Phase);
        }

        for (var side = 0; side < 2; side++)
        {
            StepSide(side, _setupTargets[side], default, default, keeperOverridden: false);
        }

        _machine.Step();

        if (_machine.IsKickDue)
        {
            TakeRestartKick();
        }
    }

    /// <summary>Sets up the picture for the dead ball being taken, and puts the ball on its spot.</summary>
    private void BeginSetup(TickMatchPhase phase)
    {
        _setupPhase = phase;
        ClearLiveBall();

        var restart = _machine.Restart;
        var taking = SideIndex(restart.Side);
        var defending = 1 - taking;

        _ball.PlaceAt(restart.Ball.X, restart.Ball.Y);
        SyncBall();

        var situation = RestartSituation();

        _setupPlan = TickSetPieces.Assign(situation, _setupTargets[taking], _setupTargets[defending]);

        switch (phase)
        {
            case TickMatchPhase.CornerPending:
                _state.Emit(restart.Side, EngineEventType.Corner);
                break;

            case TickMatchPhase.PenaltyPending:
                _state.Emit(restart.Side, EngineEventType.PenaltyAwarded, Participant(taking, _setupPlan.Taker));
                break;

            case TickMatchPhase.FreeKickPending when !_offsideFreeKick:
                _state.Emit(restart.Side, EngineEventType.FreeKickWon, Participant(taking, _setupPlan.Taker));
                break;

            default:
                break;
        }

        _offsideFreeKick = false;
        SyncBall();
        StampEvents();
        _recorder?.AddRestart(_tick, phase, restart.Side, restart.Ball.X, restart.Ball.Y);
    }

    /// <summary>Takes the restart the hold was been waiting for, and lets play resume from the kick.</summary>
    private void TakeRestartKick()
    {
        var restart = _machine.Restart;
        var taking = SideIndex(restart.Side);
        var situation = RestartSituation();
        var action = TickSetPieces.Kick(situation, _setupPlan, _ball, _state.Random, out var receiver);

        _machine.KickTaken();
        _setupPhase = TickMatchPhase.OpenPlay;
        SyncBall();

        if (action == TickRestartAction.None || _setupPlan.Taker < 0)
        {
            return;
        }

        var taker = _setupPlan.Taker;
        var penalty = restart.Kind == TickMatchPhase.PenaltyPending;

        BookKick(
            taking,
            taker,
            isPass: action is TickRestartAction.Pass or TickRestartAction.Throw or TickRestartAction.LongBall);
        _decisionIn = 1;
        _shieldTicks = 0;

        var touch = action switch
        {
            TickRestartAction.Cross => PassageAction.Cross,
            TickRestartAction.Shot => penalty ? PassageAction.Penalty : PassageAction.FreeKick,
            _ => PassageAction.Pass,
        };

        _recorder?.AddTouch(_tick, Entity(taking, taker), touch);

        if (action == TickRestartAction.Shot)
        {
            _lastKickWasPass = false;
            ClearReceiver();
            StartShot(restart.Side, taker, penalty);
        }
        else
        {
            _receiverSide = receiver > 0 ? taking : -1;
            _receiverLocal = receiver > 0 ? receiver : -1;

            if (receiver >= 0)
            {
                _lastPasserSide = taking;
                _lastPasserLocal = taker;
            }
        }
    }

    /// <summary>Builds the moment the set-piece picture and the restart kick read, in the taking side's own point of view.</summary>
    private TickRestartSituation RestartSituation()
    {
        var restart = _machine.Restart;
        var taking = SideIndex(restart.Side);
        var defending = 1 - taking;

        return new TickRestartSituation
        {
            Kind = restart.Kind,
            IsHome = restart.Side == MatchSide.Home,
            Ball = restart.Ball,
            Takers = _players[taking].AsSpan(0, _count[taking]),
            TakerSpecs = _specs[taking].AsSpan(0, _count[taking]),
            TakerSkills = _skills[taking].AsSpan(0, _count[taking]),
            Defenders = _players[defending].AsSpan(0, _count[defending]),
            DefenderSpecs = _specs[defending].AsSpan(0, _count[defending]),
            DefenderSkills = _skills[defending].AsSpan(0, _count[defending]),
        };
    }

    // ---- Open play -----------------------------------------------------------------------------------------------

    /// <summary>One tick of open play: orders, decisions, movement, contests, the ball, and reception.</summary>
    private void TickOpenPlay()
    {
        _setupPhase = TickMatchPhase.OpenPlay;

        var attacking = _controllerSide ?? _lastTouchSide ?? MatchSide.Home;
        var attackingIndex = SideIndex(attacking);
        var defendingIndex = 1 - attackingIndex;
        var carrier = _controllerSide is not null ? _ball.ControllerIndex : -1;
        var ballX = _ball.UnitX;
        var ballY = _ball.UnitY;

        Span<SpatialPoint> homeAnchors = stackalloc SpatialPoint[TickTacticalGeometry.TeamSize];
        Span<SpatialPoint> awayAnchors = stackalloc SpatialPoint[TickTacticalGeometry.TeamSize];
        Span<SpatialPoint> homeTargets = stackalloc SpatialPoint[TickTacticalGeometry.TeamSize];
        Span<SpatialPoint> awayTargets = stackalloc SpatialPoint[TickTacticalGeometry.TeamSize];
        Span<int> homePaces = stackalloc int[TickTacticalGeometry.TeamSize];
        Span<int> awayPaces = stackalloc int[TickTacticalGeometry.TeamSize];
        Span<TickAttackingOrder> attackingOrders = stackalloc TickAttackingOrder[TickTacticalGeometry.TeamSize];
        Span<TickDefensiveOrder> defendingOrders = stackalloc TickDefensiveOrder[TickTacticalGeometry.TeamSize];

        TickTacticalGeometry.ResolveTeam(
            _specs[Home].AsSpan(0, _count[Home]), _style[Home], true, attacking == MatchSide.Home, ballX, ballY, homeAnchors);
        TickTacticalGeometry.ResolveTeam(
            _specs[Away].AsSpan(0, _count[Away]), _style[Away], false, attacking == MatchSide.Away, ballX, ballY, awayAnchors);

        var attackingTargets = attackingIndex == Home ? homeTargets : awayTargets;
        var attackingPaces = attackingIndex == Home ? homePaces : awayPaces;
        var attackingAnchors = attackingIndex == Home ? homeAnchors : awayAnchors;
        var defendingTargets = defendingIndex == Home ? homeTargets : awayTargets;
        var defendingPaces = defendingIndex == Home ? homePaces : awayPaces;
        var defendingAnchors = defendingIndex == Home ? homeAnchors : awayAnchors;

        // The attacking side: passing triangles, runs, pockets and overlaps, or plain shape while the ball travels.
        if (carrier >= 0)
        {
            var attackingSituation = new TickAttackingSituation
            {
                IsHome = attacking == MatchSide.Home,
                Mentality = Instructions(attackingIndex).Mentality,
                Passing = Instructions(attackingIndex).Passing,
                Attackers = _players[attackingIndex].AsSpan(0, _count[attackingIndex]),
                Specs = _specs[attackingIndex].AsSpan(0, _count[attackingIndex]),
                Anchors = attackingAnchors,
                Skills = _skills[attackingIndex].AsSpan(0, _count[attackingIndex]),
                Defenders = _players[defendingIndex].AsSpan(0, _count[defendingIndex]),
                CarrierIndex = carrier,
                PreviousSupporters = _supporters[attackingIndex],
                PreviousRunners = _runners[attackingIndex],
            };

            TickOffBallSupport.Assign(attackingSituation, attackingOrders);
            TickOffBallSupport.ToSteering(attackingOrders, attackingTargets, attackingPaces);
            _supporters[attackingIndex] = TickOffBallSupport.SupporterMask(attackingOrders);
            _runners[attackingIndex] = TickOffBallSupport.RunnerMask(attackingOrders);
        }
        else
        {
            attackingAnchors.CopyTo(attackingTargets);
            attackingPaces.Clear();
            ChaseLooseBall(attackingIndex, attackingTargets, attackingPaces);
        }

        // The defending side: one presser at most, markers, cover shadows and the line.
        var defendingSituation = new TickDefensiveSituation
        {
            IsHome = defendingIndex == Home,
            Pressing = Instructions(defendingIndex).Pressing,
            Defenders = _players[defendingIndex].AsSpan(0, _count[defendingIndex]),
            Specs = _specs[defendingIndex].AsSpan(0, _count[defendingIndex]),
            Anchors = defendingAnchors,
            Skills = _skills[defendingIndex].AsSpan(0, _count[defendingIndex]),
            Attackers = _players[attackingIndex].AsSpan(0, _count[attackingIndex]),
            BallX = ballX,
            BallY = ballY,
            CarrierIndex = carrier,
            PreviousPresser = _presser[defendingIndex],
        };

        TickDefensiveAI.Assign(defendingSituation, defendingOrders);
        TickDefensiveAI.ToSteering(defendingOrders, defendingTargets, defendingPaces);
        _presser[defendingIndex] = FindPresser(defendingOrders);

        // A ball in flight is contested by both sides: the presser runs at where it will be, as the receiver does,
        // so a cross into the box is a race rather than an uncontested delivery.
        if (carrier < 0 && _ball.Mode != TickBallMode.Controlled && _presser[defendingIndex] > 0)
        {
            defendingTargets[_presser[defendingIndex]] = PredictedBallPoint(defendingIndex, _presser[defendingIndex]);
            defendingPaces[_presser[defendingIndex]] = 10_000;
        }

        // The man on the ball decides, on his own interval, and acts.
        if (carrier >= 0)
        {
            CarrierTurn(attackingIndex, defendingIndex, carrier, attackingOrders, attackingTargets, attackingPaces);

            carrier = _controllerSide is not null ? _ball.ControllerIndex : -1;
        }

        // Both goalkeepers: their arcs, their rushes, or the dive at a shot already in flight.
        var homeKeeper = KeeperIntent(Home, homeAnchors);
        var awayKeeper = KeeperIntent(Away, awayAnchors);

        if (_shotForecast.OnTarget && _shotSide is MatchSide shootingSide)
        {
            var keeperSide = SideIndex(shootingSide) == Home ? Away : Home;
            var dive = TickShotStopper.DiveIntent(
                _players[keeperSide][0], _skills[keeperSide][0], _shotForecast, _shotTicks);

            if (keeperSide == Home)
            {
                homeKeeper = dive;
            }
            else
            {
                awayKeeper = dive;
            }
        }

        // Everyone moves; a carried ball moves with its player; the two contests resolve against fresh bodies.
        StepSide(Home, homeTargets, homePaces, homeKeeper);
        StepSide(Away, awayTargets, awayPaces, awayKeeper);

        if (_controllerSide is MatchSide controlling)
        {
            CarryControlled(controlling);
        }

        TrySmother(defendingIndex, attackingIndex, carrier);

        if (_controllerSide is MatchSide holder)
        {
            TryTackle(attackingIndex, defendingIndex, holder, _ball.ControllerIndex);
        }

        // The ball: a save is settled at the plane, otherwise it steps and its boundary is answered.
        if (_ball.Mode == TickBallMode.Controlled)
        {
            CheckSelfPassGuard();
            return;
        }

        if (_shotForecast != TickShotForecast.None && TickShotStopper.ShouldResolve(_shotForecast, _shotTicks))
        {
            ResolveSave();
        }
        else
        {
            var boundary = _ball.Step();

            if (_shotForecast != TickShotForecast.None)
            {
                _shotTicks++;
            }

            SyncBall();

            if (boundary != TickBallBoundary.InPlay)
            {
                HandleBoundary(boundary);

                return;
            }

            CheckSelfPassGuard();
            TryReception();
        }
    }

    /// <summary>
    /// While the ball is loose the attacking side sends one player to run onto it: the man the pass was meant
    /// for, or — after a rebound, a clearance or a poke — the nearest outfielder. He runs at where the ball will
    /// be, not where it is, so a pass is contested by the man it was played to rather than conceded to whoever
    /// happens to be chasing the ball itself.
    /// </summary>
    private void ChaseLooseBall(int attackingIndex, Span<SpatialPoint> targets, Span<int> paces)
    {
        if (_ball.Mode == TickBallMode.Controlled)
        {
            return;
        }

        var local = -1;

        if (_receiverSide == attackingIndex && _receiverLocal > 0 && _receiverLocal < _count[attackingIndex])
        {
            local = _receiverLocal;
        }
        else
        {
            long nearest = long.MaxValue;

            for (var index = 1; index < _count[attackingIndex]; index++)
            {
                long dx = _players[attackingIndex][index].X - _ball.X;
                long dy = _players[attackingIndex][index].Y - _ball.Y;
                var distance = (dx * dx) + (dy * dy);

                if (distance < nearest)
                {
                    nearest = distance;
                    local = index;
                }
            }
        }

        if (local < 0)
        {
            return;
        }

        targets[local] = PredictedBallPoint(attackingIndex, local);
        paces[local] = 10_000;
    }

    /// <summary>
    /// Gets where a player should run to meet the ball: a point along its flight, led by the time he needs to
    /// cover the gap at his top speed. The lead is what lets a player run onto a moving ball instead of chasing it.
    /// </summary>
    private SpatialPoint PredictedBallPoint(int side, int local)
    {
        var player = _players[side][local];
        var topSpeed = Math.Max(1, TickPlayerPhysics.EffectiveTopSpeed(player, _profiles[side][local]) / TickSpatialUnits.FixedScale);
        long toBallX = _ball.X - player.X;
        long toBallY = _ball.Y - player.Y;
        var gap = SpatialMath.Sqrt((toBallX * toBallX) + (toBallY * toBallY)) / TickSpatialUnits.FixedScale;
        var lead = (int)Math.Clamp(gap / topSpeed, 0, 12);
        var x = _ball.UnitX + (int)((long)_ball.VelocityX * lead / TickSpatialUnits.FixedScale);
        var y = _ball.UnitY + (int)((long)_ball.VelocityY * lead / TickSpatialUnits.FixedScale);

        return new SpatialPoint(
            Math.Clamp(x, TickTacticalGeometry.Margin, SpatialPitch.PitchLength - TickTacticalGeometry.Margin),
            Math.Clamp(y, TickTacticalGeometry.Margin, SpatialPitch.PitchWidth - TickTacticalGeometry.Margin));
    }

    /// <summary>The carrier's decision and what the loop does about it: a kick, or a dribble or shield he moves on.</summary>
    private void CarrierTurn(
        int attackingIndex,
        int defendingIndex,
        int carrier,
        ReadOnlySpan<TickAttackingOrder> orders,
        Span<SpatialPoint> targets,
        Span<int> paces)
    {
        _decisionIn--;

        if (_decisionIn <= 0)
        {
            var situation = new TickCarrierSituation
            {
                IsHome = attackingIndex == Home,
                Mentality = Instructions(attackingIndex).Mentality,
                Tempo = Instructions(attackingIndex).Tempo,
                Passing = Instructions(attackingIndex).Passing,
                Focus = Instructions(attackingIndex).PassFocus,
                Attackers = _players[attackingIndex].AsSpan(0, _count[attackingIndex]),
                Specs = _specs[attackingIndex].AsSpan(0, _count[attackingIndex]),
                Skills = _skills[attackingIndex].AsSpan(0, _count[attackingIndex]),
                Defenders = _players[defendingIndex].AsSpan(0, _count[defendingIndex]),
                CarrierIndex = carrier,
                Orders = orders,
                ShieldTicks = _shieldTicks,
            };

            _decision = TickBallCarrierBrain.Decide(situation);
            _decisionIn = TickBallCarrierBrain.DecisionInterval(
                _skills[attackingIndex][carrier], Instructions(attackingIndex).Tempo);

            if (_decision.Action == TickCarrierAction.Shield)
            {
                _shieldTicks++;
            }
            else
            {
                _shieldTicks = 0;
            }

            if (_decision.Action is TickCarrierAction.Dribble)
            {
                _recorder?.AddTouch(_tick, Entity(attackingIndex, carrier), PassageAction.Carry);
            }
            else if (_decision.Action is not TickCarrierAction.Shield)
            {
                ExecuteDecision(attackingIndex, defendingIndex, carrier);
            }
        }

        if (_controllerSide is not null && _decision.Action is TickCarrierAction.Dribble or TickCarrierAction.Shield)
        {
            targets[carrier] = _decision.Target;
            paces[carrier] = _decision.PaceBasisPoints;
        }
    }

    /// <summary>Hits the kick the carrier decided on, unless it was a pass to a player standing offside.</summary>
    private void ExecuteDecision(int attackingIndex, int defendingIndex, int carrier)
    {
        var attacker = SideOfIndex(attackingIndex);
        var receiver = _decision.Receiver;

        if (receiver >= 0 && _decision.Action is TickCarrierAction.Pass or TickCarrierAction.ThroughBall)
        {
            var offside = TickDefensiveAI.IsOffside(
                _players[attackingIndex][receiver].X,
                _ball.X,
                _players[defendingIndex].AsSpan(0, _count[defendingIndex]),
                defendingIndex == Home);

            if (offside)
            {
                SyncBall();
                TickDefensiveAI.FlagOffside(
                    _state,
                    attacker,
                    Participant(attackingIndex, receiver),
                    Participant(attackingIndex, carrier));
                _offsideFreeKick = true;
                _shotForecast = TickShotForecast.None;
                _shotSide = null;
                _controllerSide = null;

                var spot = _players[attackingIndex][receiver];
                _machine.Foul(
                    new SpatialPoint(TickSpatialUnits.ToUnits(spot.X), TickSpatialUnits.ToUnits(spot.Y)),
                    Other(attacker));
                StampEvents();

                return;
            }
        }

        var kicked = TickBallCarrierBrain.Execute(_decision, _skills[attackingIndex][carrier], _ball, _state.Random);

        if (!kicked)
        {
            return;
        }

        var isPass = _decision.Action is TickCarrierAction.Pass or TickCarrierAction.ThroughBall or TickCarrierAction.Recycle;
        var isShot = _decision.Action is TickCarrierAction.Shoot;
        var isCross = _decision.Action is TickCarrierAction.Cross;

        BookKick(attackingIndex, carrier, isPass);
        _lastKickWasPass = isPass;
        _shieldTicks = 0;

        if (isPass)
        {
            _lastPasserSide = attackingIndex;
            _lastPasserLocal = carrier;

            var passerId = Participant(attackingIndex, carrier);

            _state.SideOf(attacker).PassesAttempted[passerId] =
                _state.SideOf(attacker).PassesAttempted.TryGetValue(passerId, out var attempted) ? attempted + 1 : 1;
        }
        else
        {
            _lastPasserSide = -1;
            _lastPasserLocal = -1;
        }

        _recorder?.AddTouch(
            _tick,
            Entity(attackingIndex, carrier),
            isShot ? PassageAction.Shot : (isCross ? PassageAction.Cross : PassageAction.Pass));

        if (isShot)
        {
            ClearReceiver();
            StartShot(attacker, carrier, penalty: false);
        }
        else
        {
            _receiverSide = receiver > 0 ? attackingIndex : -1;
            _receiverLocal = receiver > 0 ? receiver : -1;
        }
    }

    /// <summary>Steps one side: everyone towards his target, the goalkeeper on his own order.</summary>
    private void StepSide(
        int side,
        ReadOnlySpan<SpatialPoint> targets,
        ReadOnlySpan<int> paces,
        in TickMoveIntent keeperIntent,
        bool keeperOverridden = true)
    {
        Span<TickMoveIntent> intents = stackalloc TickMoveIntent[TickTacticalGeometry.TeamSize];

        var players = _players[side];
        var profiles = _profiles[side];
        var count = _count[side];

        for (var index = 0; index < count; index++)
        {
            intents[index] = TickSteering.Steer(
                index,
                players.AsSpan(0, count),
                profiles[index],
                targets[index],
                paces.IsEmpty ? 0 : paces[index]);
        }

        if (keeperOverridden)
        {
            intents[0] = keeperIntent;
        }

        for (var index = 0; index < count; index++)
        {
            TickPlayerPhysics.Step(ref players[index], profiles[index], intents[index]);
        }
    }

    /// <summary>Works out where a goalkeeper goes this tick when there is no shot to face.</summary>
    private TickMoveIntent KeeperIntent(int side, ReadOnlySpan<SpatialPoint> anchors)
    {
        var possession = _controllerSide is not MatchSide controlling
            ? TickKeeperPossession.Loose
            : (controlling == SideOfIndex(side) ? TickKeeperPossession.HeldByOwn : TickKeeperPossession.HeldByOpponent);

        var situation = new TickKeeperSituation
        {
            IsHome = side == Home,
            Keeper = _players[side][0],
            Skills = _skills[side][0],
            Anchor = anchors[0],
            Ball = _ball,
            Possession = possession,
            Teammates = _players[side].AsSpan(0, _count[side]),
            Opponents = _players[1 - side].AsSpan(0, _count[1 - side]),
            WasRushing = _keeperRushing[side],
        };

        var order = TickGoalkeeperAI.Decide(situation);
        _keeperRushing[side] = order.Mode == TickKeeperMode.Rush;

        return order.Intent;
    }

    /// <summary>Holds a controlled ball at its player's dribbling foot.</summary>
    private void CarryControlled(MatchSide controlling)
    {
        var side = SideIndex(controlling);
        var local = _ball.ControllerIndex;

        if (local < 0 || local >= _count[side])
        {
            _ball.Release();
            _controllerSide = null;

            return;
        }

        var player = _players[side][local];

        _ball.Carry(player.X, player.Y, player.Heading, player.Speed);
        _lastTouchSide = controlling;
        SyncBall();
    }

    /// <summary>A challenge on the carrier by the nearest defender within contact range.</summary>
    private void TryTackle(int attackingIndex, int defendingIndex, MatchSide holder, int carrier)
    {
        var defenders = _players[defendingIndex];
        var attacker = _players[attackingIndex][carrier];
        var best = -1;
        long bestDistance = long.MaxValue;

        for (var index = 1; index < _count[defendingIndex]; index++)
        {
            if (!TickTackleResolver.InContact(defenders[index], attacker))
            {
                continue;
            }

            long dx = defenders[index].X - attacker.X;
            long dy = defenders[index].Y - attacker.Y;
            var distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        if (best < 0)
        {
            return;
        }

        var rules = _state.Rules;
        var outcome = TickTackleResolver.Resolve(
            _skills[defendingIndex][best],
            _skills[attackingIndex][carrier],
            Instructions(defendingIndex).Tackling,
            rules,
            _state.Random);

        TickTackleResolver.Apply(outcome, ref defenders[best], ref _players[attackingIndex][carrier], best, _ball);
        SyncBall();

        switch (outcome)
        {
            case TickTackleOutcome.Won:
                _controllerSide = SideOfIndex(defendingIndex);
                _decisionIn = 1;
                _shieldTicks = 0;
                ClearShot();
                ClearReceiver();
                AdjustRating(defendingIndex, best, rules.LiveRatingTackleBonusBasisPoints);
                AdjustRating(attackingIndex, carrier, -rules.LiveRatingTackleLostPenaltyBasisPoints);
                _recorder?.AddTouch(_tick, Entity(defendingIndex, best), PassageAction.Tackle);
                break;

            case TickTackleOutcome.PokedLoose:
                _controllerSide = null;
                _lastTouchSide = SideOfIndex(defendingIndex);
                ClearShot();
                ClearReceiver();
                AdjustRating(defendingIndex, best, rules.LiveRatingTackleBonusBasisPoints);
                AdjustRating(attackingIndex, carrier, -rules.LiveRatingTackleLostPenaltyBasisPoints);
                _recorder?.AddTouch(_tick, Entity(defendingIndex, best), PassageAction.Tackle);
                break;

            case TickTackleOutcome.Foul:
                {
                    var foulerId = Participant(defendingIndex, best);
                    var result = TickTackleResolver.PunishFoul(_state, Other(holder), foulerId);

                    if (result.SecondYellow || result.StraightRed)
                    {
                        RemovePlayer(defendingIndex, foulerId);
                    }

                    _controllerSide = null;
                    ClearShot();
                    ClearReceiver();
                    _machine.Foul(_ball.GroundPoint, holder);
                    StampEvents();
                    break;
                }

            default:
                break;
        }
    }

    /// <summary>The defending goalkeeper's smother at an opponent's feet.</summary>
    private void TrySmother(int defendingIndex, int attackingIndex, int carrier)
    {
        if (carrier < 0 || _controllerSide == SideOfIndex(defendingIndex))
        {
            return;
        }

        var keeper = _players[defendingIndex][0];

        if (!TickGoalkeeperAI.CanSmother(keeper, _ball))
        {
            return;
        }

        var roll = _state.Random.NextBasisPoints();
        var outcome = TickGoalkeeperAI.Smother(
            _skills[defendingIndex][0], _skills[attackingIndex][carrier], roll);

        TickGoalkeeperAI.ApplySmother(
            outcome, ref _players[defendingIndex][0], ref _players[attackingIndex][carrier], 0, defendingIndex == Home, _ball);
        SyncBall();

        switch (outcome)
        {
            case TickSmotherOutcome.Claimed:
                _controllerSide = SideOfIndex(defendingIndex);
                _decisionIn = 1;
                ClearShot();
                _recorder?.AddTouch(_tick, Entity(defendingIndex, 0), PassageAction.Save);
                break;

            case TickSmotherOutcome.Spilled:
                _controllerSide = null;
                _lastTouchSide = SideOfIndex(defendingIndex);
                ClearShot();
                _recorder?.AddTouch(_tick, Entity(defendingIndex, 0), PassageAction.Save);
                break;

            case TickSmotherOutcome.Foul:
                {
                    var keeperId = Participant(defendingIndex, 0);

                    _state.Emit(SideOfIndex(defendingIndex), EngineEventType.Foul, keeperId);
                    _controllerSide = null;
                    ClearShot();
                    _machine.Foul(_ball.GroundPoint, SideOfIndex(attackingIndex));
                    StampEvents();
                    break;
                }

            default:
                _recorder?.AddTouch(_tick, Entity(defendingIndex, 0), PassageAction.Dive);
                break;
        }
    }

    /// <summary>Settles a shot at the goalkeeper's plane: assess, resolve, apply, and log what it was.</summary>
    private void ResolveSave()
    {
        var shootingSide = _shotSide ?? MatchSide.Home;
        var shooterIndex = SideIndex(shootingSide);
        var keeperIndex = 1 - shooterIndex;
        var shooterId = Participant(shooterIndex, _shotShooterLocal);
        var keeperId = Participant(keeperIndex, 0);
        var rules = _state.Rules;

        var assessment = TickShotStopper.Assess(_players[keeperIndex][0], _skills[keeperIndex][0], _shotForecast);
        var outcome = TickShotStopper.Resolve(assessment, _shotForecast.PlaneZ, _state.Random, out var deflectRoll);

        TickShotStopper.Apply(outcome, ref _players[keeperIndex][0], 0, keeperIndex == Home, _ball, deflectRoll);
        SyncBall();

        if (outcome != TickSaveOutcome.Beaten)
        {
            _recorder?.AddTouch(_tick, Entity(keeperIndex, 0), PassageAction.Save);

            if (_penaltyInFlight)
            {
                _state.Emit(shootingSide, EngineEventType.PenaltyMissed, shooterId);
            }
            else
            {
                _state.Emit(shootingSide, EngineEventType.ShotSaved, shooterId, keeperId);
                AdjustRating(shooterIndex, _shotShooterLocal, rules.LiveRatingShotBonusBasisPoints);
                AdjustRating(keeperIndex, 0, rules.LiveRatingSaveBonusBasisPoints);
            }

            _lastTouchSide = SideOfIndex(keeperIndex);
            _penaltyInFlight = false;
        }

        if (outcome == TickSaveOutcome.Caught)
        {
            _controllerSide = SideOfIndex(keeperIndex);
            _decisionIn = 1;
            _shieldTicks = 0;
        }
        else
        {
            _controllerSide = null;
        }

        _shotForecast = TickShotForecast.None;
        _shotSide = null;
        _shotShooterLocal = -1;
    }

    /// <summary>Starts tracking a shot in flight, so the goalkeeper can dive at it and the events can name the shooter.</summary>
    private void StartShot(MatchSide side, int shooterLocal, bool penalty)
    {
        var keeperIndex = SideIndex(side) == Home ? Away : Home;

        _shotForecast = TickShotStopper.Forecast(_ball, _scratch, keeperIndex == Home, _players[keeperIndex][0].X);
        _shotTicks = 0;
        _shotSide = side;
        _shotShooterLocal = shooterLocal;
        _penaltyInFlight = penalty;
    }

    /// <summary>Answers what the ball did at a boundary: a goal, a throw-in, a corner or goal kick, or the woodwork.</summary>
    private void HandleBoundary(TickBallBoundary boundary)
    {
        switch (boundary)
        {
            case TickBallBoundary.GoalHomeEnd:
                ScoreGoal(MatchSide.Away);
                break;

            case TickBallBoundary.GoalAwayEnd:
                ScoreGoal(MatchSide.Home);
                break;

            case TickBallBoundary.OutTouchline:
                _machine.TouchlineOut(_ball.GroundPoint, _lastTouchSide ?? MatchSide.Home);
                ClearLiveBall();
                break;

            case TickBallBoundary.OutGoalLine:
                HandleGoalLineOut();
                break;

            default:
                if (_shotSide is MatchSide shootingSide && _shotShooterLocal >= 0)
                {
                    _state.Emit(
                        shootingSide,
                        EngineEventType.Woodwork,
                        Participant(SideIndex(shootingSide), _shotShooterLocal));
                }

                break;
        }
    }

    /// <summary>Answers the ball crossing a goal line: a corner or a goal kick, and what the shot was.</summary>
    private void HandleGoalLineOut()
    {
        var crossing = _ball.GroundPoint;
        var homeGoalLine = _ball.UnitX <= 0;
        var lastTouch = _lastTouchSide ?? MatchSide.Home;

        if (_shotSide is MatchSide shootingSide && _shotForecast != TickShotForecast.None)
        {
            var shooterId = Participant(SideIndex(shootingSide), _shotShooterLocal);

            if (_penaltyInFlight)
            {
                _state.Emit(shootingSide, EngineEventType.PenaltyMissed, shooterId);
            }
            else
            {
                _state.Emit(shootingSide, EngineEventType.ShotOffTarget, shooterId);
                AdjustRating(SideIndex(shootingSide), _shotShooterLocal, -_state.Rules.LiveRatingShotMissPenaltyBasisPoints);
            }
        }
        else if (_penaltyInFlight && _shotSide is MatchSide penaltySide)
        {
            _state.Emit(penaltySide, EngineEventType.PenaltyMissed, Participant(SideIndex(penaltySide), _shotShooterLocal));
        }

        _machine.GoalLineOut(homeGoalLine, crossing, lastTouch);
        ClearLiveBall();
    }

    /// <summary>A goal: the scorer, the celebration, and the conceding side's kick-off that follows.</summary>
    private void ScoreGoal(MatchSide scoringSide)
    {
        var scoringIndex = SideIndex(scoringSide);
        var scorerLocal = _sideLastKickerLocal[scoringIndex];
        Guid? scorerId = scorerLocal >= 0 ? Participant(scoringIndex, scorerLocal) : null;
        var penalty = _penaltyInFlight;

        _state.Emit(
            scoringSide,
            penalty ? EngineEventType.PenaltyGoal : EngineEventType.Goal,
            scorerId);
        _state.AddGoalStoppage();

        if (scorerLocal >= 0)
        {
            AdjustRating(scoringIndex, scorerLocal, _state.Rules.LiveRatingGoalBonusBasisPoints);
        }

        AdjustRating(1 - scoringIndex, 0, -_state.Rules.LiveRatingGoalConcededPenaltyBasisPoints);

        _machine.Goal(scoringSide);
        ClearLiveBall();
        StampEvents();
    }

    /// <summary>The reception: the nearest player within reach of a loose ball takes it.</summary>
    private void TryReception()
    {
        if (_ball.Mode == TickBallMode.Controlled)
        {
            return;
        }

        var bestSide = -1;
        var bestLocal = -1;
        long bestDistance = long.MaxValue;

        for (var side = 0; side < 2; side++)
        {
            for (var index = 0; index < _count[side]; index++)
            {
                if (_entities[side][index] == _guardEntity)
                {
                    continue;
                }

                var player = _players[side][index];

                if (!_ball.IsReceivableFrom(player.X, player.Y))
                {
                    continue;
                }

                long dx = _ball.X - player.X;
                long dy = _ball.Y - player.Y;
                var distance = (dx * dx) + (dy * dy);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSide = side;
                    bestLocal = index;
                }
            }
        }

        if (bestSide < 0)
        {
            return;
        }

        var previousTouch = _lastTouchSide;
        var wasShot = _shotForecast != TickShotForecast.None;
        var shotSide = _shotSide;
        var shotShooter = _shotShooterLocal;

        _ball.Attach(bestLocal);
        _controllerSide = SideOfIndex(bestSide);
        _lastTouchSide = _controllerSide;
        _decisionIn = 1;
        _decision = default;
        _shieldTicks = 0;
        ClearGuard();
        ClearReceiver();

        if (wasShot && shotSide is MatchSide firingSide && firingSide != _controllerSide)
        {
            _state.Emit(
                firingSide,
                EngineEventType.ShotBlocked,
                Participant(SideIndex(firingSide), shotShooter),
                Participant(bestSide, bestLocal));
            _recorder?.AddTouch(_tick, Entity(bestSide, bestLocal), PassageAction.Interception);
        }
        else if (previousTouch is MatchSide touching && touching != _controllerSide)
        {
            _recorder?.AddTouch(_tick, Entity(bestSide, bestLocal), PassageAction.Interception);
        }
        else
        {
            _recorder?.AddTouch(_tick, Entity(bestSide, bestLocal), PassageAction.Receive);
        }

        if (_lastPasserSide == bestSide && _lastPasserLocal >= 0 && _lastKickWasPass)
        {
            var passerId = Participant(_lastPasserSide, _lastPasserLocal);

            _state.SideOf(SideOfIndex(_lastPasserSide)).PassesCompleted[passerId] =
                _state.SideOf(SideOfIndex(_lastPasserSide)).PassesCompleted.TryGetValue(passerId, out var completed)
                    ? completed + 1
                    : 1;

            _lastPasserSide = -1;
            _lastPasserLocal = -1;
        }

        _shotForecast = TickShotForecast.None;
        _shotSide = null;
        _shotShooterLocal = -1;
        _penaltyInFlight = false;
    }

    /// <summary>Clears the self-pass guard once the ball has left the kicker or come to rest.</summary>
    private void CheckSelfPassGuard()
    {
        if (_guardLocal < 0 || _ball.Mode == TickBallMode.Controlled)
        {
            return;
        }

        if (_guardSide < 0 || _guardSide >= 2 || _guardLocal >= _count[_guardSide])
        {
            ClearGuard();

            return;
        }

        if (_ball.GroundSpeed == 0)
        {
            ClearGuard();

            return;
        }

        var kicker = _players[_guardSide][_guardLocal];
        long dx = _ball.X - kicker.X;
        long dy = _ball.Y - kicker.Y;
        var guard = (long)TickSpatialUnits.CentimetresToFixed(300);

        if ((dx * dx) + (dy * dy) > guard * guard)
        {
            ClearGuard();
        }
    }

    /// <summary>Books a kick: who touched the ball last, who kicked it, and that the kicker may not take it straight back.</summary>
    private void BookKick(int side, int local, bool isPass)
    {
        _controllerSide = null;
        _lastTouchSide = SideOfIndex(side);
        _sideLastKickerLocal[side] = local;
        _lastKickWasPass = isPass;
        _guardSide = side;
        _guardLocal = local;
        _guardEntity = Entity(side, local);
        _decisionIn = 1;
    }

    /// <summary>Removes a sent-off player from the loop's eleven, keeping every other player's identity.</summary>
    private void RemovePlayer(int side, Guid participantId)
    {
        var index = -1;

        for (var local = 1; local < _count[side]; local++)
        {
            if (_slots[side][local].Participant.ParticipantId == participantId)
            {
                index = local;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        var last = _count[side] - 1;

        for (var local = index; local < last; local++)
        {
            _players[side][local] = _players[side][local + 1];
            _profiles[side][local] = _profiles[side][local + 1];
            _skills[side][local] = _skills[side][local + 1];
            _specs[side][local] = _specs[side][local + 1];
            _slots[side][local] = _slots[side][local + 1];
            _entities[side][local] = _entities[side][local + 1];
        }

        _count[side] = last;

        if (_guardSide == side)
        {
            if (_guardLocal == index)
            {
                ClearGuard();
            }
            else if (_guardLocal > index)
            {
                _guardLocal--;
                _guardEntity = Entity(side, _guardLocal);
            }
        }

        if (_receiverSide == side)
        {
            if (_receiverLocal == index)
            {
                ClearReceiver();
            }
            else if (_receiverLocal > index)
            {
                _receiverLocal--;
            }
        }

        if (_lastPasserSide == side && _lastPasserLocal >= 0)
        {
            if (_lastPasserLocal == index)
            {
                _lastPasserSide = -1;
                _lastPasserLocal = -1;
            }
            else if (_lastPasserLocal > index)
            {
                _lastPasserLocal--;
            }
        }

        if (_sideLastKickerLocal[side] == index)
        {
            _sideLastKickerLocal[side] = -1;
        }
        else if (_sideLastKickerLocal[side] > index)
        {
            _sideLastKickerLocal[side]--;
        }

        if (_controllerSide == SideOfIndex(side))
        {
            if (_ball.ControllerIndex == index)
            {
                _ball.Release();
                _controllerSide = null;
            }
            else if (_ball.ControllerIndex > index)
            {
                _ball.Attach(_ball.ControllerIndex - 1);
            }
        }

        _supporters[side] = 0;
        _runners[side] = 0;
        _presser[side] = -1;
    }

    /// <summary>Keeps the match state's ball where the tick ball is, so events are stamped where they happened.</summary>
    private void SyncBall() => _state.MoveBall(_ball.GroundPoint, _ball.UnitZ);

    /// <summary>Clears everything that belongs to a ball in flight.</summary>
    private void ClearShot()
    {
        _shotForecast = TickShotForecast.None;
        _shotSide = null;
        _shotShooterLocal = -1;
        _penaltyInFlight = false;
    }

    /// <summary>Clears the live ball state as play stops for a restart.</summary>
    private void ClearLiveBall()
    {
        _controllerSide = null;
        _decision = default;
        _shieldTicks = 0;
        ClearShot();
        ClearGuard();
        ClearReceiver();
    }

    /// <summary>Clears the self-pass guard.</summary>
    private void ClearGuard()
    {
        _guardSide = -1;
        _guardLocal = -1;
        _guardEntity = -1;
    }

    /// <summary>Clears the man a pass in flight was meant for.</summary>
    private void ClearReceiver()
    {
        _receiverSide = -1;
        _receiverLocal = -1;
    }

    /// <summary>Records the frame: every entity's position at the end of the tick.</summary>
    private void RecordFrame()
    {
        if (_recorder is null)
        {
            return;
        }

        for (var side = 0; side < 2; side++)
        {
            for (var index = 0; index < _count[side]; index++)
            {
                var entity = _entities[side][index];

                _entityX[entity] = TickSpatialUnits.ToUnits(_players[side][index].X);
                _entityY[entity] = TickSpatialUnits.ToUnits(_players[side][index].Y);
                _entityZ[entity] = 0;
            }
        }

        _entityX[TickMatchRecording.BallEntityIndex] = _ball.UnitX;
        _entityY[TickMatchRecording.BallEntityIndex] = _ball.UnitY;
        _entityZ[TickMatchRecording.BallEntityIndex] = _ball.UnitZ;

        _recorder.AddFrame(
            _entityX,
            _entityY,
            _entityZ,
            _state.ClockSeconds,
            _state.InFirstHalf ? 1 : 2);
    }

    /// <summary>Stamps any event emitted since the last stamp with the current tick.</summary>
    private void StampEvents()
    {
        if (_recorder is null)
        {
            return;
        }

        while (_stampedEvents < _state.Events.Count)
        {
            _recorder.AddEvent(_tick, _state.Events[_stampedEvents].Sequence);
            _stampedEvents++;
        }
    }

    /// <summary>Gets the index of the nearest defender pressing, or -1.</summary>
    private static int FindPresser(ReadOnlySpan<TickDefensiveOrder> orders)
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

    /// <summary>Gets a side's instructions.</summary>
    private MatchInstructionsV1 Instructions(int side) => _state.SideOf(SideOfIndex(side)).Instructions;

    /// <summary>Adjusts one player's live match rating.</summary>
    private void AdjustRating(int side, int local, int delta)
    {
        if (local < 0 || local >= _count[side])
        {
            return;
        }

        _state.SideOf(SideOfIndex(side)).AdjustLiveRating(_slots[side][local].Participant.ParticipantId, delta);
    }

    /// <summary>Gets the entity index of a local player.</summary>
    private int Entity(int side, int local) => _entities[side][local];

    /// <summary>Gets a local player's participant identity.</summary>
    private Guid Participant(int side, int local) =>
        local >= 0 && local < _count[side] ? _slots[side][local].Participant.ParticipantId : Guid.Empty;

    private static int SideIndex(MatchSide side) => side == MatchSide.Home ? Home : Away;

    private static MatchSide SideOfIndex(int side) => side == Home ? MatchSide.Home : MatchSide.Away;

    private static MatchSide Other(MatchSide side) => side == MatchSide.Home ? MatchSide.Away : MatchSide.Home;
}
