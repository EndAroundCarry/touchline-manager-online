using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>The part of the loop that stops and restarts play: shots and their results, goals, fouls, offsides and the set pieces.</summary>
internal sealed partial class TickMatchLoop
{
    /// <summary>The ticks a shot may stay live without anybody reaching it before it is given up on (8 s).</summary>
    private const int ShotLifeTicks = 80;

    private bool _shotLive;
    private bool _shotResolved;
    private TickRef _shooter = TickRef.None;
    private TickShotForecast _forecast = TickShotForecast.None;
    private int _shotTicks;
    private int _shotQuality;
    private ShotZone _shotZone;
    private bool _shotPenalty;
    private SpatialPoint _shotOrigin;

    private TickSetPiecePlan _plan = new(0, -1);

    // ---- Shots ----------------------------------------------------------------------------------------------------------------------

    private void StartShot(int side, int seat, in TickCarrierDecision decision, SpatialPoint origin, bool penalty, bool freeKick)
    {
        var team = _teams[side];
        var opponents = _teams[1 - side];
        var viewX = team.IsHome ? origin.X : SpatialPitch.PitchLength - origin.X;
        var viewY = team.IsHome ? origin.Y : SpatialPitch.PitchWidth - origin.Y;

        _shotLive = true;
        _shotResolved = false;
        _shooter = new TickRef(side, team.SlotNumber[seat]);
        _shotTicks = 0;
        _shotPenalty = penalty;
        _shotOrigin = origin;
        _shotQuality = penalty
            ? PenaltyQuality
            : Math.Clamp(TickBallCarrierBrain.ExpectedGoals(viewX, viewY, 0, team.Skills[seat]), 100, 9_500);
        _shotZone = ZoneOf(viewY);

        var keeperX = opponents.Count > 0 && opponents.IsKeeper(0)
            ? opponents.Body[0].X
            : (opponents.IsHome ? 0 : TickSpatialUnits.PitchLengthFixed);

        _forecast = TickShotStopper.Forecast(_ball, _scratch, opponents.IsHome, keeperX);

        if (freeKick)
        {
            Emit(
                team.Side,
                EngineEventType.FreeKickShot,
                team.Id[seat],
                opponents.Count > 0 && opponents.IsKeeper(0) ? opponents.Id[0] : null,
                _shotZone,
                _shotQuality,
                origin);
        }
    }

    /// <summary>The goal probability a penalty is recorded with, in basis points.</summary>
    private const int PenaltyQuality = 7_600;

    private static ShotZone ZoneOf(int viewY)
    {
        var offset = viewY - SpatialPitch.GoalYCenter;
        var across = Math.Abs(offset);

        if (across <= 500)
        {
            return ShotZone.Central;
        }

        if (across <= 1_500)
        {
            return offset < 0 ? ShotZone.InsideLeft : ShotZone.InsideRight;
        }

        return offset < 0 ? ShotZone.WideLeft : ShotZone.WideRight;
    }

    /// <summary>The ball reaches the goalkeeper's plane: he gets to it or he does not.</summary>
    private void ResolveSave()
    {
        var keepers = _teams[1 - _shooter.Side];
        var assessment = TickShotStopper.Assess(keepers.Body[0], keepers.Skills[0], _forecast);
        var outcome = TickShotStopper.Resolve(assessment, _forecast.PlaneZ, _random, out var deflect);

        TickShotStopper.Apply(outcome, ref keepers.Body[0], 0, keepers.IsHome, _ball, deflect);
        _shotResolved = true;

        if (outcome == TickSaveOutcome.Beaten)
        {
            return;
        }

        var keeperRef = new TickRef(1 - _shooter.Side, keepers.SlotNumber[0]);
        var shooters = _teams[_shooter.Side];
        var shooterSeat = shooters.SeatOfSlot(_shooter.Slot);

        EmitShotResult(EngineEventType.ShotSaved);

        if (shooterSeat >= 0)
        {
            Rate(shooters, shooters.Id[shooterSeat], _rules.LiveRatingShotBonusBasisPoints);
        }

        Rate(keepers, keepers.Id[0], _rules.LiveRatingSaveBonusBasisPoints);
        Tag(keeperRef, PassageAction.Save);
        Tag(keeperRef, PassageAction.Dive);
        _shotLive = false;
        Touch(keeperRef);

        if (outcome == TickSaveOutcome.Caught)
        {
            _controllerSide = 1 - _shooter.Side;
            TurnOver();
        }
        else
        {
            _controllerSide = -1;
            PredictRest();
        }
    }

