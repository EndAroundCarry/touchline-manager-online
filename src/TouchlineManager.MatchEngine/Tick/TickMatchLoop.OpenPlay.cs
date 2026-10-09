using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>The part of the loop that plays the ball while it is in play: who has it, what he does with it, who reaches it next.</summary>
internal sealed partial class TickMatchLoop
{
    /// <summary>The farthest a player runs to a loose ball, in pitch units (60 m).</summary>
    public const int ChaseRange = 6_000;

    /// <summary>A ball faster than this (14 m/s) is checked along its whole path for a player, not only where it ends the tick.</summary>
    private static readonly int FastBall = TickSpatialUnits.SpeedToFixedPerTick(1_400);

    private int _controllerSide = -1;
    private TickRef _lastTouch = TickRef.None;
    private readonly TickRef[] _lastTouchBySide = [TickRef.None, TickRef.None];
    private TickRef _kicker = TickRef.None;
    private int _kickTick;

    private bool _passPending;

    /// <summary>The opponents who have already had their chance to cut out the pass in flight: one bit per side and seat.</summary>
    private ulong _passTried;
    private TickRef _passer = TickRef.None;
    private TickRef _intended = TickRef.None;
    private bool _offsidePending;

    private TickRef _assistPasser = TickRef.None;
    private TickRef _assistReceiver = TickRef.None;
    private int _assistTick;

    private bool _decided;
    private TickCarrierDecision _decision;
    private int _decisionTick;
    private TickRef _decisionOwner = TickRef.None;
    private int _heldTicks;
    private int _shieldTicks;

    private int _restX = SpatialPitch.PitchLength / 2;
    private int _restY = SpatialPitch.GoalYCenter;

    private void StepOpenPlay()
    {
        var controlled = _ball.Mode == TickBallMode.Controlled;
        var carrierSide = controlled ? _controllerSide : -1;
        var carrierSeat = controlled ? _ball.ControllerIndex : -1;
        var possession = controlled ? carrierSide : (_lastTouch.IsNone ? 0 : _lastTouch.Side);

        for (var index = 0; index < _teams.Length; index++)
        {
            var team = _teams[index];

            TickTacticalGeometry.ResolveTeam(
                team.Spec.AsSpan(0, team.Count),
                team.Style,
                team.IsHome,
                index == possession,
                _ball.UnitX,
                _ball.UnitY,
                team.Anchor.AsSpan(0, team.Count));
        }

        AssignAttack(_teams[possession], _teams[1 - possession], carrierSide == possession ? carrierSeat : -1);
        AssignDefence(_teams[1 - possession], _teams[possession], controlled ? carrierSeat : -1);
        AssignChasers(controlled);
        AssignKeepers(controlled, carrierSide);

        if (controlled)
        {
            StepCarrier(carrierSide, carrierSeat);
        }

        MoveTeams();

        if (_ball.Mode == TickBallMode.Controlled)
        {
            var body = _teams[_controllerSide].Body[_ball.ControllerIndex];

            _ball.Carry(body.X, body.Y, body.Heading, body.Speed);
            ResolveChallenges();

            if (_machine.IsDeadBall)
            {
                return;
            }
        }

        if (_ball.Mode != TickBallMode.Controlled)
        {
            StepBall();
        }
    }

    // ---- Orders ----------------------------------------------------------------------------------------------------------------------

    private static void AssignAttack(TickTeam attackers, TickTeam defenders, int carrier)
    {
        var count = attackers.Count;

        if (count == 0)
        {
            return;
        }

        var situation = new TickAttackingSituation
        {
            IsHome = attackers.IsHome,
            Mentality = attackers.Runtime.Instructions.Mentality,
            Passing = attackers.Runtime.Instructions.Passing,
            Attackers = attackers.Body.AsSpan(0, count),
            Specs = attackers.Spec.AsSpan(0, count),
            Anchors = attackers.Anchor.AsSpan(0, count),
            Skills = attackers.Skills.AsSpan(0, count),
            Defenders = defenders.Body.AsSpan(0, defenders.Count),
            CarrierIndex = carrier,
            PreviousSupporters = attackers.SupporterMask,
            PreviousRunners = attackers.RunnerMask,
        };

        var orders = attackers.AttackOrders.AsSpan(0, count);

        TickOffBallSupport.Assign(situation, orders);
        attackers.SupporterMask = TickOffBallSupport.SupporterMask(orders);
        attackers.RunnerMask = TickOffBallSupport.RunnerMask(orders);
        TickOffBallSupport.ToSteering(orders, attackers.Anchor.AsSpan(0, count), attackers.Pace.AsSpan(0, count));
    }

