using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>What a defender is doing this tick.</summary>
internal enum TickDefensiveRole
{
    /// <summary>Holding his shape position: nothing about the play asks more of him.</summary>
    Holding = 0,

    /// <summary>The goalkeeper, who is steered by his own milestone and here simply keeps his anchor.</summary>
    Keeper = 1,

    /// <summary>A member of the back line, standing on the line's shared height.</summary>
    Line = 2,

    /// <summary>The one defender closing the ball down.</summary>
    Presser = 3,

    /// <summary>The second defender of a high press, trapping the ball against the touchline.</summary>
    SupportPresser = 4,

    /// <summary>A defender goal-side of an attacker in the defensive third.</summary>
    Marker = 5,

    /// <summary>A player standing in the passing lane between the ball and a receiver (a cover shadow).</summary>
    Screen = 6,
}

/// <summary>
/// One defender's job and where it sends him.
/// </summary>
/// <param name="Role">What he is doing.</param>
/// <param name="Target">The point he is to go to, in pitch units.</param>
/// <param name="PaceBasisPoints">The share of his top speed to travel at, or 0 for the holding pace.</param>
/// <param name="Opponent">The index of the attacker he is marking or screening, or -1.</param>
internal readonly record struct TickDefensiveOrder(TickDefensiveRole Role, SpatialPoint Target, int PaceBasisPoints, int Opponent);

/// <summary>
/// Everything the defensive AI reads about one moment of play. Index 0 of both sides is the goalkeeper.
/// </summary>
internal readonly ref struct TickDefensiveSituation
{
    /// <summary>Gets a value indicating whether the defending side is the home side (defending X = 0).</summary>
    public required bool IsHome { get; init; }

    /// <summary>Gets the defending side's pressing instruction.</summary>
    public required MatchPressing Pressing { get; init; }

    /// <summary>Gets the defending side's players, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Defenders { get; init; }

    /// <summary>Gets the defending side's board positions, in the same order (they carry the position family).</summary>
    public required ReadOnlySpan<TickAnchorSpec> Specs { get; init; }

    /// <summary>Gets the defending side's dynamic anchors for this tick, in the same order, in pitch units.</summary>
    public required ReadOnlySpan<SpatialPoint> Anchors { get; init; }

    /// <summary>Gets the defending side's skills, in the same order.</summary>
    public required ReadOnlySpan<TickPlayerSkills> Skills { get; init; }

    /// <summary>Gets the attacking side's players, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Attackers { get; init; }

    /// <summary>Gets the ball's X, in pitch units.</summary>
    public required int BallX { get; init; }

    /// <summary>Gets the ball's Y, in pitch units.</summary>
    public required int BallY { get; init; }

    /// <summary>Gets the attacker with the ball at his feet, or -1 when the ball is loose or in the air.</summary>
    public required int CarrierIndex { get; init; }

    /// <summary>
    /// Gets the defender who pressed last tick, or -1. He keeps the job unless someone is clearly closer, so two
    /// defenders at the same distance do not trade the job every tick.
    /// </summary>
    public required int PreviousPresser { get; init; }
}

