using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Spatial;

namespace Tactical11Manager.MatchEngine.Tick;

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

    /// <summary>Whether the pass in flight is a cross that counts as one on the passer's line: any cross but a corner's.</summary>
    private bool _passIsCross;

    /// <summary>The opponents who have already had their chance to cut out the pass in flight: one bit per side and seat.</summary>
    private ulong _passTried;
    private TickRef _passer = TickRef.None;

    /// <summary>Where the pass in flight was meant to go, before the kick's error.</summary>
    private SpatialPoint _passAim;
    private TickRef _intended = TickRef.None;

    /// <summary>The man a ball played back (a recycle) is for, until anybody touches the ball; a recycle is not a pass pending, so it has no intended receiver of its own.</summary>
    private TickRef _recycledTo = TickRef.None;
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

    /// <summary>The ball's path for the next ticks, worked out each tick it is loose so the men can find where to meet it.</summary>
    private readonly TickBallPathPoint[] _path = new TickBallPathPoint[TickInterception.PathTicks];

    /// <summary>The bodies of a side with one man moved to where he will be, for a decision taken ahead of the ball.</summary>
    private readonly TickPlayerState[] _projected = new TickPlayerState[TickTacticalGeometry.TeamSize];

    /// <summary>The ticks until the man a pass is meant for meets it, or -1 when he cannot (or no pass is in flight).</summary>
    private int _receiverReach = -1;

    private SpatialPoint _receiverPoint;
    private int _receiverSeat = -1;

    /// <summary>How far from where a pass was aimed the man it was played to goes to meet it, in pitch units: the catch the brain assumes (2.5 m) and a little more.</summary>
    private const int ReceiverAdjustUnits = 300;

    /// <summary>Whether the man a pass was meant for is the one sent to meet it.</summary>
    private bool _receiverMeets;

    /// <summary>What the man a pass is coming to decided to do with it, taken before it reached him.</summary>
    private TickCarrierDecision _early;
    private TickRef _earlyOwner = TickRef.None;
    private int _earlyTick;

    private int _restX = SpatialPitch.PitchLength / 2;
    private int _restY = SpatialPitch.GoalYCenter;

    /// <summary>The tick each slot entity last had a blame stamp (beaten, bypassed), so one man is not shown beaten twice in a few seconds.</summary>
    private readonly int[] _blameTick = Enumerable.Repeat(int.MinValue / 2, TickMatchRecording.Entities).ToArray();

    /// <summary>The slot entity of the carrier each defender is watching run at him, or -1: one entry per defender entity.</summary>
    private readonly int[] _bypassCarrier = Enumerable.Repeat(-1, TickMatchRecording.Entities).ToArray();

    /// <summary>The tick each defender began watching the carrier.</summary>
    private readonly int[] _bypassSince = new int[TickMatchRecording.Entities];

    /// <summary>The dribble the carrier is on, as the commentary has been told of it: no dribble yet.</summary>
    private const int NoDribble = -1;

    /// <summary>The dribble the carrier is on is a plain carry, which the commentary is not told of.</summary>
    private const int PlainCarry = -2;

    /// <summary>The dribble the carrier is on is a break into open space.</summary>
    private const int BreakAway = -3;

    /// <summary>How near a defender, ahead of a dribbling carrier, must be for the run to be a take-on (4 m).</summary>
    private const int TakeOnUnits = 400;

    /// <summary>How far ahead of a dribbling carrier no defender may stand for the run to be a break into space (8 m).</summary>
    private const int BreakClearUnits = 800;

    /// <summary>The ticks that must pass before the commentary is told of another dribble by the same carrier (1 s).</summary>
    private const int DribbleStampGapTicks = 10;

    /// <summary>The distance from which a pass is called a long ball (30 m).</summary>
    private const int LongBallUnits = 3_000;

    /// <summary>The distance from goal beyond which a shot is called a long-range effort (22 m).</summary>
    private const int LongRangeShotUnits = 2_200;

    /// <summary>What the carrier's dribble was last stamped as: a defender's slot entity, <see cref="PlainCarry"/>, <see cref="BreakAway"/> or <see cref="NoDribble"/>. Recording only.</summary>
    private int _dribbleKey = NoDribble;

    /// <summary>The tick each slot entity's dribble was last told to the commentary.</summary>
    private readonly int[] _dribbleStampTick = Enumerable.Repeat(int.MinValue / 2, TickMatchRecording.Entities).ToArray();

    private void StepOpenPlay()
    {
        var controlled = _ball.Mode == TickBallMode.Controlled;
        var carrierSide = controlled ? _controllerSide : -1;
        var carrierSeat = controlled ? _ball.ControllerIndex : -1;
        var possession = controlled ? carrierSide : (_lastTouch.IsNone ? 0 : _lastTouch.Side);

        PlanBall(controlled);
        FollowBall(controlled, possession);

        for (var index = 0; index < _teams.Length; index++)
        {
            var team = _teams[index];

            Array.Clear(team.Engaged);
            Array.Clear(team.UseMicro);

            TickTacticalGeometry.ResolveTeam(
                team.Spec.AsSpan(0, team.Count),
                team.Style,
                team.IsHome,
                team.PossessionBlend,
                team.RefX,
                team.RefY,
                team.Anchor.AsSpan(0, team.Count));
        }

        AssignAttack(_teams[possession], _teams[1 - possession], carrierSide == possession ? carrierSeat : -1);
        AssignDefence(_teams[1 - possession], _teams[possession], controlled ? carrierSeat : -1);
        AssignChasers(controlled);
        PrepareReceiver();
        AssignKeepers(controlled, carrierSide);

        if (controlled)
        {
            _teams[carrierSide].Engaged[carrierSeat] = true;
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

    // ---- The block follows the ball -------------------------------------------------------------------------------------------------

    /// <summary>The share of the way to its target the ball reference covers each tick, in percent.</summary>
    private const int ReferencePercent = 25;

    /// <summary>The fastest the ball reference moves, in pitch units per tick (9 m/s).</summary>
    private static readonly int ReferenceStepUnits = TickSpatialUnits.ToUnits(TickSpatialUnits.SpeedToFixedPerTick(900));

    /// <summary>The ticks the block takes to change from its shape with the ball to the one without it (1.5 s), and back.</summary>
    private const int PossessionBlendTicks = 15;

    /// <summary>The side that had the ball last tick: when it changes, the squares and steps worked out for the old shapes are dropped.</summary>
    private int _microPossession = -1;

    /// <summary>The side that has the ball (or touched it last) this tick.</summary>
    private int _possessionSide;

    /// <summary>
    /// Moves what each block's shape follows. It is not the ball: it is where the ball is going. A pass in flight sends it to the point the
    /// man it is for will meet it at, a man with the ball sends it a second ahead of him, and a loose ball sends it to where it will stop; it
    /// closes a quarter of the way to its target a tick and never runs faster than 9 m/s. The shape with the ball and the shape without it are
    /// blended by how long ago the ball changed hands, so a turnover bends the block instead of switching it.
    /// </summary>
    private void FollowBall(bool controlled, int possession)
    {
        _possessionSide = possession;

        if (_microPossession != possession)
        {
            _microPossession = possession;

            foreach (var team in _teams)
            {
                Array.Clear(team.HasOffset);
                team.ReturnSlot = 0;
                team.ThirdSlot = 0;
            }
        }

        int targetX;
        int targetY;

        if (_passPending && _receiverReach >= 0)
        {
            targetX = _receiverPoint.X;
            targetY = _receiverPoint.Y;
        }
        else if (controlled)
        {
            var body = _teams[_controllerSide].Body[_ball.ControllerIndex];

            targetX = _ball.UnitX + (body.VelocityX * TickSpatialUnits.TicksPerSecond / TickSpatialUnits.FixedScale);
            targetY = _ball.UnitY + (body.VelocityY * TickSpatialUnits.TicksPerSecond / TickSpatialUnits.FixedScale);
        }
        else
        {
            targetX = _restX;
            targetY = _restY;
        }

        targetX = Math.Clamp(targetX, 0, SpatialPitch.PitchLength);
        targetY = Math.Clamp(targetY, 0, SpatialPitch.PitchWidth);

        for (var index = 0; index < _teams.Length; index++)
        {
            var team = _teams[index];
            long moveX = (targetX - team.RefX) * ReferencePercent / 100;
            long moveY = (targetY - team.RefY) * ReferencePercent / 100;
            var length = SpatialMath.Sqrt((moveX * moveX) + (moveY * moveY));

            if (length > ReferenceStepUnits)
            {
                moveX = moveX * ReferenceStepUnits / length;
                moveY = moveY * ReferenceStepUnits / length;
            }

            team.RefX += (int)moveX;
            team.RefY += (int)moveY;

            var blendStep = 10_000 / PossessionBlendTicks;

            team.PossessionBlend = index == possession
                ? Math.Min(10_000, team.PossessionBlend + blendStep)
                : Math.Max(0, team.PossessionBlend - blendStep);
        }
    }

    /// <summary>While the ball is dead the references sit on it, so open play begins with the block where the restart is.</summary>
    private void SnapReferences()
    {
        CloseBreak();

        foreach (var team in _teams)
        {
            team.RefX = _ball.UnitX;
            team.RefY = _ball.UnitY;
            Array.Clear(team.HasOffset);
            team.ReturnSlot = 0;
            team.ThirdSlot = 0;
        }

        _microPossession = -1;
    }

    // ---- Orders ----------------------------------------------------------------------------------------------------------------------

    private void AssignAttack(TickTeam attackers, TickTeam defenders, int carrier)
    {
        var count = attackers.Count;

        if (count == 0)
        {
            return;
        }

        var returnSeat = RunSeat(attackers, carrier, attackers.ReturnSlot, attackers.ReturnUntil, out var returnLive);
        var thirdSeat = RunSeat(attackers, carrier, attackers.ThirdSlot, attackers.ThirdUntil, out var thirdLive);

        if (!returnLive)
        {
            attackers.ReturnSlot = 0;
        }

        if (!thirdLive)
        {
            attackers.ThirdSlot = 0;
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
            Tick = _tick,
            Offsets = attackers.MicroOffset.AsSpan(0, count),
            HasOffset = attackers.HasOffset.AsSpan(0, count),
            OffsetPaces = attackers.MicroPace.AsSpan(0, count),
            ReturnSeat = returnSeat,
            ReturnTarget = attackers.ReturnTarget,
            ThirdSeat = thirdSeat,
            ThirdTarget = attackers.ThirdTarget,
            BreakRunners = attackers.IsBreaking(_tick) ? attackers.BreakRunners : 0,
            BreakImmediate = attackers.IsBreaking(_tick) && attackers.BreakImmediate,
            Holding = attackers.Runtime.Instructions.CounterAttack && !attackers.IsBreaking(_tick),
            EdgeFactorPercent = attackers.AttackEdgeFactorPercent,
        };

        var orders = attackers.AttackOrders.AsSpan(0, count);

        TickOffBallSupport.Assign(situation, orders);
        attackers.SupporterMask = TickOffBallSupport.SupporterMask(orders);
        attackers.RunnerMask = TickOffBallSupport.RunnerMask(orders);
        TickOffBallSupport.ToSteering(orders, attackers.Anchor.AsSpan(0, count), attackers.Pace.AsSpan(0, count));

        for (var seat = 0; seat < count; seat++)
        {
            var family = attackers.Spec[seat].Family;

            // A man with a job goes where it sends him; one with none walks about his place, a back-line man in his line.
            attackers.UseMicro[seat] = orders[seat].Role == TickAttackingRole.Holding && family != MatchPositionFamily.Goalkeeper;
            attackers.Engaged[seat] = attackers.Body[seat].Lockout > 0;
        }
    }

    /// <summary>
    /// Gets the seat of a man on a combination run, or -1 when none is on: the run ends when its ticks are up, or when the ball reaches him
    /// (a return or a lay-off played), or when the man has left the pitch.
    /// </summary>
    private int RunSeat(TickTeam team, int carrier, int slot, int until, out bool live)
    {
        live = false;

        if (slot == 0 || _tick > until)
        {
            return -1;
        }

        var seat = team.SeatOfSlot(slot);

        if (seat < 0 || seat == carrier)
        {
            return -1;
        }

        live = true;

        return seat;
    }

    /// <summary>
    /// A short pass has left the foot: the passer may run for the ball back (a one-two) and a midfielder may run past a pressed forward who has
    /// his back to goal (the third man). Only the first run of each kind at a time is made.
    /// </summary>
    private void StartCombinations(int side, int passer, int receiver)
    {
        var team = _teams[side];
        var opponents = _teams[1 - side];
        var bodies = team.Body.AsSpan(0, team.Count);
        var specs = team.Spec.AsSpan(0, team.Count);
        var defenders = opponents.Body.AsSpan(0, opponents.Count);

        if (team.ReturnSlot == 0 && TickCombinations.TryReturnRun(team.IsHome, bodies, specs, defenders, passer, receiver, out var back))
        {
            team.ReturnSlot = team.SlotNumber[passer];
            team.ReturnTarget = back;
            team.ReturnUntil = _tick + TickCombinations.ReturnTicks;
        }

        if (team.ThirdSlot == 0 && TickCombinations.TryThirdMan(team.IsHome, bodies, specs, defenders, passer, receiver, out var third, out var ahead))
        {
            team.ThirdSlot = team.SlotNumber[third];
            team.ThirdTarget = ahead;
            team.ThirdUntil = _tick + TickCombinations.ThirdManTicks;
        }
    }

    private void AssignDefence(TickTeam defenders, TickTeam attackers, int carrier)
    {
        var count = defenders.Count;

        if (count == 0)
        {
            return;
        }

        // The side with the ball is not defending: it starts its count again whenever it loses the ball.
        attackers.UnpressedTicks = 0;
        defenders.UnpressedTicks = TickDefensiveAI.PressApplies(defenders.Runtime.Instructions.Pressing, _ball.UnitX, defenders.IsHome)
            ? 0
            : defenders.UnpressedTicks + 1;

        var situation = new TickDefensiveSituation
        {
            IsHome = defenders.IsHome,
            Pressing = defenders.Runtime.Instructions.Pressing,
            LowBlock = defenders.Style.LowBlock,
            Defenders = defenders.Body.AsSpan(0, count),
            Specs = defenders.Spec.AsSpan(0, count),
            Anchors = defenders.Anchor.AsSpan(0, count),
            Skills = defenders.Skills.AsSpan(0, count),
            Attackers = attackers.Body.AsSpan(0, attackers.Count),
            BallX = _ball.UnitX,
            BallY = _ball.UnitY,
            CarrierIndex = carrier,
            PreviousPresser = defenders.PreviousPresser,
            UnpressedTicks = defenders.UnpressedTicks,
            Tick = _tick,
            Offsets = defenders.MicroOffset.AsSpan(0, count),
            HasOffset = defenders.HasOffset.AsSpan(0, count),
            OffsetPaces = defenders.MicroPace.AsSpan(0, count),
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

        for (var seat = 0; seat < count; seat++)
        {
            var role = orders[seat].Role;

            defenders.UseMicro[seat] = (role == TickDefensiveRole.Holding || role == TickDefensiveRole.Line)
                && defenders.Spec[seat].Family != MatchPositionFamily.Goalkeeper;
            defenders.Engaged[seat] = role == TickDefensiveRole.Presser
                || role == TickDefensiveRole.SupportPresser
                || defenders.Body[seat].Lockout > 0;
        }
    }

    /// <summary>
    /// Looks ahead along a ball nobody has: where it will be on each of the next ticks, and, for a pass in flight, where and when the man it
    /// was meant for meets it.
    /// </summary>
    private void PlanBall(bool controlled)
    {
        _receiverReach = -1;
        _receiverSeat = -1;

        if (controlled || _shotLive)
        {
            return;
        }

        _scratch.CopyFrom(_ball);
        TickInterception.Trace(_scratch, _path);

        if (!_passPending || _intended.IsNone)
        {
            return;
        }

        var receivers = _teams[_intended.Side];
        var seat = receivers.SeatOfSlot(_intended.Slot);

        if (seat < 0)
        {
            return;
        }

        var ticks = TickInterception.EarliestReach(_path, receivers.Body[seat], receivers.Profile[seat], out var point, _passAim, ReceiverAdjustUnits);

        if (ticks < 0)
        {
            return;
        }

        _receiverReach = ticks;
        _receiverPoint = point;
        _receiverSeat = seat;
    }

    /// <summary>
    /// A ball nobody has: the man of each side who can get to it first goes to meet it where he can (<see cref="TickInterception"/>), so a
    /// pass is met on its way and not at the end of its roll, and a loose ball is contested. A man who cannot get to the ball at all goes
    /// to where it will come to rest, if it is near.
    /// </summary>
    private void AssignChasers(bool controlled)
    {
        _receiverMeets = false;

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
            var bestReach = int.MaxValue;
            var bestDistance = range * range;
            var bestPoint = new SpatialPoint(_restX, _restY);
            var bestTicks = -1;
            var bestPace = TickOffBallSupport.RunPaceBasisPoints;

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

                var intended = _passPending && _intended.Side == index && _intended.Slot == team.SlotNumber[seat];
                SpatialPoint point;
                int ticks;

                if (intended)
                {
                    // The man it was played to goes for the ball where it comes to him; a pass that misses him he does not chase.
                    ticks = _receiverSeat == seat ? _receiverReach : -1;
                    point = _receiverSeat == seat ? _receiverPoint : _passAim;
                }
                else
                {
                    ticks = TickInterception.EarliestReach(_path, team.Body[seat], team.Profile[seat], out point);
                }

                if (ticks >= 0)
                {
                    // He gets there: the earliest wins, the man the pass was meant for with a head start, a tie to the nearer man.
                    var reach = intended ? ticks - IntendedHeadStartTicks : ticks;
                    long cx = team.Body[seat].X - TickSpatialUnits.ToFixed(point.X);
                    long cy = team.Body[seat].Y - TickSpatialUnits.ToFixed(point.Y);
                    var closeness = (cx * cx) + (cy * cy);

                    if (reach < bestReach || (reach == bestReach && closeness < bestDistance))
                    {
                        bestReach = reach;
                        bestDistance = closeness;
                        bestPoint = point;
                        bestTicks = ticks;
                        bestPace = MeetingPace(team.Body[seat], team.Profile[seat], closeness, ticks);
                        best = seat;
                    }

                    continue;
                }

                if (bestReach != int.MaxValue)
                {
                    continue;
                }

                long dx = team.Body[seat].X - restX;
                long dy = team.Body[seat].Y - restY;
                var distance = (dx * dx) + (dy * dy);

                if (intended)
                {
                    distance = distance * 64 / 100;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPoint = intended ? _passAim : new SpatialPoint(_restX, _restY);
                    bestTicks = -1;
                    bestPace = TickOffBallSupport.RunPaceBasisPoints;
                    best = seat;
                }
            }

            if (best < 0)
            {
                continue;
            }

            team.Anchor[best] = bestPoint;
            team.Pace[best] = bestPace;
            team.Engaged[best] = true;
            team.UseMicro[best] = false;

            if (_passPending && bestTicks >= 0 && _intended.Side == index && _intended.Slot == team.SlotNumber[best])
            {
                _receiverMeets = true;
            }
        }
    }

    /// <summary>
    /// Gets how hard a man runs to meet a ball: just fast enough to be there when it is, with a margin, and never slower than a jog. He
    /// who has time walks onto the ball and does not sprint to it and wait.
    /// </summary>
    /// <param name="body">The man.</param>
    /// <param name="profile">His athletic limits.</param>
    /// <param name="distanceSquared">The squared distance to the meeting point, in fixed units.</param>
    /// <param name="ticks">The ticks until the ball is there.</param>
    private static int MeetingPace(in TickPlayerState body, in TickPlayerProfile profile, long distanceSquared, int ticks)
    {
        var top = Math.Max(1, TickPlayerPhysics.EffectiveTopSpeed(body, profile));
        var needed = SpatialMath.Sqrt(distanceSquared) * MeetingMarginPercent / 100 / Math.Max(1, ticks);

        return (int)Math.Clamp(needed * 10_000 / top, MeetingMinimumPace, TickOffBallSupport.RunPaceBasisPoints);
    }

    /// <summary>The margin a man keeps on the speed he needs to meet a ball, in percent.</summary>
    private const int MeetingMarginPercent = 125;

    /// <summary>The slowest pace, in basis points of top speed, a man goes to meet a ball at: a jog.</summary>
    private const int MeetingMinimumPace = 4_000;

    /// <summary>The ticks' start the man a pass was meant for has over a teammate in being sent to meet it.</summary>
    private const int IntendedHeadStartTicks = 2;

    /// <summary>
    /// The man a pass is about to reach decides what he will do with it before it arrives, from where he will be: the decision is his on
    /// the first tick he has the ball, so the touch can be the pass (or the shot) and not the stop before it.
    /// </summary>
    private void PrepareReceiver()
    {
        if (!_passPending || !_receiverMeets || _receiverReach < 0 || _receiverReach > EarlyDecisionTicks)
        {
            return;
        }

        var side = _intended.Side;
        var team = _teams[side];
        var seat = team.SeatOfSlot(_intended.Slot);

        if (seat < 0 || team.IsKeeper(seat))
        {
            return;
        }

        var count = team.Count;
        var bodies = _projected.AsSpan(0, count);

        team.Body.AsSpan(0, count).CopyTo(bodies);
        bodies[seat].X = TickSpatialUnits.ToFixed(_receiverPoint.X);
        bodies[seat].Y = TickSpatialUnits.ToFixed(_receiverPoint.Y);

        _early = DecideFor(side, seat, bodies, 0);
        _earlyOwner = _intended;
        _earlyTick = _tick;
    }

    /// <summary>How near the ball, in ticks, the man it is coming to decides what to do with it.</summary>
    private const int EarlyDecisionTicks = 4;

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
                team.Intent[0] = TickShotStopper.DiveIntent(keeper, _shotFreeKick ? team.Skills[0].Uncurved() : team.Skills[0], _forecast, _shotTicks);
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
                PassedToHim = ((_passPending && _intended == new TickRef(index, team.SlotNumber[0])) || _recycledTo == new TickRef(index, team.SlotNumber[0])),
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
            _dribbleKey = NoDribble;

            if (_earlyOwner == me && _tick - _earlyTick <= EarlyDecisionTicks)
            {
                // He decided while the ball was on its way: that is his decision for the first ticks he has it.
                _decision = _early;
                _decided = true;
                _decisionTick = _tick;
            }

            _earlyOwner = TickRef.None;
        }

        _heldTicks++;

        var keeper = team.IsKeeper(seat);
        var skills = team.Skills[seat];
        var instructions = team.Runtime.Instructions;
        var breaking = team.IsBreaking(_tick);
        var interval = TickBallCarrierBrain.DecisionInterval(skills, instructions.Tempo, breaking);

        if (keeper && _heldTicks < KeeperHoldTicks + (team.Runtime.WastingTime ? KeeperWastedTicks : 0))
        {
            return;
        }

        if (!_decided || _tick - _decisionTick >= interval)
        {
            _decision = DecideFor(side, seat, team.Body.AsSpan(0, team.Count), _shieldTicks);
            _decided = true;
            _decisionTick = _tick;
            _shieldTicks = _decision.Action == TickCarrierAction.Shield ? _shieldTicks + interval : 0;
        }

        switch (_decision.Action)
        {
            case TickCarrierAction.Dribble:
                team.Anchor[seat] = _decision.Target;
                team.Pace[seat] = _decision.PaceBasisPoints;
                NoteDribble(side, seat);
                break;

            case TickCarrierAction.Shield:
                _dribbleKey = NoDribble;
                team.Anchor[seat] = new SpatialPoint(TickSpatialUnits.ToUnits(team.Body[seat].X), TickSpatialUnits.ToUnits(team.Body[seat].Y));
                team.Pace[seat] = 0;
                break;

            default:
                _dribbleKey = NoDribble;

                if (_heldTicks >= HoldTicks(_decision, skills, instructions.Tempo, keeper, breaking))
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

    /// <summary>
    /// Tells the commentary what a dribbling carrier means to do, once per run: a defender within four metres ahead of him is a take-on,
    /// and open ground ahead past halfway is a break. A plain carry is not told. Recording only: it reads the bodies, draws nothing and
    /// changes no state the match reads.
    /// </summary>
    private void NoteDribble(int side, int seat)
    {
        if (_recording is null)
        {
            return;
        }

        var team = _teams[side];
        var opponents = _teams[1 - side];
        var carrierX = TickSpatialUnits.ToUnits(team.Body[seat].X);
        var carrierY = TickSpatialUnits.ToUnits(team.Body[seat].Y);
        long aheadX = _decision.Target.X - carrierX;
        long aheadY = _decision.Target.Y - carrierY;
        var nearest = -1;
        var nearestSquared = long.MaxValue;
        var blocked = false;

        for (var other = 0; other < opponents.Count; other++)
        {
            if (opponents.IsKeeper(other))
            {
                continue;
            }

            long dx = TickSpatialUnits.ToUnits(opponents.Body[other].X) - carrierX;
            long dy = TickSpatialUnits.ToUnits(opponents.Body[other].Y) - carrierY;

            if ((dx * aheadX) + (dy * aheadY) <= 0)
            {
                continue;
            }

            var squared = (dx * dx) + (dy * dy);

            blocked |= squared <= (long)BreakClearUnits * BreakClearUnits;

            if (squared <= (long)TakeOnUnits * TakeOnUnits && squared < nearestSquared)
            {
                nearestSquared = squared;
                nearest = other;
            }
        }

        var pastHalfway = team.IsHome ? carrierX > SpatialPitch.PitchLength / 2 : carrierX < SpatialPitch.PitchLength / 2;
        var key = nearest >= 0 ? opponents.Entity(nearest) : (!blocked && pastHalfway ? BreakAway : PlainCarry);

        if (key == _dribbleKey)
        {
            return;
        }

        _dribbleKey = key;

        var me = new TickRef(side, team.SlotNumber[seat]);
        var entity = EntityOf(me);

        if (key == PlainCarry || _tick - _dribbleStampTick[entity] < DribbleStampGapTicks)
        {
            return;
        }

        _dribbleStampTick[entity] = _tick;

        if (key == BreakAway)
        {
            Tag(me, PassageAction.Break);
        }
        else
        {
            Tag(me, PassageAction.TakeOn, new TickRef(1 - side, opponents.SlotNumber[nearest]));
        }
    }

    /// <summary>Chooses what a man does with the ball from the bodies as they stand (or are projected to stand) on a tick.</summary>
    private TickCarrierDecision DecideFor(int side, int seat, ReadOnlySpan<TickPlayerState> bodies, int shieldTicks)
    {
        var team = _teams[side];
        var opponents = _teams[1 - side];
        var instructions = team.Runtime.Instructions;
        var count = team.Count;

        return TickBallCarrierBrain.Decide(new TickCarrierSituation
        {
            IsHome = team.IsHome,
            Mentality = instructions.Mentality,
            Tempo = instructions.Tempo,
            Passing = instructions.Passing,
            Focus = instructions.PassFocus,
            Attackers = bodies,
            Specs = team.Spec.AsSpan(0, count),
            Skills = team.Skills.AsSpan(0, count),
            Defenders = opponents.Body.AsSpan(0, opponents.Count),
            CarrierIndex = seat,
            Orders = team.AttackOrders.AsSpan(0, count),
            ShieldTicks = shieldTicks,
            Breaking = team.IsBreaking(_tick),
            BreakProgress = team.IsBreaking(_tick) ? team.BreakProgressPercent : 0,
            EdgeFactorPercent = team.AttackEdgeFactorPercent,
        });
    }

    /// <summary>The pace a man carries the ball at while he waits to play it, in basis points of top speed.</summary>
    private const int TickHoldPace = 4_000;

    /// <summary>The Technique from which a man plays first time, with the ball hardly under his feet.</summary>
    private const int OneTouchTechnique = 12;

    /// <summary>The pressure, in basis points, from which a man with the Technique plays first time.</summary>
    private const int OneTouchPressure = 7_000;

    /// <summary>The ticks a man takes over his first touch before a pass or a cross, however free he is (0.3 s).</summary>
    private const int FirstTouchTicks = 3;

    /// <summary>
    /// The ticks a man with nobody near him adds to his first touch before he plays, at an even tempo: he looks up and carries the ball. The
    /// dwell falls in proportion to the pressure on him and is nothing at <see cref="DwellScale"/>.
    /// </summary>
    private const int DwellTicks = 60;

    /// <summary>The pressure, in basis points, at which a man has no time to dwell.</summary>
    private const int DwellScale = 8_000;

    /// <summary>The share of his dwell a man on a break keeps, in percent: he looks up and plays at once, with the other side still running back.</summary>
    private const int BreakDwellPercent = 30;

    /// <summary>The longest a man keeps the ball before he plays it when nothing forces him (4 s).</summary>
    private const int MaximumDwellTicks = 40;

    /// <summary>
    /// The ticks a man keeps the ball before a through ball leaves his foot (0.1 s): a run behind the line is on for a moment only, and a
    /// man who waits for four seconds to look up is no longer playing the ball to the man who ran.
    /// </summary>
    private const int ThroughBallHoldTicks = 1;

    /// <summary>
    /// Gets how long a man keeps the ball before the kick he has chosen leaves his foot: his first touch (three tenths of a second) and,
    /// while nobody is on him, the time he takes to look up and carry the ball, which shrinks to nothing as the pressure rises to
    /// <see cref="DwellScale"/>. A man with the Technique plays first time, with a single tick on the ball, when the pressure reaches
    /// <see cref="OneTouchPressure"/> or he shoots. A shot takes its three tenths; a clearance is a flick; a through ball is played on the
    /// first tick (<see cref="ThroughBallHoldTicks"/>), however free he is. A goalkeeper has already waited.
    /// </summary>
    internal static int HoldTicks(in TickCarrierDecision decision, in TickPlayerSkills skills, MatchTempo tempo, bool keeper, bool breaking = false)
    {
        if (keeper)
        {
            return 0;
        }

        if (decision.Action == TickCarrierAction.ThroughBall)
        {
            return ThroughBallHoldTicks;
        }

        var firstTime = skills.Technique >= OneTouchTechnique
            && (decision.Action == TickCarrierAction.Shoot || decision.EffectivePressure >= OneTouchPressure);

        if (firstTime)
        {
            return 1;
        }

        switch (decision.Action)
        {
            case TickCarrierAction.Shoot:
                return 3;

            case TickCarrierAction.Clear:
                return 1;
        }

        var free = Math.Max(0, DwellScale - decision.EffectivePressure);
        var dwell = DwellTicks * free / DwellScale;

        dwell = tempo switch
        {
            MatchTempo.High => dwell * 6 / 10,
            MatchTempo.Low => dwell * 14 / 10,
            _ => dwell,
        };

        if (breaking)
        {
            dwell = dwell * BreakDwellPercent / 100;
        }

        return FirstTouchTicks + Math.Min(dwell, MaximumDwellTicks);
    }

    private void Kick(int side, int seat, in TickCarrierDecision decision)
    {
        var team = _teams[side];
        var origin = new SpatialPoint(_ball.UnitX, _ball.UnitY);
        var kicked = decision;

        if (decision.Action == TickCarrierAction.Shoot)
        {
            // Bodies between the shooter and the goal spoil the strike; his choice to shoot was already made with them in mind.
            var opponents = _teams[1 - side];
            var keeper = -1;

            for (var other = 0; other < opponents.Count && keeper < 0; other++)
            {
                keeper = opponents.IsKeeper(other) ? other : -1;
            }

            var cover = TickBallCarrierBrain.ShotCover(
                TickSpatialUnits.ToUnits(team.Body[seat].X),
                TickSpatialUnits.ToUnits(team.Body[seat].Y),
                team.IsHome,
                opponents.Body.AsSpan(0, opponents.Count),
                keeper);

            kicked = decision with { EffectivePressure = decision.EffectivePressure + (TickBallCarrierBrain.ShotCoverPressure * cover) };
        }

        if (!TickBallCarrierBrain.Execute(kicked, team.Skills[seat], _ball, _random))
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
        _earlyOwner = TickRef.None;
        _offsidePending = false;
        _passPending = false;
        _passIsCross = false;
        Touch(me);

        if (EndsBreak(decision.Action))
        {
            // A shot, a ball played back and a hoof end the break: it is over once the side stops going forward.
            team.BreakUntilTick = -1;
        }

        switch (decision.Action)
        {
            case TickCarrierAction.Shoot:
                {
                    var penalty = fromRestart && kind == TickRestartKind.Penalty;
                    var freeKick = fromRestart && kind == TickRestartKind.FreeKick;

                    var goal = new SpatialPoint(team.IsHome ? SpatialPitch.PitchLength : 0, SpatialPitch.GoalYCenter);
                    var longRange = !penalty && !freeKick && SpatialMath.Distance(origin, goal) > LongRangeShotUnits;

                    Tag(
                        me,
                        penalty ? PassageAction.Penalty : freeKick ? PassageAction.FreeKick : PassageAction.Shot,
                        detail: longRange ? TickActionDetail.LongRange : TickActionDetail.None);
                    StartShot(side, seat, decision, origin, penalty, freeKick);
                    break;
                }

            case TickCarrierAction.Pass or TickCarrierAction.ThroughBall or TickCarrierAction.Cross:
                {
                    var id = team.Id[seat];

                    _passPending = true;
                    _passTried = 0;
                    _passer = me;
                    _passAim = decision.Target;
                    _intended = decision.Receiver >= 0 && decision.Receiver < team.Count
                        ? new TickRef(side, team.SlotNumber[decision.Receiver])
                        : TickRef.None;

                    team.Runtime.PassesAttempted[id] = team.Runtime.PassesAttempted.GetValueOrDefault(id) + 1;

                    if (decision.Action == TickCarrierAction.Cross && !(fromRestart && kind == TickRestartKind.Corner))
                    {
                        _passIsCross = true;
                        team.Runtime.CrossesAttempted[id] = team.Runtime.CrossesAttempted.GetValueOrDefault(id) + 1;
                    }

                    var passDetail = decision.Action switch
                    {
                        TickCarrierAction.ThroughBall => TickActionDetail.ThroughBall,
                        TickCarrierAction.Pass when SpatialMath.Distance(origin, decision.Target) >= LongBallUnits => TickActionDetail.LongBall,
                        _ => TickActionDetail.None,
                    };

                    Tag(me, decision.Action == TickCarrierAction.Cross ? PassageAction.Cross : PassageAction.Pass, _intended, passDetail);

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

                    if (!fromRestart
                        && decision.Action == TickCarrierAction.Pass
                        && decision.Receiver > 0
                        && decision.Receiver < team.Count
                        && decision.Receiver != seat)
                    {
                        StartCombinations(side, seat, decision.Receiver);
                    }

                    break;
                }

            default:
                {
                    var receiver = decision.Receiver >= 0 && decision.Receiver < team.Count
                        ? new TickRef(side, team.SlotNumber[decision.Receiver])
                        : TickRef.None;

                    Tag(
                        me,
                        PassageAction.Pass,
                        decision.Action == TickCarrierAction.Clear ? null : receiver,
                        decision.Action == TickCarrierAction.Clear ? TickActionDetail.Clear : TickActionDetail.Recycle);

                    if (decision.Action == TickCarrierAction.Recycle && !receiver.IsNone)
                    {
                        _recycledTo = receiver;
                    }

                    break;
                }
        }

        PredictRest();
    }

    private void Touch(TickRef who)
    {
        _recycledTo = TickRef.None;
        _lastTouch = who;
        _lastTouchBySide[who.Side] = who;
    }

    /// <summary>Gives a man the ball at his feet; a ball that was played to him is eased to his foot, not snapped.</summary>
    private void Attach(int side, int seat, bool cushioned = false)
    {
        var team = _teams[side];

        _ball.Attach(seat, cushioned);
        _controllerSide = side;
        Touch(new TickRef(side, team.SlotNumber[seat]));
    }

    // ---- Moving the bodies -------------------------------------------------------------------------------------------------------------

    private void MoveTeams()
    {
        // In a dead ball nobody walks about and the opponents are not kept clear of: a set piece is set where its plan puts the men.
        var live = !_machine.IsDeadBall;

        for (var side = 0; side < _teams.Length; side++)
        {
            var team = _teams[side];
            var other = _teams[1 - side];
            var count = team.Count;
            var bodies = team.Body.AsSpan(0, count);
            var opponents = other.Body.AsSpan(0, other.Count);

            for (var seat = 0; seat < count; seat++)
            {
                if (team.HasIntent[seat])
                {
                    continue;
                }

                var walks = live && team.UseMicro[seat];
                var micro = default(SpatialPoint);
                var microPace = team.MicroPace[seat];

                if (walks)
                {
                    var anchor = team.Anchor[seat];
                    var offset = team.MicroOffset[seat];

                    if (!team.HasOffset[seat])
                    {
                        // Nothing to look for or cover: he drifts about his place, a few steps one way and then another.
                        offset = Drift(side, seat);
                        microPace = 0;
                    }

                    micro = new SpatialPoint(
                        Math.Clamp(anchor.X + offset.X, TickTacticalGeometry.Margin, SpatialPitch.PitchLength - TickTacticalGeometry.Margin),
                        Math.Clamp(anchor.Y + offset.Y, TickTacticalGeometry.Margin, SpatialPitch.PitchWidth - TickTacticalGeometry.Margin));

                    if (side == _possessionSide)
                    {
                        micro = TickOffBallSupport.ClampOnside(micro, team.IsHome, _ball.UnitX, opponents);
                    }
                }

                var context = new TickSteerContext
                {
                    Opponents = live ? opponents : default,
                    Engaged = !live || team.Engaged[seat],
                    HasMicro = walks,
                    Micro = micro,
                    MicroPaceBasisPoints = microPace,
                };

                team.Intent[seat] = TickSteering.Steer(seat, bodies, team.Profile[seat], team.Anchor[seat], team.Pace[seat], context);
            }
        }

        foreach (var team in _teams)
        {
            var count = team.Count;
            var drain = TickPlayerPhysics.DrainPercent(team.Runtime.Instructions);

            for (var seat = 0; seat < count; seat++)
            {
                TickPlayerPhysics.Step(ref team.Body[seat], team.Profile[seat], team.Intent[seat], drain);
            }
        }
    }

    /// <summary>The ticks a man drifts one way before he turns to another (2 s).</summary>
    private const int DriftTicks = 20;

    /// <summary>The steps of a drift, in pitch units: eight directions 2 m from his place.</summary>
    private static readonly SpatialPoint[] DriftSteps =
    [
        new(190, 0), new(134, 134), new(0, 190), new(-134, 134), new(-190, 0), new(-134, -134), new(0, -190), new(134, -134),
    ];

    /// <summary>Gets where a man with nothing to do drifts to, as an offset from his place: it changes every two seconds, at a different tick for each seat.</summary>
    private SpatialPoint Drift(int side, int seat) =>
        DriftSteps[(((_tick + (7 * seat) + (3 * side)) / DriftTicks * 3) + seat + side) & 7];

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
                Tackled(opponents, challengerId, won: true);
                Dribbled(team, carrierId, completed: false);
                Rate(opponents, challengerId, _rules.LiveRatingTackleBonusBasisPoints);
                Rate(team, carrierId, -_rules.LiveRatingTackleLostPenaltyBasisPoints);
                Tag(challengerRef, PassageAction.Tackle, carrierRef, TickActionDetail.TackleWon);
                Tag(carrierRef, PassageAction.Dispossessed, challengerRef);
                _controllerSide = 1 - side;
                Touch(challengerRef);
                TurnOver();
                OpenBreak(1 - side);
                break;

            case TickTackleOutcome.PokedLoose:
                Tackled(opponents, challengerId, won: true);
                Dribbled(team, carrierId, completed: false);
                Rate(opponents, challengerId, _rules.LiveRatingTackleBonusBasisPoints);
                Rate(team, carrierId, -_rules.LiveRatingTackleLostPenaltyBasisPoints);
                Tag(challengerRef, PassageAction.Tackle, carrierRef, TickActionDetail.PokedLoose);
                Tag(carrierRef, PassageAction.Dispossessed, challengerRef);
                _controllerSide = -1;
                Touch(challengerRef);
                TurnOver();
                PredictRest();
                break;

            case TickTackleOutcome.Beaten:
                Tackled(opponents, challengerId, won: false);
                Dribbled(team, carrierId, completed: true);
                Rate(team, carrierId, _rules.LiveRatingTackleBonusBasisPoints);
                Rate(opponents, challengerId, -_rules.LiveRatingTackleLostPenaltyBasisPoints);
                Tag(carrierRef, PassageAction.Carry, challengerRef);
                TagBlame(challengerRef, PassageAction.Beaten);
                break;

            default:
                Tackled(opponents, challengerId, won: false);
                Rate(opponents, challengerId, -_rules.LiveRatingTackleLostPenaltyBasisPoints);
                Tag(challengerRef, PassageAction.Tackle, carrierRef);
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
        var attackerRef = new TickRef(side, team.SlotNumber[seat]);

        switch (outcome)
        {
            case TickSmotherOutcome.Claimed:
                Dribbled(team, attackerId, completed: false);
                Rate(keeperTeam, keeperId, _rules.LiveRatingTackleBonusBasisPoints);
                _controllerSide = 1 - side;
                Touch(keeperRef);
                Tag(keeperRef, PassageAction.Save, attackerRef, TickActionDetail.Smothered);
                TurnOver();
                break;

            case TickSmotherOutcome.Spilled:
                Dribbled(team, attackerId, completed: false);
                _controllerSide = -1;
                Touch(keeperRef);
                Tag(keeperRef, PassageAction.Save, attackerRef, TickActionDetail.Spilled);
                TurnOver();
                PredictRest();
                break;

            case TickSmotherOutcome.Beaten:
                Dribbled(team, attackerId, completed: true);
                Tag(attackerRef, PassageAction.Carry, keeperRef);
                Tag(keeperRef, PassageAction.Dive);
                TagBlame(keeperRef, PassageAction.Beaten);
                break;

            default:
                FoulBy(1 - side, keeperId, side, spot);
                break;
        }
    }

    // ---- Who erred, for the film ------------------------------------------------------------------------------------------------------

    /// <summary>The ticks a man is not blamed again for after he has been shown beaten or bypassed (3 s).</summary>
    private const int BlameCooldownTicks = 30;

    /// <summary>How near the carrier a defender must be, on the goal side of him, to be watched (3 m).</summary>
    private const int BypassReach = 300;

    /// <summary>How long the carrier must keep the ball, running on, before a defender left behind is called bypassed (1 s).</summary>
    private const int BypassMinTicks = 10;

    /// <summary>How long a defender is watched before he is let off (3 s).</summary>
    private const int BypassMaxTicks = 30;

    /// <summary>How far behind the carrier the defender must be left, in pitch units (1 m).</summary>
    private const int BypassBehindUnits = 100;

    /// <summary>
    /// Stamps a man the carrier got the better of, unless he has been stamped within the last few seconds: a defender who is beaten
    /// tick after tick in one dribble is shown beaten once. Recording only: it draws nothing and changes no state.
    /// </summary>
    private void TagBlame(TickRef who, PassageAction action)
    {
        if (_recording is null)
        {
            return;
        }

        var entity = EntityOf(who);

        if (entity < 0 || _tick - _blameTick[entity] < BlameCooldownTicks)
        {
            return;
        }

        _blameTick[entity] = _tick;
        _recording.AddAction(entity, action);
    }

    /// <summary>A pass that did not find its man: the passer is stamped, at the moment the ball is cut out or leaves the pitch.</summary>
    private void TagMisplacedPass()
    {
        if (_passPending && !_passer.IsNone)
        {
            Tag(_passer, PassageAction.Misplaced);
        }
    }

    /// <summary>
    /// Watches the man with the ball for a defender he runs past: one who was on the goal side of him within three metres and, a second
    /// or more later, is behind him with the carrier still on the ball and no challenge made. That man was outrun or turned, which is
    /// the error a manager wants to see. Runs only while a film is being recorded and reads nothing but the bodies.
    /// </summary>
    private void TrackBypassed()
    {
        if (_ball.Mode != TickBallMode.Controlled || _controllerSide < 0 || _ball.ControllerIndex >= _teams[_controllerSide].Count)
        {
            Array.Fill(_bypassCarrier, -1);

            return;
        }

        var team = _teams[_controllerSide];
        var opponents = _teams[1 - _controllerSide];
        var carrier = team.Body[_ball.ControllerIndex];
        var carrierEntity = team.Entity(_ball.ControllerIndex);
        var forward = team.IsHome ? 1L : -1L;
        var reach = (long)TickSpatialUnits.ToFixed(BypassReach);
        var behind = (long)TickSpatialUnits.ToFixed(BypassBehindUnits);

        for (var seat = 0; seat < opponents.Count; seat++)
        {
            if (opponents.IsKeeper(seat))
            {
                continue;
            }

            var entity = opponents.Entity(seat);
            long ahead = (opponents.Body[seat].X - carrier.X) * forward;
            long dx = opponents.Body[seat].X - carrier.X;
            long dy = opponents.Body[seat].Y - carrier.Y;

            if (_bypassCarrier[entity] == carrierEntity)
            {
                var watched = _tick - _bypassSince[entity];

                if (ahead <= -behind && watched >= BypassMinTicks)
                {
                    _bypassCarrier[entity] = -1;
                    TagBlame(new TickRef(1 - _controllerSide, opponents.SlotNumber[seat]), PassageAction.Bypassed);

                    continue;
                }

                if (watched < BypassMaxTicks)
                {
                    continue;
                }
            }

            _bypassCarrier[entity] = ahead > 0 && (dx * dx) + (dy * dy) <= reach * reach ? carrierEntity : -1;
            _bypassSince[entity] = _tick;
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

    /// <summary>A defender went in for the ball in the carrier duel: it is a tackle attempted, and a tackle won when he took the ball or poked it loose.</summary>
    private static void Tackled(TickTeam team, Guid id, bool won)
    {
        var runtime = team.Runtime;

        runtime.TacklesAttempted[id] = runtime.TacklesAttempted.GetValueOrDefault(id) + 1;

        if (won)
        {
            runtime.TacklesWon[id] = runtime.TacklesWon.GetValueOrDefault(id) + 1;
        }
    }

    private static void Rate(TickTeam team, Guid id, int delta) => team.Runtime.AdjustLiveRating(id, delta);

    /// <summary>The farthest up the pitch the ball may be won for a break, in the winning side's own point of view (its own 60%).</summary>
    private const int BreakZoneUnits = SpatialPitch.PitchLength * 6 / 10;

    /// <summary>
    /// A side has cut out a pass or won a tackle: if it counter-attacks and won the ball in its own 60% it is on a break
    /// (`weaker-side-v1`, Milestone 1) whose length, runners and start are set by <see cref="TickBreakGain.Plan"/> from the opponent's posture and
    /// the two sides' speed (Milestone 1b). A Defensive side opens one too, with or without the flag, and gets at least the outlet of
    /// <see cref="TickBreakGain.WithOutlet"/>: one runner and a mild lift for the ball won in its own 60% (Milestone 2). It plays forward until a ball goes back, a shot is taken or the ball goes dead
    /// (<see cref="CloseBreak"/>), or the other side wins it back (<see cref="TurnOver"/>). A loose ball picked up does not open one.
    /// </summary>
    private void OpenBreak(int side)
    {
        var team = _teams[side];

        var instructions = team.Runtime.Instructions;
        var outlet = instructions.Mentality == MatchMentality.Defensive;
        var opponent = _teams[1 - side];
        var opponentPosture = TickTeamStyle.Posture(opponent.Runtime.Instructions);
        var exposed = TickBreakGain.IsExposed(instructions.CounterAttack, opponentPosture);

        if (!OpensBreak(instructions.CounterAttack || outlet || exposed, team.IsHome, _ball.UnitX))
        {
            return;
        }

        var edge = TickBreakGain.GroupSpeed(opponent, MatchPositionFamily.Defence, MatchPositionFamily.Midfield)
            - TickBreakGain.GroupSpeed(team, MatchPositionFamily.Attack, MatchPositionFamily.Midfield);
        var full = instructions.CounterAttack || exposed
            ? TickBreakGain.Plan(opponentPosture, TickBreakGain.SpeedFactorPercent(edge))
            : default;
        var plan = exposed ? TickBreakGain.Exposed(full, TickBreakGain.ExposedBreakPercent) : full;

        if (outlet)
        {
            plan = TickBreakGain.WithOutlet(plan);
        }

        team.BreakUntilTick = _tick + plan.Ticks;
        team.BreakRunners = plan.Runners;
        team.BreakImmediate = plan.Immediate;
        team.BreakProgressPercent = plan.ProgressPercent;
    }

    /// <summary>Gets whether a ball cut out or tackled away opens a break: the side can break (the counter flag, a Defensive outlet or an opponent committed forward) and won it in its own 60%.</summary>
    /// <param name="opens">Whether the side's instructions, or the opponent's posture, allow a break.</param>
    /// <param name="isHome">Whether the side is the home side.</param>
    /// <param name="ballX">The X the ball was won at, in absolute pitch units.</param>
    internal static bool OpensBreak(bool opens, bool isHome, int ballX) =>
        opens && (isHome ? ballX : SpatialPitch.PitchLength - ballX) <= BreakZoneUnits;

    /// <summary>Gets whether a kick ends the break: a shot, a ball played back and a hoof are the side no longer going forward.</summary>
    /// <param name="action">What the man with the ball did.</param>
    internal static bool EndsBreak(TickCarrierAction action) =>
        action is TickCarrierAction.Shoot or TickCarrierAction.Recycle or TickCarrierAction.Clear;

    /// <summary>The break of both sides is over.</summary>
    private void CloseBreak()
    {
        foreach (var team in _teams)
        {
            team.BreakUntilTick = -1;
        }
    }

    /// <summary>The ball has changed hands: the old side's movement is forgotten and the new carrier decides afresh.</summary>
    private void TurnOver()
    {
        CloseBreak();

        foreach (var team in _teams)
        {
            team.SupporterMask = 0;
            team.RunnerMask = 0;
            team.PreviousPresser = -1;
            team.UnpressedTicks = 0;
        }

        _decisionOwner = TickRef.None;
        _assistPasser = TickRef.None;
        _assistReceiver = TickRef.None;
        _passPending = false;
        _passIsCross = false;
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
                var lane = _passPending && index != _passer.Side && _ball.UnitZ <= TickBallPhysics.GroundContactZUnits;

                if (fast || lane)
                {
                    distance = TickOffBallSupport.SegmentDistanceSquared(body.X, body.Y, previousX, previousY, _ball.X, _ball.Y);
                }
                else
                {
                    long dx = _ball.X - body.X;
                    long dy = _ball.Y - body.Y;

                    distance = (dx * dx) + (dy * dy);
                }

                var reach = lane ? Math.Max(radius, (long)TickSpatialUnits.ToFixed(TickOffBallSupport.InterceptReach)) : radius;

                if (distance <= reach * reach && distance < best)
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
        var intercepted = _passPending && side != _passer.Side;
        var looseBall = !_passPending && _recycledTo != me;

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

                    if (_passIsCross)
                    {
                        passerTeam.Runtime.CrossesCompleted[passerId] = passerTeam.Runtime.CrossesCompleted.GetValueOrDefault(passerId) + 1;
                    }
                }

                Tag(me, PassageAction.Receive, _passer);
                _assistPasser = _passer;
                _assistReceiver = me;
                _assistTick = _tick;
            }
            else if (side != _passer.Side)
            {
                Tag(me, PassageAction.Interception, _passer);
                TagMisplacedPass();
            }

            _passPending = false;
            _passIsCross = false;
        }
        else if (looseBall)
        {
            Tag(me, PassageAction.Receive, detail: TickActionDetail.LooseBall);
        }

        Attach(side, seat, cushioned: !team.IsKeeper(seat));

        if (changedSide)
        {
            TurnOver();

            if (intercepted && !team.IsKeeper(seat))
            {
                OpenBreak(side);
            }
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
            Tag(new TickRef(side, team.SlotNumber[seat]), PassageAction.Interception, _shooter, TickActionDetail.Blocked);
            _controllerSide = -1;
            _passPending = false;
            _passIsCross = false;
            Touch(new TickRef(side, team.SlotNumber[seat]));
            TurnOver();
            PredictRest();

            return;
        }

        _ball.Kick(
            (int)(TickTrigonometry.Cos(heading) * speed / TickTrigonometry.Scale),
            (int)(TickTrigonometry.Sin(heading) * speed / TickTrigonometry.Scale),
            0);

        Tag(blocker, PassageAction.Interception, _shooter, TickActionDetail.Blocked);
        _controllerSide = -1;
        _passPending = false;
        _passIsCross = false;
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