    private void AssignDefence(TickTeam defenders, TickTeam attackers, int carrier)
    {
        var count = defenders.Count;

        if (count == 0)
        {
            return;
        }

        var situation = new TickDefensiveSituation
        {
            IsHome = defenders.IsHome,
            Pressing = defenders.Runtime.Instructions.Pressing,
            Defenders = defenders.Body.AsSpan(0, count),
            Specs = defenders.Spec.AsSpan(0, count),
            Anchors = defenders.Anchor.AsSpan(0, count),
            Skills = defenders.Skills.AsSpan(0, count),
            Attackers = attackers.Body.AsSpan(0, attackers.Count),
            BallX = _ball.UnitX,
            BallY = _ball.UnitY,
            CarrierIndex = carrier,
            PreviousPresser = defenders.PreviousPresser,
        };

        var orders = defenders.DefenceOrders.AsSpan(0, count);

        TickDefensiveAI.Assign(situation, orders);

        defenders.PreviousPresser = -1;

        for (var seat = 0; seat < count; seat++)
        {
            if (orders[seat].Role == TickDefensiveRole.Presser)
            {
                defenders.PreviousPresser = seat;

                break;
            }
        }

        TickDefensiveAI.ToSteering(orders, defenders.Anchor.AsSpan(0, count), defenders.Pace.AsSpan(0, count));
    }

    /// <summary>
    /// A ball nobody has: the man it was played to runs to where it will arrive, and the nearest man of each side goes for it,
    /// so a pass is met and a loose ball is contested rather than left to roll.
    /// </summary>
    private void AssignChasers(bool controlled)
    {
        if (controlled || _shotLive)
        {
            return;
        }

        var restX = TickSpatialUnits.ToFixed(_restX);
        var restY = TickSpatialUnits.ToFixed(_restY);
        var range = (long)TickSpatialUnits.ToFixed(ChaseRange);

        for (var index = 0; index < _teams.Length; index++)
        {
            var team = _teams[index];
            var best = -1;
            var bestDistance = range * range;

            for (var seat = 0; seat < team.Count; seat++)
            {
                if (team.IsKeeper(seat) || team.Body[seat].Lockout > 0)
                {
                    continue;
                }

                if (_kicker.Side == index && _kicker.Slot == team.SlotNumber[seat] && _tick - _kickTick < KickCooldownTicks * 3)
                {
                    continue;
                }

                long dx = team.Body[seat].X - restX;
                long dy = team.Body[seat].Y - restY;
                var distance = (dx * dx) + (dy * dy);

                if (_passPending && _intended.Side == index && _intended.Slot == team.SlotNumber[seat])
                {
                    distance = distance * 64 / 100;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = seat;
                }
            }

            if (best >= 0)
            {
                team.Anchor[best] = new SpatialPoint(_restX, _restY);
                team.Pace[best] = TickOffBallSupport.RunPaceBasisPoints;
            }
        }
    }

    private void AssignKeepers(bool controlled, int carrierSide)
    {
        for (var index = 0; index < _teams.Length; index++)
        {
            var team = _teams[index];
            var other = _teams[1 - index];

            if (team.Count == 0 || !team.IsKeeper(0))
            {
                continue;
            }

            var keeper = team.Body[0];

            if (_shotLive && _shooter.Side != index && _forecast.OnTarget && !_shotResolved)
            {
                team.Intent[0] = TickShotStopper.DiveIntent(keeper, team.Skills[0], _forecast, _shotTicks);
                team.HasIntent[0] = true;

                continue;
            }

            if (controlled && carrierSide == index && _ball.ControllerIndex == 0)
            {
                team.Intent[0] = new TickMoveIntent(TickSpatialUnits.ToUnits(keeper.X), TickSpatialUnits.ToUnits(keeper.Y), 0, Arrive: true);
                team.HasIntent[0] = true;

                continue;
            }

            var held = !controlled
                ? TickKeeperPossession.Loose
                : (carrierSide == index ? TickKeeperPossession.HeldByOwn : TickKeeperPossession.HeldByOpponent);

            var order = TickGoalkeeperAI.Decide(new TickKeeperSituation
            {
                IsHome = team.IsHome,
                Keeper = keeper,
                Skills = team.Skills[0],
                Anchor = team.Anchor[0],
                Ball = _ball,
                Possession = held,
                Teammates = team.Body.AsSpan(0, team.Count),
                Opponents = other.Body.AsSpan(0, other.Count),
                WasRushing = team.KeeperRushing,
            });

            team.KeeperRushing = order.Mode == TickKeeperMode.Rush;
            team.Intent[0] = order.Intent;
            team.HasIntent[0] = true;
        }
    }