/// <summary>
/// Disciplined zonal defending for the tick engine: who presses, who screens, who marks, where the line stands and who is
/// offside (`tick-engine-v1`, Milestone 3).
/// </summary>
/// <remarks>
/// <para>
/// Each tick <see cref="Assign"/> gives every defender exactly one job, in this order, each job taking players the ones
/// before it have not:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Press.</b> The nearest outfield defender inside the press radius closes the ball down: 12 m under a low block (and
/// only while the ball is in the defender's own half), 20 m under a mid block (not in the opponent's final third), 32 m
/// under a high press. A defender who is off balance after a challenge does not press. Only <em>one</em> player presses.
/// A second does so only under a high press when the ball is within 10 m of a touchline, to trap it there. Everyone else
/// is forbidden from charging the ball, which is what stops the schoolyard swarm.
/// </description></item>
/// <item><description>
/// <b>Mark.</b> A defender (back-line family) not pressing takes the nearest unmarked attacker inside the defensive
/// third, within 15 m, and stands goal-side of him: 5.7 m away at Marking 1, 2.7 m at Marking 20 (the plan's tight mark
/// is under 3 m). The pairs are picked nearest first across the whole back line, so the choice does not depend on the
/// order the players are listed in.
/// </description></item>
/// <item><description>
/// <b>Screen.</b> Midfielders and forwards not otherwise used stand in the passing lanes: for each of the three most
/// dangerous receivers within range (deepest first, between 14 m and 40 m from the ball), the nearest free player takes
/// the point 40% of the way from the ball to the receiver (never closer than 7 m to the ball) and keeps to it in
/// proportion to his Positioning (52% at 1, 100% at 20, the rest of the pull being his own shape position).
/// </description></item>
/// <item><description>
/// <b>Hold the line.</b> The back line's players still holding stand on one shared height: the mean of their anchors,
/// stepped up by up to 2.5 m when the carrier is closed down within 6 m (the squeeze, scaled by their Decisions) and
/// dropped by 3 m when the carrier has time (no defender within 12 m and the ball in the defender's own 65%). It never
/// goes beyond the halfway line.
/// </description></item>
/// </list>
/// <para>
/// <see cref="IsOffside"/> and <see cref="FlagOffside"/> judge the offside the line sets: the second-last defender
/// (the goalkeeper counts, so it is normally the last outfield defender). Everything is integer arithmetic over spans and
/// a stack buffer, so a tick allocates nothing.
/// </para>
/// </remarks>
internal static class TickDefensiveAI
{
    /// <summary>The press radius of a low block, in pitch units (12 m).</summary>
    public const int LowBlockRadius = 1_200;

    /// <summary>The press radius of a mid block, in pitch units (20 m).</summary>
    public const int MidBlockRadius = 2_000;

    /// <summary>The press radius of a high press, in pitch units (32 m).</summary>
    public const int HighPressRadius = 3_200;

    /// <summary>The distance from a touchline within which a high press sends a second player (10 m), in pitch units.</summary>
    public const int TrapDistance = 950;

    /// <summary>The farthest up the pitch a mid block presses, in the defender's own X (the opponent's final third starts here).</summary>
    public const int MidBlockLimit = 7_000;

    /// <summary>
    /// The farthest up the pitch a high press presses, in the defender's own X: from about the edge of the opponent's penalty area, not
    /// at the goal kick. A ball that is played out cleanly and fast beats a press that chases it into the corner of the pitch and leaves
    /// the side stretched behind it, so an unlimited press was worth a goal and a half a match to the side it was played against.
    /// </summary>
    public const int HighPressLimit = 7_800;

    /// <summary>The own-X below which an attacker is in the defensive third, in pitch units.</summary>
    public const int DefensiveThird = 3_500;

    /// <summary>The farthest a marker looks for his man, in pitch units (15 m).</summary>
    public const int MarkingZone = 1_500;

    /// <summary>How closely a marker stands at Marking 1, in pitch units.</summary>
    public const int LooseMarkingStandOff = 570;

    /// <summary>How much closer a marker stands per point of Marking, in pitch units.</summary>
    public const int MarkingStandOffStep = 15;

    /// <summary>The nearest a screen stands to the ball, in pitch units (7 m).</summary>
    public const int MinimumScreenDistance = 700;

    /// <summary>The nearest receiver a screen is cast on, in pitch units (14 m): closer ones are in the presser's face.</summary>
    public const int MinimumScreenReach = 1_400;

    /// <summary>The farthest receiver a screen is cast on, in pitch units (40 m).</summary>
    public const int MaximumScreenReach = 4_000;

    /// <summary>The farthest a player goes to take up a screen, in pitch units.</summary>
    public const int ScreenerRange = 2_500;

    /// <summary>How many receivers one side screens at once.</summary>
    public const int MaximumScreens = 3;