    /// <summary>Puts a shot's result on the event log: a penalty's results are its own.</summary>
    private void EmitShotResult(EngineEventType result)
    {
        var shooters = _teams[_shooter.Side];
        var opponents = _teams[1 - _shooter.Side];
        var seat = shooters.SeatOfSlot(_shooter.Slot);
        var type = _shotPenalty
            ? (result == EngineEventType.Goal ? EngineEventType.PenaltyGoal : EngineEventType.PenaltyMissed)
            : result;

        Emit(
            shooters.Side,
            type,
            seat >= 0 ? shooters.Id[seat] : null,
            opponents.Count > 0 && opponents.IsKeeper(0) ? opponents.Id[0] : null,
            _shotZone,
            _shotQuality,
            _shotOrigin);
    }

    /// <summary>A shot nobody has met for a long time has gone nowhere: it is recorded as off target.</summary>
    private void ExpireShot()
    {
        if (!_shotLive)
        {
            return;
        }

        if (_shotTicks > ShotLifeTicks || (_ball.Mode == TickBallMode.Loose && _ball.GroundSpeed == 0))
        {
            EmitShotResult(EngineEventType.ShotOffTarget);
            _shotLive = false;
        }
    }

    private void OnWoodwork()
    {
        if (_shotLive)
        {
            EmitShotResult(EngineEventType.Woodwork);
            _shotLive = false;
            _shotResolved = true;
        }

        _controllerSide = -1;
        PredictRest();
    }

    // ---- Goals ----------------------------------------------------------------------------------------------------------------------------