    // ---- The man with the ball ------------------------------------------------------------------------------------------------------

    private void StepCarrier(int side, int seat)
    {
        var team = _teams[side];
        var me = new TickRef(side, team.SlotNumber[seat]);

        if (_decisionOwner != me)
        {
            _decisionOwner = me;
            _decided = false;
            _heldTicks = 0;
            _shieldTicks = 0;
        }

        _heldTicks++;

        var keeper = team.IsKeeper(seat);
        var skills = team.Skills[seat];
        var instructions = team.Runtime.Instructions;
        var interval = TickBallCarrierBrain.DecisionInterval(skills, instructions.Tempo);

        if (keeper && _heldTicks < KeeperHoldTicks)
        {
            return;
        }

        if (!_decided || _tick - _decisionTick >= interval)
        {
            var opponents = _teams[1 - side];
            var count = team.Count;

            _decision = TickBallCarrierBrain.Decide(new TickCarrierSituation
            {
                IsHome = team.IsHome,
                Mentality = instructions.Mentality,
                Tempo = instructions.Tempo,
                Passing = instructions.Passing,
                Focus = instructions.PassFocus,
                Attackers = team.Body.AsSpan(0, count),
                Specs = team.Spec.AsSpan(0, count),
                Skills = team.Skills.AsSpan(0, count),
                Defenders = opponents.Body.AsSpan(0, opponents.Count),
                CarrierIndex = seat,
                Orders = team.AttackOrders.AsSpan(0, count),
                ShieldTicks = _shieldTicks,
            });

            _decided = true;
            _decisionTick = _tick;
            _shieldTicks = _decision.Action == TickCarrierAction.Shield ? _shieldTicks + interval : 0;
        }

        switch (_decision.Action)
        {
            case TickCarrierAction.Dribble:
                team.Anchor[seat] = _decision.Target;
                team.Pace[seat] = _decision.PaceBasisPoints;
                break;

            case TickCarrierAction.Shield:
                team.Anchor[seat] = new SpatialPoint(TickSpatialUnits.ToUnits(team.Body[seat].X), TickSpatialUnits.ToUnits(team.Body[seat].Y));
                team.Pace[seat] = 0;
                break;

            default:
                if (_heldTicks >= HoldTicks(_decision, instructions.Tempo, keeper))
                {
                    Kick(side, seat, _decision);
                }
                else
                {
                    // He takes the ball in his stride towards where he means to play it.
                    team.Anchor[seat] = _decision.Target;
                    team.Pace[seat] = TickHoldPace;
                }

                break;
        }
    }

    /// <summary>The pace a man carries the ball at while he waits to play it, in basis points of top speed.</summary>
    private const int TickHoldPace = 6_000;

    /// <summary>
    /// Gets how long a man keeps the ball before the kick he has chosen leaves his foot: a second for a pass, a third of that for a
    /// shot, a flick when he is hurried. A goalkeeper has already waited.
    /// </summary>
    private static int HoldTicks(in TickCarrierDecision decision, MatchTempo tempo, bool keeper)
    {
        if (keeper)
        {
            return 0;
        }

        var hold = decision.Action switch
        {
            TickCarrierAction.Shoot => 3,
            TickCarrierAction.Clear => 2,
            TickCarrierAction.Cross => 5,
            _ => 10,
        };

        hold = tempo switch
        {
            MatchTempo.High => hold * 6 / 10,
            MatchTempo.Low => hold * 14 / 10,
            _ => hold,
        };

        return decision.EffectivePressure >= 5_000 ? Math.Min(hold, 3) : hold;
    }

    private void Kick(int side, int seat, in TickCarrierDecision decision)
    {
        var team = _teams[side];
        var origin = new SpatialPoint(_ball.UnitX, _ball.UnitY);

        if (!TickBallCarrierBrain.Execute(decision, team.Skills[seat], _ball, _random))
        {
            return;
        }

        AfterKick(side, seat, decision, origin, TickRestartKind.KickOff, fromRestart: false);
    }