    /// <summary>The share of the lane a screen stands along it, in percent.</summary>
    public const int ScreenLanePercent = 40;

    /// <summary>The pace of the presser, in basis points of top speed.</summary>
    public const int PresserPaceBasisPoints = 9_500;

    /// <summary>The pace of the second presser of a trap, in basis points of top speed.</summary>
    public const int SupportPresserPaceBasisPoints = 8_500;

    /// <summary>The pace of a marker, in basis points of top speed.</summary>
    public const int MarkerPaceBasisPoints = 8_000;

    /// <summary>The pace of a screen, in basis points of top speed.</summary>
    public const int ScreenPaceBasisPoints = 7_500;

    /// <summary>Within this distance of the ball a defender counts as closing the carrier down (6 m), in pitch units.</summary>
    public const int ClosedDownDistance = 600;

    /// <summary>Beyond this distance of the ball no defender is near the carrier (12 m), in pitch units.</summary>
    public const int FreeCarrierDistance = 1_200;

    /// <summary>The most the line steps up to squeeze a closed-down carrier, at Decisions 20, in pitch units.</summary>
    public const int LineStepUp = 250;

    /// <summary>How far the line drops when the carrier has time, in pitch units.</summary>
    public const int LineDropOff = 300;

    /// <summary>The ball's own X beyond which a free carrier no longer drops the line, in pitch units.</summary>
    public const int LineDropBallLimit = 6_500;

    /// <summary>The highest the line goes, in the defender's own X: the halfway line.</summary>
    public const int LineCeiling = 5_000;

    /// <summary>The deepest the line goes, in the defender's own X.</summary>
    public const int LineFloor = 1_300;

    /// <summary>How far a presser aims beyond the ball so that he runs onto it instead of stopping a metre short, in pitch units.</summary>
    public const int PressThrough = 100;

    /// <summary>How far in from the ball the second presser of a trap stands, in pitch units (3 m).</summary>
    public const int TrapInset = 300;

    private const int HysteresisPercent = 85;

    /// <summary>Gets the radius a pressing instruction closes the ball down from, in pitch units.</summary>
    /// <param name="pressing">The instruction.</param>
    public static int PressRadius(MatchPressing pressing) => pressing switch
    {
        MatchPressing.LowBlock => LowBlockRadius,
        MatchPressing.HighPress => HighPressRadius,
        _ => MidBlockRadius,
    };