    private void ScoreGoal(TickBallBoundary boundary)
    {
        var homeScored = boundary == TickBallBoundary.GoalAwayEnd;
        var attackIndex = homeScored ? 0 : 1;
        var attackers = _teams[attackIndex];
        var defenders = _teams[1 - attackIndex];

        // A ball nobody shot that runs over the line off the attackers' last touch is a pass or a cross that went too far, not a goal:
        // the goalkeeper picks it up.
        if (!_shotLive && !_lastTouch.IsNone && _lastTouch.Side == attackIndex)
        {
            OutOnGoalLine();

            return;
        }

        var scorerRef = _shotLive && _shooter.Side == attackIndex ? _shooter : _lastTouchBySide[attackIndex];
        var scorerSeat = scorerRef.IsNone ? -1 : attackers.SeatOfSlot(scorerRef.Slot);

        if (scorerSeat < 0)
        {
            scorerSeat = NearestOutfield(attackers);
        }

        scorerRef = new TickRef(attackIndex, attackers.SlotNumber[scorerSeat]);

        var scorerId = attackers.Id[scorerSeat];
        Guid? keeperId = defenders.Count > 0 && defenders.IsKeeper(0) ? defenders.Id[0] : null;
        var penalty = _shotLive && _shotPenalty;
        var live = _shotLive;

        var goal = Emit(
            attackers.Side,
            penalty ? EngineEventType.PenaltyGoal : EngineEventType.Goal,
            scorerId,
            keeperId,
            live ? _shotZone : ShotZone.Central,
            live ? _shotQuality : 500,
            live ? _shotOrigin : null);

        _ = goal;

        var runtime = attackers.Runtime;

        runtime.Goals[scorerId] = runtime.Goals.GetValueOrDefault(scorerId) + 1;
        Rate(attackers, scorerId, _rules.LiveRatingGoalBonusBasisPoints);

        if (keeperId is Guid beaten)
        {
            Rate(defenders, beaten, -_rules.LiveRatingGoalConcededPenaltyBasisPoints);
        }

        if (!penalty
            && _assistReceiver == scorerRef
            && !_assistPasser.IsNone
            && _assistPasser != scorerRef
            && _tick - _assistTick <= AssistWindowTicks)
        {
            var passerSeat = attackers.SeatOfSlot(_assistPasser.Slot);

            if (passerSeat >= 0)
            {
                var passerId = attackers.Id[passerSeat];

                runtime.Assists[passerId] = runtime.Assists.GetValueOrDefault(passerId) + 1;
                Rate(attackers, passerId, _rules.LiveRatingAssistBonusBasisPoints);
            }
        }

        _state.AddGoalStoppage();
        runtime.ShiftMorale(_rules.MoraleGainPerGoalBasisPoints, _rules.MaxMoraleDriftBasisPoints, _rules);
        defenders.Runtime.ShiftMorale(-_rules.MoraleLossPerConcededGoalBasisPoints, _rules.MaxMoraleDriftBasisPoints, _rules);

        if (!live)
        {
            // A goal off a deflection or a goalkeeper's mistake has no shot of its own: the film still shows the man it is given to striking it.
            Tag(scorerRef, PassageAction.Shot);
        }

        Tag(scorerRef, PassageAction.Celebrate);
        ResetPlay();
        _celebrationScorerIsHome = homeScored;
        _machine.BeginGoalCelebration(homeScored);
        _recording?.AddStoppage(TickPlayState.GoalCelebration, TickRestartKind.KickOff, !homeScored);
    }

    /// <summary>The ticks within which a pass that reached the scorer is the assist (10 s).</summary>
    private const int AssistWindowTicks = 100;

    private int NearestOutfield(TickTeam team)
    {
        var best = Math.Min(1, team.Count - 1);
        var bestDistance = long.MaxValue;

        for (var seat = 0; seat < team.Count; seat++)
        {
            if (team.IsKeeper(seat))
            {
                continue;
            }

            long dx = team.Body[seat].X - _ball.X;
            long dy = team.Body[seat].Y - _ball.Y;
            var distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = seat;
            }
        }