    /// <summary>What the kick that has just left a foot starts: a pass in flight, a shot to be saved, a ball to be met.</summary>
    private void AfterKick(int side, int seat, in TickCarrierDecision decision, SpatialPoint origin, TickRestartKind kind, bool fromRestart)
    {
        var team = _teams[side];
        var me = new TickRef(side, team.SlotNumber[seat]);

        _controllerSide = -1;
        _kicker = me;
        _kickTick = _tick;
        _decisionOwner = TickRef.None;
        _offsidePending = false;
        _passPending = false;
        Touch(me);

        switch (decision.Action)
        {
            case TickCarrierAction.Shoot:
                {
                    var penalty = fromRestart && kind == TickRestartKind.Penalty;
                    var freeKick = fromRestart && kind == TickRestartKind.FreeKick;

                    Tag(me, penalty ? PassageAction.Penalty : freeKick ? PassageAction.FreeKick : PassageAction.Shot);
                    StartShot(side, seat, decision, origin, penalty, freeKick);
                    break;
                }

            case TickCarrierAction.Pass or TickCarrierAction.ThroughBall or TickCarrierAction.Cross:
                {
                    var id = team.Id[seat];

                    _passPending = true;
                    _passTried = 0;
                    _passer = me;
                    _intended = decision.Receiver >= 0 && decision.Receiver < team.Count
                        ? new TickRef(side, team.SlotNumber[decision.Receiver])
                        : TickRef.None;

                    team.Runtime.PassesAttempted[id] = team.Runtime.PassesAttempted.GetValueOrDefault(id) + 1;
                    Tag(me, decision.Action == TickCarrierAction.Cross ? PassageAction.Cross : PassageAction.Pass);

                    if (decision.Receiver >= 0
                        && decision.Receiver < team.Count
                        && (!fromRestart || TickMatchStateMachine.OffsideApplies(kind)))
                    {
                        var opponents = _teams[1 - side];

                        _offsidePending = TickDefensiveAI.IsOffside(
                            team.Body[decision.Receiver].X,
                            _ball.X,
                            opponents.Body.AsSpan(0, opponents.Count),
                            opponents.IsHome);
                    }

                    break;
                }

            default:
                Tag(me, PassageAction.Pass);
                break;
        }

        PredictRest();
    }

    private void Touch(TickRef who)
    {
        _lastTouch = who;
        _lastTouchBySide[who.Side] = who;
    }

    /// <summary>Gives a man the ball at his feet.</summary>
    private void Attach(int side, int seat)
    {
        var team = _teams[side];

        _ball.Attach(seat);
        _controllerSide = side;
        Touch(new TickRef(side, team.SlotNumber[seat]));
    }

    // ---- Moving the bodies -------------------------------------------------------------------------------------------------------------

    private void MoveTeams()
    {
        foreach (var team in _teams)
        {
            var count = team.Count;
            var bodies = team.Body.AsSpan(0, count);

            for (var seat = 0; seat < count; seat++)
            {
                if (!team.HasIntent[seat])
                {
                    team.Intent[seat] = TickSteering.Steer(seat, bodies, team.Profile[seat], team.Anchor[seat], team.Pace[seat]);
                }
            }

            for (var seat = 0; seat < count; seat++)
            {
                TickPlayerPhysics.Step(ref team.Body[seat], team.Profile[seat], team.Intent[seat]);
            }
        }
    }

    // ---- Challenges on the man with the ball ----------------------------------------------------------------------------------------------