    /// <summary>Gives every defender his job for this tick.</summary>
    /// <param name="situation">The moment of play.</param>
    /// <param name="orders">Receives one order per defender, in the same order as the situation's defenders.</param>
    public static void Assign(in TickDefensiveSituation situation, Span<TickDefensiveOrder> orders)
    {
        var defenders = situation.Defenders;
        var attackers = situation.Attackers;
        var count = defenders.Length;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(attackers.Length, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(orders.Length, count);

        Span<bool> busy = stackalloc bool[TickTacticalGeometry.TeamSize];
        Span<bool> dealtWith = stackalloc bool[TickTacticalGeometry.TeamSize];

        for (var index = 0; index < count; index++)
        {
            orders[index] = new TickDefensiveOrder(
                situation.Specs[index].Family == MatchPositionFamily.Goalkeeper ? TickDefensiveRole.Keeper : TickDefensiveRole.Holding,
                situation.Anchors[index],
                0,
                -1);
        }

        AssignPress(situation, orders, busy);
        AssignMarkers(situation, orders, busy, dealtWith);
        AssignScreens(situation, orders, busy, dealtWith);
        AssignLine(situation, orders);
    }

    /// <summary>Copies the orders into the anchors and paces the steering takes.</summary>
    /// <param name="orders">The orders.</param>
    /// <param name="anchors">Receives the points to go to.</param>
    /// <param name="paces">Receives the paces, in basis points of top speed (0 is the holding pace).</param>
    public static void ToSteering(ReadOnlySpan<TickDefensiveOrder> orders, Span<SpatialPoint> anchors, Span<int> paces)
    {
        for (var index = 0; index < orders.Length; index++)
        {
            anchors[index] = orders[index].Target;
            paces[index] = orders[index].PaceBasisPoints;
        }
    }

    /// <summary>
    /// Gets the offside line: the defender's-own X (0 at his own goal line) of the second-last defender, in fixed units.
    /// </summary>
    /// <param name="defenders">The defending side's players, including the goalkeeper.</param>
    /// <param name="defendersAreHome">Whether the defending side is the home side.</param>
    /// <returns>The line, or the full pitch length when fewer than two defenders are left.</returns>
    public static int OffsideLine(ReadOnlySpan<TickPlayerState> defenders, bool defendersAreHome)
    {
        var last = int.MaxValue;
        var secondLast = int.MaxValue;

        for (var index = 0; index < defenders.Length; index++)
        {
            var x = OwnFixed(defenders[index].X, defendersAreHome);

            if (x < last)
            {
                secondLast = last;
                last = x;
            }
            else if (x < secondLast)
            {
                secondLast = x;
            }
        }

        return secondLast == int.MaxValue ? TickSpatialUnits.PitchLengthFixed : secondLast;
    }

    /// <summary>
    /// Decides whether a receiver was offside at the moment a pass was played (the law: in the opponent's half and
    /// nearer their goal line than both the ball and the second-last defender; level is onside).
    /// </summary>
    /// <remarks>
    /// The caller calls this only for a pass that is subject to the law: not from a goal kick, a throw-in or a corner, and
    /// not backwards to a teammate who was behind the ball (which the ball comparison already clears).
    /// </remarks>
    /// <param name="receiverX">The receiver's X when the pass was played, in fixed units.</param>
    /// <param name="ballX">The ball's X when the pass was played, in fixed units.</param>
    /// <param name="defenders">The defending side's players when the pass was played.</param>
    /// <param name="defendersAreHome">Whether the defending side is the home side.</param>
    public static bool IsOffside(int receiverX, int ballX, ReadOnlySpan<TickPlayerState> defenders, bool defendersAreHome)
    {
        var receiver = OwnFixed(receiverX, defendersAreHome);

        return receiver < TickSpatialUnits.PitchLengthFixed / 2
            && receiver < OwnFixed(ballX, defendersAreHome)
            && receiver < OffsideLine(defenders, defendersAreHome);
    }

    /// <summary>Puts an offside on the event log, on the attacking side, against the receiver.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="attackingSide">The side that was caught offside.</param>
    /// <param name="receiverId">The receiver who was offside.</param>
    /// <param name="passerId">The player who played the pass, when known.</param>
    public static void FlagOffside(MatchState state, MatchSide attackingSide, Guid receiverId, Guid? passerId = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Emit(attackingSide, EngineEventType.Offside, receiverId, passerId);
    }

    private static void AssignPress(in TickDefensiveSituation situation, Span<TickDefensiveOrder> orders, Span<bool> busy)
    {
        var ownBallX = Own(situation.BallX, situation.IsHome);
        var applies = situation.Pressing switch
        {
            MatchPressing.LowBlock => ownBallX <= SpatialPitch.PitchLength / 2,
            MatchPressing.MidBlock => ownBallX <= MidBlockLimit,
            MatchPressing.HighPress => ownBallX <= HighPressLimit,
            _ => true,
        };

        if (!applies)
        {
            return;
        }

        var radius = TickSpatialUnits.ToFixed(PressRadius(situation.Pressing));
        var presser = NearestPresser(situation, radius, excluded: -1, prefer: situation.PreviousPresser);

        if (presser < 0)
        {
            return;
        }

        busy[presser] = true;
        orders[presser] = new TickDefensiveOrder(
            TickDefensiveRole.Presser,
            RunThrough(situation.Defenders[presser], situation.BallX, situation.BallY, PressThrough),
            PresserPaceBasisPoints,
            situation.CarrierIndex);

        var toTouchline = Math.Min(situation.BallY, SpatialPitch.PitchWidth - situation.BallY);

        if (situation.Pressing == MatchPressing.HighPress && toTouchline <= TrapDistance)
        {
            var second = NearestPresser(situation, radius, excluded: presser, prefer: -1);

            if (second >= 0)
            {
                var inward = situation.BallY < SpatialPitch.GoalYCenter ? TrapInset : -TrapInset;

                busy[second] = true;
                orders[second] = new TickDefensiveOrder(
                    TickDefensiveRole.SupportPresser,
                    new SpatialPoint(situation.BallX, Math.Clamp(situation.BallY + inward, 0, SpatialPitch.PitchWidth)),
                    SupportPresserPaceBasisPoints,
                    situation.CarrierIndex);
            }
        }
    }

    /// <summary>Finds the nearest outfield defender inside the radius who is able to press, favouring the last presser.</summary>
    private static int NearestPresser(in TickDefensiveSituation situation, int radius, int excluded, int prefer)
    {
        var limit = (long)radius * radius;
        var ballX = TickSpatialUnits.ToFixed(situation.BallX);
        var ballY = TickSpatialUnits.ToFixed(situation.BallY);
        var best = -1;
        var bestScore = long.MaxValue;

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            var player = situation.Defenders[index];

            if (index == excluded || player.Lockout > 0 || situation.Specs[index].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            long dx = player.X - ballX;
            long dy = player.Y - ballY;
            var distance = (dx * dx) + (dy * dy);

            if (distance > limit)
            {
                continue;
            }

            // The player already on the job counts as 15% nearer, so the job only changes hands for a clear reason.
            var score = index == prefer ? distance * HysteresisPercent * HysteresisPercent / 10_000 : distance;

            if (score < bestScore)
            {
                bestScore = score;
                best = index;
            }
        }

        return best;
    }

    private static void AssignMarkers(
        in TickDefensiveSituation situation,
        Span<TickDefensiveOrder> orders,
        Span<bool> busy,
        Span<bool> claimed)
    {
        var zone = (long)TickSpatialUnits.ToFixed(MarkingZone) * TickSpatialUnits.ToFixed(MarkingZone);
        var goalX = situation.IsHome ? 0 : SpatialPitch.PitchLength;

        while (true)
        {
            var bestDefender = -1;
            var bestAttacker = -1;
            var bestDistance = long.MaxValue;

            for (var defender = 0; defender < situation.Defenders.Length; defender++)
            {
                if (busy[defender] || situation.Specs[defender].Family != MatchPositionFamily.Defence)
                {
                    continue;
                }

                for (var attacker = 1; attacker < situation.Attackers.Length; attacker++)
                {
                    if (claimed[attacker]
                        || attacker == situation.CarrierIndex
                        || Own(TickSpatialUnits.ToUnits(situation.Attackers[attacker].X), situation.IsHome) >= DefensiveThird)
                    {
                        continue;
                    }

                    var distance = DistanceSquared(situation.Defenders[defender], situation.Attackers[attacker]);

                    if (distance <= zone && distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestDefender = defender;
                        bestAttacker = attacker;
                    }
                }
            }

            if (bestDefender < 0)
            {
                return;
            }

            var man = situation.Attackers[bestAttacker];
            var standOff = LooseMarkingStandOff - (MarkingStandOffStep * situation.Skills[bestDefender].Marking);

            busy[bestDefender] = true;
            claimed[bestAttacker] = true;
            orders[bestDefender] = new TickDefensiveOrder(
                TickDefensiveRole.Marker,
                RunThrough(man, goalX, SpatialPitch.GoalYCenter, standOff, fromPlayer: true),
                MarkerPaceBasisPoints,
                bestAttacker);
        }
    }

    private static void AssignScreens(
        in TickDefensiveSituation situation,
        Span<TickDefensiveOrder> orders,
        Span<bool> busy,
        Span<bool> dealtWith)
    {
        if (situation.CarrierIndex < 0)
        {
            return;
        }

        var carrier = situation.Attackers[situation.CarrierIndex];
        var carrierX = TickSpatialUnits.ToUnits(carrier.X);
        var carrierY = TickSpatialUnits.ToUnits(carrier.Y);
        var nearest = (long)TickSpatialUnits.ToFixed(MinimumScreenReach) * TickSpatialUnits.ToFixed(MinimumScreenReach);
        var farthest = (long)TickSpatialUnits.ToFixed(MaximumScreenReach) * TickSpatialUnits.ToFixed(MaximumScreenReach);
        var range = (long)TickSpatialUnits.ToFixed(ScreenerRange) * TickSpatialUnits.ToFixed(ScreenerRange);

        for (var screen = 0; screen < MaximumScreens; screen++)
        {
            // The most dangerous receiver left: the one nearest the defender's own goal line.
            var receiver = -1;
            var receiverDepth = int.MaxValue;

            for (var attacker = 1; attacker < situation.Attackers.Length; attacker++)
            {
                if (dealtWith[attacker] || attacker == situation.CarrierIndex)
                {
                    continue;
                }

                var lane = DistanceSquared(carrier, situation.Attackers[attacker]);
                var depth = Own(TickSpatialUnits.ToUnits(situation.Attackers[attacker].X), situation.IsHome);

                if (lane >= nearest && lane <= farthest && depth < receiverDepth)
                {
                    receiverDepth = depth;
                    receiver = attacker;
                }
            }

            if (receiver < 0)
            {
                return;
            }

            dealtWith[receiver] = true;

            var target = situation.Attackers[receiver];
            var laneLength = (int)SpatialMath.Sqrt(DistanceSquared(carrier, target)) / TickSpatialUnits.FixedScale;
            var along = Math.Max(MinimumScreenDistance, laneLength * ScreenLanePercent / 100);
            var laneX = carrierX + ((TickSpatialUnits.ToUnits(target.X) - carrierX) * along / Math.Max(1, laneLength));
            var laneY = carrierY + ((TickSpatialUnits.ToUnits(target.Y) - carrierY) * along / Math.Max(1, laneLength));

            // The nearest free midfielder or forward to that point takes it.
            var screener = -1;
            var screenerDistance = long.MaxValue;

            for (var defender = 0; defender < situation.Defenders.Length; defender++)
            {
                var family = situation.Specs[defender].Family;

                if (busy[defender] || (family != MatchPositionFamily.Midfield && family != MatchPositionFamily.Attack))
                {
                    continue;
                }

                var distance = DistanceSquared(situation.Defenders[defender], laneX, laneY);

                if (distance <= range && distance < screenerDistance)
                {
                    screenerDistance = distance;
                    screener = defender;
                }
            }

            if (screener < 0)
            {
                continue;
            }

            // Positioning decides how much of the lane point he takes, the rest being the shape position he came from.
            var commitment = 5_000 + (250 * situation.Skills[screener].Positioning);
            var anchor = situation.Anchors[screener];

            busy[screener] = true;
            orders[screener] = new TickDefensiveOrder(
                TickDefensiveRole.Screen,
                new SpatialPoint(
                    anchor.X + ((laneX - anchor.X) * commitment / 10_000),
                    anchor.Y + ((laneY - anchor.Y) * commitment / 10_000)),
                ScreenPaceBasisPoints,
                receiver);
        }
    }

    private static void AssignLine(in TickDefensiveSituation situation, Span<TickDefensiveOrder> orders)
    {
        var members = 0;
        var holders = 0;
        long anchorSum = 0;
        long decisionsSum = 0;

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            if (situation.Specs[index].Family != MatchPositionFamily.Defence)
            {
                continue;
            }

            members++;
            anchorSum += Own(situation.Anchors[index].X, situation.IsHome);
            decisionsSum += situation.Skills[index].Decisions;

            if (orders[index].Role == TickDefensiveRole.Holding)
            {
                holders++;
            }
        }

        if (holders == 0)
        {
            return;
        }

        var line = (int)(anchorSum / members);
        var decisions = (int)(decisionsSum / members);
        var nearest = NearestOutfieldDistanceSquared(situation);
        var closed = (long)TickSpatialUnits.ToFixed(ClosedDownDistance) * TickSpatialUnits.ToFixed(ClosedDownDistance);
        var free = (long)TickSpatialUnits.ToFixed(FreeCarrierDistance) * TickSpatialUnits.ToFixed(FreeCarrierDistance);

        if (nearest <= closed)
        {
            line += LineStepUp * decisions / 20;
        }
        else if (nearest > free && Own(situation.BallX, situation.IsHome) <= LineDropBallLimit)
        {
            line -= LineDropOff;
        }

        line = Math.Clamp(line, LineFloor, LineCeiling);

        var lineX = Own(line, situation.IsHome);

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            if (situation.Specs[index].Family == MatchPositionFamily.Defence && orders[index].Role == TickDefensiveRole.Holding)
            {
                orders[index] = new TickDefensiveOrder(
                    TickDefensiveRole.Line,
                    new SpatialPoint(lineX, situation.Anchors[index].Y),
                    0,
                    -1);
            }
        }
    }

    private static long NearestOutfieldDistanceSquared(in TickDefensiveSituation situation)
    {
        var nearest = long.MaxValue;

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            if (situation.Specs[index].Family != MatchPositionFamily.Goalkeeper)
            {
                nearest = Math.Min(nearest, DistanceSquared(situation.Defenders[index], situation.BallX, situation.BallY));
            }
        }

        return nearest;
    }

    /// <summary>Gets the point a given distance beyond (or, from a player, short of) a destination, along the line to it.</summary>
    /// <param name="from">The player running.</param>
    /// <param name="toX">The destination X, in pitch units.</param>
    /// <param name="toY">The destination Y, in pitch units.</param>
    /// <param name="distance">The distance, in pitch units.</param>
    /// <param name="fromPlayer">
    /// False to go that far past the destination (a presser running onto the ball); true to stand that far from the player,
    /// on the destination's side (a marker standing between his man and the goal).
    /// </param>
    private static SpatialPoint RunThrough(in TickPlayerState from, int toX, int toY, int distance, bool fromPlayer = false)
    {
        var dx = (long)TickSpatialUnits.ToFixed(toX) - from.X;
        var dy = (long)TickSpatialUnits.ToFixed(toY) - from.Y;
        var length = SpatialMath.Sqrt((dx * dx) + (dy * dy));
        var origin = fromPlayer
            ? new SpatialPoint(TickSpatialUnits.ToUnits(from.X), TickSpatialUnits.ToUnits(from.Y))
            : new SpatialPoint(toX, toY);

        if (length == 0)
        {
            return origin;
        }

        var reach = fromPlayer ? Math.Min(distance, (int)(length / TickSpatialUnits.FixedScale)) : distance;

        return new SpatialPoint(origin.X + (int)(dx * reach / length), origin.Y + (int)(dy * reach / length));
    }

    private static long DistanceSquared(in TickPlayerState one, in TickPlayerState other)
    {
        long dx = one.X - other.X;
        long dy = one.Y - other.Y;

        return (dx * dx) + (dy * dy);
    }

    private static long DistanceSquared(in TickPlayerState player, int xUnits, int yUnits)
    {
        long dx = player.X - TickSpatialUnits.ToFixed(xUnits);
        long dy = player.Y - TickSpatialUnits.ToFixed(yUnits);

        return (dx * dx) + (dy * dy);
    }

    /// <summary>Converts an X between the pitch's and a side's own point of view, where 0 is its own goal line (and back again).</summary>
    private static int Own(int xUnits, bool isHome) => isHome ? xUnits : SpatialPitch.PitchLength - xUnits;

    private static int OwnFixed(int xFixed, bool isHome) => isHome ? xFixed : TickSpatialUnits.PitchLengthFixed - xFixed;
}