        return Math.Max(0, best);
    }

    // ---- The ball leaves the pitch --------------------------------------------------------------------------------------------------

    private void OutOnTouchline()
    {
        if (_shotLive)
        {
            EmitShotResult(EngineEventType.ShotOffTarget);
            _shotLive = false;
        }

        var lastSide = _lastTouch.IsNone ? 0 : _lastTouch.Side;

        BeginRestart(TickRestart.ThrowIn(homeTakes: lastSide != 0, _ball.UnitX, _ball.UnitY));
    }

    private void OutOnGoalLine()
    {
        var homeEnd = _ball.UnitX < SpatialPitch.PitchLength / 2;
        var defenceIndex = homeEnd ? 0 : 1;
        var attackIndex = 1 - defenceIndex;
        var lastSide = _lastTouch.IsNone ? attackIndex : _lastTouch.Side;
        var ballY = _ball.UnitY;

        if (_shotLive)
        {
            EmitShotResult(EngineEventType.ShotOffTarget);
            _shotLive = false;
        }

        if (lastSide == defenceIndex)
        {
            Emit(_teams[attackIndex].Side, EngineEventType.Corner);
            BeginRestart(TickRestart.Corner(homeTakes: attackIndex == 0, ballY));

            return;
        }

        BeginRestart(TickRestart.GoalKick(homeTakes: defenceIndex == 0, ballY));
    }

    // ---- Fouls and offsides ----------------------------------------------------------------------------------------------------------

    /// <summary>A foul: the card is rolled by the possession engine's discipline rules, then the attacking side has its free kick or penalty.</summary>
    private void FoulBy(int defenceIndex, Guid foulerId, int attackIndex, SpatialPoint spot)
    {
        TickTackleResolver.PunishFoul(_state, _teams[defenceIndex].Side, foulerId);

        var attackers = _teams[attackIndex];
        var inBox = attackers.IsHome ? SpatialPitch.IsInAwayPenaltyBox(spot) : SpatialPitch.IsInHomePenaltyBox(spot);

        if (inBox)
        {
            BeginRestart(TickRestart.Penalty(attackers.IsHome));
            Emit(attackers.Side, EngineEventType.PenaltyAwarded, TakerId(attackers), zone: ShotZone.Central);

            return;
        }

        BeginRestart(TickRestart.FreeKick(attackers.IsHome, spot.X, spot.Y));

        var viewX = attackers.IsHome ? spot.X : SpatialPitch.PitchLength - spot.X;
        var viewY = attackers.IsHome ? spot.Y : SpatialPitch.PitchWidth - spot.Y;

        if (TickSetPieces.IsShootingRange(viewX, viewY))
        {
            Emit(attackers.Side, EngineEventType.FreeKickWon, TakerId(attackers));
        }
    }

    private Guid? TakerId(TickTeam team) =>
        _plan.TakerIndex >= 0 && _plan.TakerIndex < team.Count ? team.Id[_plan.TakerIndex] : null;

    /// <summary>The man a pass was meant for was offside when it was played: the other side has a free kick where he stood.</summary>
    private void CallOffside(int side, int seat)
    {
        var team = _teams[side];
        var spot = new SpatialPoint(TickSpatialUnits.ToUnits(team.Body[seat].X), TickSpatialUnits.ToUnits(team.Body[seat].Y));
        var passerTeam = _teams[_passer.Side];
        var passerSeat = passerTeam.SeatOfSlot(_passer.Slot);

        Emit(team.Side, EngineEventType.Offside, team.Id[seat], passerSeat >= 0 ? passerTeam.Id[passerSeat] : null);
        BeginRestart(TickRestart.FreeKick(homeTakes: !team.IsHome, spot.X, spot.Y));
    }

    // ---- Restarts -------------------------------------------------------------------------------------------------------------------------

    /// <summary>Forgets the flight of the ball and the carrier's decision: play has stopped.</summary>
    private void ResetPlay()
    {
        _shotLive = false;
        _shotResolved = false;
        _forecast = TickShotForecast.None;
        _passPending = false;
        _offsidePending = false;
        _controllerSide = -1;
        _kicker = TickRef.None;
        _decisionOwner = TickRef.None;
        _decided = false;
        _assistPasser = TickRef.None;
        _assistReceiver = TickRef.None;
        _restX = _ball.UnitX;
        _restY = _ball.UnitY;

        foreach (var team in _teams)
        {
            team.SupporterMask = 0;
            team.RunnerMask = 0;
            team.PreviousPresser = -1;
            team.KeeperRushing = false;
        }
    }

    /// <summary>Stops play for a restart: the planners are given their stoppage, the ball is put on its spot and the players take up their places.</summary>
    private void BeginRestart(in TickRestart restart)
    {
        Stoppage();

        var jump = DistanceUnits(_ball.UnitX, _ball.UnitY, restart.SpotX, restart.SpotY);

        _machine.Begin(restart);
        _ball.PlaceAt(restart.SpotX, restart.SpotY);
        ResetPlay();

        if (jump > BallPlacedUnits)
        {
            _pendingFlags |= TickFrameFlags.BallPlaced;
        }

        _recording?.AddStoppage(TickMatchStateMachine.PendingStateOf(restart.Kind), restart.Kind, restart.TakerIsHome);
        LayOut(restart);
    }

    /// <summary>A ball put down more than this far from where it was (4.7 m) is a placement, not a movement: a strike never moves it so far in a tick.</summary>
    private const int BallPlacedUnits = 450;

    private static int DistanceUnits(int ax, int ay, int bx, int by)
    {
        long dx = ax - bx;
        long dy = ay - by;

        return (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    private static TickSetPieceSituation SetPieceSituation(in TickRestart restart, TickTeam taker, TickTeam other) =>
        new()
        {
            Restart = restart,
            Takers = taker.Body.AsSpan(0, taker.Count),
            TakerSpecs = taker.Spec.AsSpan(0, taker.Count),
            TakerSkills = taker.Skills.AsSpan(0, taker.Count),
            TakerStyle = taker.Style,
            TakerPassing = taker.Runtime.Instructions.Passing,
            Others = other.Body.AsSpan(0, other.Count),
            OtherSpecs = other.Spec.AsSpan(0, other.Count),
            OtherSkills = other.Skills.AsSpan(0, other.Count),
            OtherStyle = other.Style,
        };

    /// <summary>Works out where the 22 stand for a restart and who takes it; the targets go into the sides' anchors.</summary>
    private void LayOut(in TickRestart restart)
    {
        var taker = _teams[restart.TakerIsHome ? 0 : 1];
        var other = _teams[restart.TakerIsHome ? 1 : 0];

        Array.Clear(taker.Pace);
        Array.Clear(other.Pace);

        if (taker.Count == 0 || other.Count == 0)
        {
            _plan = new TickSetPiecePlan(0, -1);

            return;
        }

        _plan = TickSetPieces.Place(
            SetPieceSituation(restart, taker, other),
            taker.Anchor.AsSpan(0, taker.Count),
            other.Anchor.AsSpan(0, other.Count));
    }

    private void StepDeadBall()
    {
        if (_machine.State == TickPlayState.GoalCelebration)
        {
            // Everybody walks to the places the kick-off will find them in.
            LayOut(TickRestart.KickOff(homeTakes: !_celebrationScorerIsHome));

            if (_machine.Step(takerReady: false) == TickRestartSignal.SetUp)
            {
                SetUpKickOffAfterGoal();
            }

            MoveTeams();

            return;
        }

        var restart = _machine.Restart;
        var taker = _teams[restart.TakerIsHome ? 0 : 1];

        LayOut(restart);

        var ready = _plan.TakerIndex >= 0
            && _plan.TakerIndex < taker.Count
            && TickSetPieces.IsTakerReady(taker.Body[_plan.TakerIndex], _ball.X, _ball.Y);

        var signal = _machine.Step(ready);

        MoveTeams();

        if (signal == TickRestartSignal.Execute)
        {
            TakeRestart(restart);
        }
    }

    private void SetUpKickOffAfterGoal()
    {
        Stoppage();

        var restart = _machine.Restart;

        _ball.PlaceAt(restart.SpotX, restart.SpotY);
        ResetPlay();
        _pendingFlags |= TickFrameFlags.BallPlaced;
        _recording?.AddStoppage(TickPlayState.KickOffPending, TickRestartKind.KickOff, restart.TakerIsHome);
        LayOut(restart);
    }

    /// <summary>The whistle: the taker hits the restart.</summary>
    private void TakeRestart(in TickRestart restart)
    {
        var sideIndex = restart.TakerIsHome ? 0 : 1;
        var taker = _teams[sideIndex];
        var other = _teams[1 - sideIndex];

        if (_plan.TakerIndex < 0 || _plan.TakerIndex >= taker.Count)
        {
            return;
        }

        var decision = TickSetPieces.Decide(SetPieceSituation(restart, taker, other), _plan);
        var origin = new SpatialPoint(_ball.UnitX, _ball.UnitY);

        if (!TickSetPieces.Execute(restart.Kind, decision, taker.Skills[_plan.TakerIndex], _ball, _random))
        {
            return;
        }

        AfterKick(sideIndex, _plan.TakerIndex, decision, origin, restart.Kind, fromRestart: true);
    }
}