    private void ResolveChallenges()
    {
        var side = _controllerSide;
        var seat = _ball.ControllerIndex;
        var team = _teams[side];
        var opponents = _teams[1 - side];
        var carrier = team.Body[seat];

        // The goalkeeper holding the ball in his hands cannot be tackled.
        if (team.IsKeeper(seat))
        {
            return;
        }

        if (opponents.Count > 0 && opponents.IsKeeper(0) && TickGoalkeeperAI.CanSmother(opponents.Body[0], _ball))
        {
            Smother(side, seat);

            return;
        }

        var challenger = -1;
        var nearest = long.MaxValue;

        for (var other = 0; other < opponents.Count; other++)
        {
            if (opponents.IsKeeper(other) || !TickTackleResolver.InContact(opponents.Body[other], carrier))
            {
                continue;
            }

            long dx = opponents.Body[other].X - carrier.X;
            long dy = opponents.Body[other].Y - carrier.Y;
            var distance = (dx * dx) + (dy * dy);

            if (distance < nearest)
            {
                nearest = distance;
                challenger = other;
            }
        }

        if (challenger < 0)
        {
            return;
        }

        // A man in touch of the carrier goes in for the ball on some ticks and not others: the draw is always taken.
        var commit = Math.Clamp(ChallengeCommitBase + (ChallengeCommitPerAggression * opponents.Skills[challenger].Aggression), 0, BasisPointsCertain);

        if (_random.NextBasisPoints() >= commit)
        {
            return;
        }

        // The home crowd and the referee lean on the duel: the roll shifts by the home edge towards the home side's man.
        var edge = HomeEdgeBasisPoints(opponents.IsHome);
        var outcome = TickTackleResolver.Resolve(
            opponents.Skills[challenger],
            team.Skills[seat],
            opponents.Runtime.Instructions.Tackling,
            _rules,
            Math.Clamp(_random.NextBasisPoints() - edge, 0, BasisPointsCertain - 1));

        var carrierRef = new TickRef(side, team.SlotNumber[seat]);
        var challengerRef = new TickRef(1 - side, opponents.SlotNumber[challenger]);
        var carrierId = team.Id[seat];
        var challengerId = opponents.Id[challenger];
        var spot = new SpatialPoint(TickSpatialUnits.ToUnits(carrier.X), TickSpatialUnits.ToUnits(carrier.Y));

        if (outcome == TickTackleOutcome.Foul
            && (team.IsHome ? SpatialPitch.IsInAwayPenaltyBox(spot) : SpatialPitch.IsInHomePenaltyBox(spot))
            && _random.NextBasisPoints() >= PenaltyGivenBasisPoints)
        {
            // A referee gives the penalty on only some of the fouls in the area; the rest is play on.
            outcome = TickTackleOutcome.Beaten;
        }

        TickTackleResolver.Apply(outcome, ref opponents.Body[challenger], ref team.Body[seat], challenger, _ball);

        switch (outcome)
        {
            case TickTackleOutcome.Won:
                Dribbled(team, carrierId, completed: false);
                Rate(opponents, challengerId, _rules.LiveRatingTackleBonusBasisPoints);
                Rate(team, carrierId, -_rules.LiveRatingTackleLostPenaltyBasisPoints);
                Tag(challengerRef, PassageAction.Tackle);
                _controllerSide = 1 - side;
                Touch(challengerRef);
                TurnOver();
                break;

            case TickTackleOutcome.PokedLoose:
                Dribbled(team, carrierId, completed: false);
                Rate(opponents, challengerId, _rules.LiveRatingTackleBonusBasisPoints);
                Rate(team, carrierId, -_rules.LiveRatingTackleLostPenaltyBasisPoints);
                Tag(challengerRef, PassageAction.Tackle);
                _controllerSide = -1;
                Touch(challengerRef);
                TurnOver();
                PredictRest();
                break;

            case TickTackleOutcome.Beaten:
                Dribbled(team, carrierId, completed: true);
                Rate(team, carrierId, _rules.LiveRatingTackleBonusBasisPoints);
                Rate(opponents, challengerId, -_rules.LiveRatingTackleLostPenaltyBasisPoints);
                Tag(carrierRef, PassageAction.Carry);
                break;

            default:
                Rate(opponents, challengerId, -_rules.LiveRatingTackleLostPenaltyBasisPoints);
                Tag(challengerRef, PassageAction.Tackle);
                FoulBy(1 - side, challengerId, side, spot);
                break;
        }
    }

    private void Smother(int side, int seat)
    {
        var team = _teams[side];
        var keeperTeam = _teams[1 - side];
        var attackerId = team.Id[seat];
        var keeperId = keeperTeam.Id[0];
        var spot = new SpatialPoint(_ball.UnitX, _ball.UnitY);
        var outcome = TickGoalkeeperAI.Smother(keeperTeam.Skills[0], team.Skills[seat], _random.NextBasisPoints());

        if (outcome == TickSmotherOutcome.Foul && _random.NextBasisPoints() >= PenaltyGivenBasisPoints)
        {
            outcome = TickSmotherOutcome.Beaten;
        }

        TickGoalkeeperAI.ApplySmother(outcome, ref keeperTeam.Body[0], ref team.Body[seat], 0, keeperTeam.IsHome, _ball);

        var keeperRef = new TickRef(1 - side, keeperTeam.SlotNumber[0]);

        switch (outcome)
        {
            case TickSmotherOutcome.Claimed:
                Dribbled(team, attackerId, completed: false);
                Rate(keeperTeam, keeperId, _rules.LiveRatingTackleBonusBasisPoints);
                _controllerSide = 1 - side;
                Touch(keeperRef);
                Tag(keeperRef, PassageAction.Save);
                TurnOver();
                break;

            case TickSmotherOutcome.Spilled:
                Dribbled(team, attackerId, completed: false);
                _controllerSide = -1;
                Touch(keeperRef);
                Tag(keeperRef, PassageAction.Save);
                TurnOver();
                PredictRest();
                break;

            case TickSmotherOutcome.Beaten:
                Dribbled(team, attackerId, completed: true);
                Tag(keeperRef, PassageAction.Dive);
                break;

            default:
                FoulBy(1 - side, keeperId, side, spot);
                break;
        }
    }

    /// <summary>The chance, in basis points, that a foul on a carrier inside the penalty area is given as a penalty.</summary>
    private const int PenaltyGivenBasisPoints = 1_000;

    /// <summary>The chance, in basis points, that a defender in touch of the carrier challenges on a tick, at Aggression 0.</summary>
    private const int ChallengeCommitBase = 1_000;

    /// <summary>The chance each point of Aggression adds, in basis points.</summary>
    private const int ChallengeCommitPerAggression = 100;

    private const int BasisPointsCertain = 10_000;

    private static void Dribbled(TickTeam team, Guid id, bool completed)
    {
        var runtime = team.Runtime;

        runtime.DribblesAttempted[id] = runtime.DribblesAttempted.GetValueOrDefault(id) + 1;

        if (completed)
        {
            runtime.DribblesCompleted[id] = runtime.DribblesCompleted.GetValueOrDefault(id) + 1;
        }
    }

    private static void Rate(TickTeam team, Guid id, int delta) => team.Runtime.AdjustLiveRating(id, delta);

    /// <summary>The ball has changed hands: the old side's movement is forgotten and the new carrier decides afresh.</summary>
    private void TurnOver()
    {
        foreach (var team in _teams)
        {
            team.SupporterMask = 0;
            team.RunnerMask = 0;
            team.PreviousPresser = -1;
        }

        _decisionOwner = TickRef.None;
        _assistPasser = TickRef.None;
        _assistReceiver = TickRef.None;
        _passPending = false;
        _offsidePending = false;
    }

    // ---- The ball ---------------------------------------------------------------------------------------------------------------------------

    private void StepBall()
    {
        var previousX = _ball.X;
        var previousY = _ball.Y;

        if (_shotLive && !_shotResolved && _forecast.OnTarget && TickShotStopper.ShouldResolve(_forecast, _shotTicks))
        {
            ResolveSave();

            if (_ball.Mode == TickBallMode.Controlled)
            {
                return;
            }
        }

        var boundary = _ball.Step();

        if (_shotLive)
        {
            _shotTicks++;
        }

        switch (boundary)
        {
            case TickBallBoundary.InPlay:
                TryReceive(previousX, previousY);
                ExpireShot();
                break;

            case TickBallBoundary.HitPost:
            case TickBallBoundary.HitCrossbar:
                OnWoodwork();
                break;

            case TickBallBoundary.GoalHomeEnd:
            case TickBallBoundary.GoalAwayEnd:
                ScoreGoal(boundary);
                break;

            case TickBallBoundary.OutTouchline:
                OutOnTouchline();
                break;

            default:
                OutOnGoalLine();
                break;
        }
    }

    /// <summary>Works out where the ball will come to be met (where a lofted one lands) or rest, so the players can run to it.</summary>
    private void PredictRest()
    {
        _scratch.CopyFrom(_ball);

        var x = _ball.UnitX;
        var y = _ball.UnitY;
        var wasAirborne = _scratch.IsAirborne;

        for (var step = 0; step < 60; step++)
        {
            var boundary = _scratch.Step();

            x = _scratch.UnitX;
            y = _scratch.UnitY;

            if (boundary is not (TickBallBoundary.InPlay or TickBallBoundary.HitPost or TickBallBoundary.HitCrossbar))
            {
                break;
            }

            if (_scratch.IsAirborne)
            {
                wasAirborne = true;
            }
            else if (wasAirborne || _scratch.GroundSpeed == 0)
            {
                break;
            }
        }

        _restX = Math.Clamp(x, 0, SpatialPitch.PitchLength);
        _restY = Math.Clamp(y, 0, SpatialPitch.PitchWidth);
    }

    /// <summary>Gives the ball to whoever reaches it first, unless it is a shot, which a defender may block and a goalkeeper deals with.</summary>
    private void TryReceive(int previousX, int previousY)
    {
        if (_ball.Mode == TickBallMode.Controlled || _ball.UnitZ > TickBallPhysics.HeadReachZUnits)
        {
            return;
        }

        var fast = _ball.GroundSpeed > FastBall;
        var radius = (long)TickSpatialUnits.ToFixed(
            _ball.UnitZ > TickBallPhysics.GroundContactZUnits
                ? TickBallPhysics.AerialReceptionRadiusUnits
                : TickBallPhysics.GroundReceptionRadiusUnits);

        var bestSide = -1;
        var bestSeat = -1;
        var best = long.MaxValue;

        for (var index = 0; index < _teams.Length; index++)
        {
            var team = _teams[index];

            if (_shotLive && index == _shooter.Side)
            {
                continue;
            }

            for (var seat = 0; seat < team.Count; seat++)
            {
                if (_shotLive && team.IsKeeper(seat))
                {
                    continue;
                }

                if (_kicker.Side == index && _kicker.Slot == team.SlotNumber[seat] && _tick - _kickTick < KickCooldownTicks)
                {
                    continue;
                }

                var body = team.Body[seat];

                if (body.Lockout > 0)
                {
                    continue;
                }

                if (_passPending && index != _passer.Side && (_tick - _kickTick < PassGraceTicks || (_passTried & PassTriedBit(index, seat)) != 0))
                {
                    continue;
                }

                long distance;

                if (fast)
                {
                    distance = TickOffBallSupport.SegmentDistanceSquared(body.X, body.Y, previousX, previousY, _ball.X, _ball.Y);
                }
                else
                {
                    long dx = _ball.X - body.X;
                    long dy = _ball.Y - body.Y;

                    distance = (dx * dx) + (dy * dy);
                }

                if (distance <= radius * radius && distance < best)
                {
                    if (_passPending && index != _passer.Side && !CutsOutPass(team.Skills[seat]))
                    {
                        _passTried |= PassTriedBit(index, seat);

                        continue;
                    }

                    best = distance;
                    bestSide = index;
                    bestSeat = seat;
                }
            }
        }

        if (bestSide < 0)
        {
            return;
        }

        if (_shotLive)
        {
            TryBlock(bestSide, bestSeat);

            return;
        }

        Take(bestSide, bestSeat);
    }

    /// <summary>The ticks after a pass leaves the passer's foot in which an opponent cannot reach it: it has not yet got out of his reach.</summary>
    private const int PassGraceTicks = 3;

    /// <summary>
    /// Gets how far a duel tilts towards one side for playing at home, in basis points: the snapshot's home advantage (a ratio of 1.0420 is
    /// 420 basis points above even), added to the chance of the home side's man in every tackle and every interception and taken off the
    /// visiting side's man's. The tick engine has no team rating to multiply, so the crowd and the referee lean on the duels.
    /// </summary>
    /// <param name="homeMan">Whether the man whose chance it is plays for the home side.</param>
    private int HomeEdgeBasisPoints(bool homeMan) =>
        (homeMan ? 1 : -1) * (_state.Input.HomeAdvantageBasisPoints - BasisPointsCertain);

    private static ulong PassTriedBit(int side, int seat) => 1UL << ((side * 16) + seat);

    /// <summary>
    /// Rolls whether a defender who has reached a pass in flight gets a foot to it: <see cref="InterceptMaximum"/> scaled by his
    /// Anticipation and Positioning, one draw. A defender who fails has had his chance and lets the ball past.
    /// </summary>
    private bool CutsOutPass(in TickPlayerSkills skills)
    {
        var passerTeam = _teams[_passer.Side];
        var passerSeat = passerTeam.SeatOfSlot(_passer.Slot);
        var passer = passerSeat >= 0 ? passerTeam.Skills[passerSeat] : skills;
        var difference = ((skills.Anticipation + skills.Positioning) - (passer.Passing + passer.Vision)) / 2;
        var factor = Math.Clamp(100 + (InterceptSkillPerPoint * difference), 50, 160);
        var chance = Math.Clamp((InterceptMaximum * factor / 100) + HomeEdgeBasisPoints(_teams[1 - _passer.Side].IsHome), 500, 9_000);

        return _random.NextBasisPoints() < chance;
    }

    /// <summary>The chance, in basis points, that a defender of average skill who reaches a pass in flight cuts it out.</summary>
    private const int InterceptMaximum = 3_500;

    /// <summary>The percentage points of <see cref="InterceptMaximum"/> each point the defender's Anticipation and Positioning are above the passer's Passing and Vision adds.</summary>
    private const int InterceptSkillPerPoint = 4;

    /// <summary>A player has reached the ball: it is his, unless he is the man a pass was meant for and he was offside.</summary>
    private void Take(int side, int seat)
    {
        var team = _teams[side];
        var me = new TickRef(side, team.SlotNumber[seat]);
        var changedSide = _lastTouch.IsNone || _lastTouch.Side != side;

        if (_offsidePending)
        {
            _offsidePending = false;

            if (_passPending && side == _passer.Side && me == _intended)
            {
                CallOffside(side, seat);

                return;
            }
        }

        if (_passPending)
        {
            if (side == _passer.Side && me != _passer)
            {
                var passerTeam = _teams[_passer.Side];
                var passerSeat = passerTeam.SeatOfSlot(_passer.Slot);

                if (passerSeat >= 0)
                {
                    var passerId = passerTeam.Id[passerSeat];

                    passerTeam.Runtime.PassesCompleted[passerId] = passerTeam.Runtime.PassesCompleted.GetValueOrDefault(passerId) + 1;
                }

                Tag(me, PassageAction.Receive);
                _assistPasser = _passer;
                _assistReceiver = me;
                _assistTick = _tick;
            }
            else if (side != _passer.Side)
            {
                Tag(me, PassageAction.Interception);
            }

            _passPending = false;
        }

        Attach(side, seat);

        if (changedSide)
        {
            TurnOver();
        }
    }

    /// <summary>A defender in the line of a shot may block it; the shot goes past him otherwise.</summary>
    private void TryBlock(int side, int seat)
    {
        var team = _teams[side];
        var skills = team.Skills[seat];
        var chance = Math.Clamp(BlockBaseBasisPoints + (BlockPerPositioning * skills.Positioning), 3_000, 8_500);
        var roll = _random.NextBasisPoints();
        var swing = _random.NextRange(-BlockSpreadAngle, BlockSpreadAngle);

        if (roll >= chance)
        {
            return;
        }

        var blocker = new TickRef(side, team.SlotNumber[seat]);
        var heading = TickTrigonometry.AngleOf(_ball.VelocityX, _ball.VelocityY) + TickTrigonometry.HalfTurn + swing;
        var speed = (long)_ball.GroundSpeed * BlockSpeedPercent / 100;

        EmitShotResult(EngineEventType.ShotBlocked);
        _shotLive = false;

        if (_random.NextBasisPoints() < BlockToCornerBasisPoints)
        {
            // Turned behind for a corner: he gets a foot to it and it runs on, wide of the post on the side it was heading for.
            var lineX = _teams[_shooter.Side].IsHome ? SpatialPitch.PitchLength : 0;
            var wideY = _ball.UnitY >= SpatialPitch.GoalYCenter ? SpatialPitch.GoalYMax + BlockWideOfPost : SpatialPitch.GoalYMin - BlockWideOfPost;

            _ball.LaunchRolling(lineX, wideY, TickSpatialUnits.SpeedToFixedPerTick(BlockCornerSpeed));
            Tag(new TickRef(side, team.SlotNumber[seat]), PassageAction.Interception);
            _controllerSide = -1;
            _passPending = false;
            Touch(new TickRef(side, team.SlotNumber[seat]));
            TurnOver();
            PredictRest();

            return;
        }

        _ball.Kick(
            (int)(TickTrigonometry.Cos(heading) * speed / TickTrigonometry.Scale),
            (int)(TickTrigonometry.Sin(heading) * speed / TickTrigonometry.Scale),
            0);

        Tag(blocker, PassageAction.Interception);
        _controllerSide = -1;
        _passPending = false;
        Touch(blocker);
        TurnOver();
        PredictRest();
    }

    /// <summary>The chance, in basis points, that a blocked shot is turned behind for a corner.</summary>
    private const int BlockToCornerBasisPoints = 4_500;

    /// <summary>How far outside the post a block turns the ball behind, in pitch units.</summary>
    private const int BlockWideOfPost = 400;

    /// <summary>The speed a block sends the ball behind at, in cm/s.</summary>
    private const int BlockCornerSpeed = 600;

    /// <summary>The chance, in basis points, that a defender in the line of a shot blocks it at Positioning 0.</summary>
    private const int BlockBaseBasisPoints = 3_800;

    /// <summary>The chance gained per point of Positioning, in basis points.</summary>
    private const int BlockPerPositioning = 160;

    /// <summary>How far a blocked shot may be turned either side of straight back, in binary angle units.</summary>
    private const int BlockSpreadAngle = 160;

    /// <summary>The share of its pace a blocked shot keeps, in percent.</summary>
    private const int BlockSpeedPercent = 35;
}
