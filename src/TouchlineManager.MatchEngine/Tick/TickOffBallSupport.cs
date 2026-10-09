using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>What an attacker is doing this tick.</summary>
internal enum TickAttackingRole
{
    /// <summary>Holding his shape position, kept onside: nothing about the play asks more of him.</summary>
    Holding = 0,

    /// <summary>The goalkeeper, who is steered by his own milestone and here simply keeps his anchor.</summary>
    Keeper = 1,

    /// <summary>The man with the ball: the ball-carrier brain decides what he does, not the support AI.</summary>
    Carrier = 2,

    /// <summary>One corner of a passing triangle with the carrier.</summary>
    Support = 3,

    /// <summary>A safe pass behind a carrier who is being closed down.</summary>
    Outlet = 4,

    /// <summary>A forward sprinting into the space behind the defensive line.</summary>
    Runner = 5,

    /// <summary>A forward dropping into the pocket between the opponent's midfield and defence to take the ball to feet.</summary>
    Pocket = 6,

    /// <summary>A full-back running into the flank a winger has left, to be crossed to.</summary>
    Overlap = 7,
}

/// <summary>
/// One attacker's job and where it sends him.
/// </summary>
/// <param name="Role">What he is doing.</param>
/// <param name="Target">The point he is to go to, in pitch units.</param>
/// <param name="PaceBasisPoints">The share of his top speed to travel at, or 0 for the holding pace.</param>
internal readonly record struct TickAttackingOrder(TickAttackingRole Role, SpatialPoint Target, int PaceBasisPoints);

/// <summary>
/// Everything the attacking support AI reads about one moment of play. Index 0 of both sides is the goalkeeper.
/// </summary>
internal readonly ref struct TickAttackingSituation
{
    /// <summary>Gets a value indicating whether the attacking side is the home side (attacking towards high X).</summary>
    public required bool IsHome { get; init; }

    /// <summary>Gets the attacking side's mentality, which sets how many penetrating runs it makes.</summary>
    public required MatchMentality Mentality { get; init; }

    /// <summary>Gets the attacking side's passing style, which sets how far its supporters stand from the ball.</summary>
    public required MatchPassingStyle Passing { get; init; }

    /// <summary>Gets the attacking side's players, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Attackers { get; init; }

    /// <summary>Gets the attacking side's board positions, in the same order (they carry the position family and flank).</summary>
    public required ReadOnlySpan<TickAnchorSpec> Specs { get; init; }

    /// <summary>Gets the attacking side's dynamic anchors for this tick, in the same order, in pitch units.</summary>
    public required ReadOnlySpan<SpatialPoint> Anchors { get; init; }

    /// <summary>Gets the attacking side's skills, in the same order.</summary>
    public required ReadOnlySpan<TickPlayerSkills> Skills { get; init; }

    /// <summary>Gets the defending side's players, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Defenders { get; init; }

    /// <summary>Gets the attacker with the ball at his feet, or -1 when the ball is loose or in the air.</summary>
    public required int CarrierIndex { get; init; }

    /// <summary>
    /// Gets the players who supported last tick, one bit each (<see cref="TickOffBallSupport.SupporterMask"/>). They keep
    /// the job unless someone is clearly closer, so two players at the same distance do not trade it every tick.
    /// </summary>
    public int PreviousSupporters { get; init; }

    /// <summary>
    /// Gets the players who were running behind the line last tick, one bit each
    /// (<see cref="TickOffBallSupport.RunnerMask"/>). A run once begun is carried through while the carrier still has room.
    /// </summary>
    public int PreviousRunners { get; init; }
}

/// <summary>
/// Attacking movement off the ball for the tick engine: passing triangles, runs behind the line, checks into the pocket and
/// overlapping full-backs (`tick-engine-v1`, Milestone 4).
/// </summary>
/// <remarks>
/// <para>
/// All geometry is worked in the attacking side's own point of view (it attacks towards high X and Y counts from its left
/// hand) and mirrored back for the away side, as <see cref="TickTacticalGeometry"/> does. Each tick <see cref="Assign"/>
/// gives every attacker exactly one job, in this order, each taking players the ones before it have not:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Overlap.</b> When a winger (a wide midfielder or forward) has the ball and is cutting inside (his heading leans at
/// least 30% of his speed towards the middle, at 1.5 m/s or more), the full-back on his flank sprints into the wide channel he
/// has left: 10 m beyond the winger, 9 m from the touchline, 85–95% of top speed by WorkRate. A defensive side never does it.
/// </description></item>
/// <item><description>
/// <b>Triangle.</b> The carrier always has two short options. Each of two slots, one either side of the carrier, is a point
/// 14 m (short passing), 19 m or 24 m (direct) from him at 45° to the direction of attack; if a defender stands within 2.5 m
/// of that lane the angle is walked through 30°, 60°, 38° and 52° for one that is clear. The nearest free outfield player
/// to each slot (within 35 m) takes it, nearest pair first, so the choice does not depend on the order the players are
/// listed in; a striker counts as 73% farther (he is kept free to run) and the player who held the slot last tick as 15%
/// nearer. He goes there in proportion to
/// <c>70% + 1.5% × (Positioning + Anticipation) / 2</c> of the way from his shape position (100% at 20) and at
/// <c>65% + 1% × WorkRate</c> of his top speed. A point is never put beyond the offside line.
/// </description></item>
/// <item><description>
/// <b>Outlet.</b> A carrier with a defender within 9 m also gets a safe pass: a point 14 m from him, turned 40° off the line
/// directly away from that defender, on the side whose lane is clear.
/// </description></item>
/// <item><description>
/// <b>Runs.</b> If the carrier is past his own third, has no defender within 12 m, and there are 15 m or more behind the back
/// line, forwards (and, for a Positive or Attacking side, wide midfielders) who are onside and ahead of the ball sprint at
/// full pace into a gap between the back line's players (the widest within 25 m across, never one already taken), to 10 m
/// behind the line. The highest scorers on Pace + Acceleration + Positioning + Anticipation (strikers 4 ahead of wide
/// midfielders) go first, up to a number set by
/// mentality: none for Defensive, 1 for Cautious and Balanced, 2 for Positive, 3 for Attacking. A run in progress carries
/// on until the carrier is closed down to 6 m. A forward already beyond the line never starts one.
/// </description></item>
/// <item><description>
/// <b>Pocket.</b> The forward left over who is nearest the carrier (and at least 12 m from him) drops to 5 m in front of the
/// back line, 40% of the way across towards the carrier's side, to take the ball to feet, provided the point is ahead of
/// the carrier.
/// </description></item>
/// </list>
/// <para>
/// Everyone else holds his shape position, held onside: no further up the pitch than the offside line, the ball, or the
/// halfway line, whichever is deepest. Everything is integer arithmetic over spans and a stack buffer, so a tick allocates
/// nothing.
/// </para>
/// </remarks>
internal static class TickOffBallSupport
{
    /// <summary>How far a supporter stands from the ball under short passing, in pitch units (14 m).</summary>
    public const int ShortSupportDistance = 1_400;

    /// <summary>How far a supporter stands from the ball under mixed passing, in pitch units (19 m).</summary>
    public const int MixedSupportDistance = 1_900;

    /// <summary>How far a supporter stands from the ball under direct passing, in pitch units (24 m).</summary>
    public const int DirectSupportDistance = 2_400;

    /// <summary>How near a defender may stand to a passing lane before it counts as cut, in pitch units (2.5 m).</summary>
    public const int InterceptReach = 250;

    /// <summary>The farthest a player goes to take up a support slot, in pitch units.</summary>
    public const int SupporterRange = 3_500;

    /// <summary>The share of the way to a support point a player with Positioning and Anticipation 0 goes, in basis points.</summary>
    public const int SupportCommitmentBasisPoints = 7_000;

    /// <summary>The extra share of the way per point of the mean of Positioning and Anticipation, in basis points.</summary>
    public const int SupportCommitmentStep = 150;

    /// <summary>The pace of a supporter at WorkRate 0, in basis points of top speed.</summary>
    public const int SupportPaceBasisPoints = 6_500;

    /// <summary>The pace a supporter gains per point of WorkRate, in basis points of top speed.</summary>
    public const int SupportPaceStep = 100;

    /// <summary>Within this distance of the carrier a defender counts as pressing him (9 m), in pitch units.</summary>
    public const int PressedDistance = 900;

    /// <summary>How far behind the carrier the safety outlet stands, in pitch units (14 m).</summary>
    public const int OutletDistance = 1_400;

    /// <summary>The pace of an outlet, in basis points of top speed.</summary>
    public const int OutletPaceBasisPoints = 9_000;

    /// <summary>How far an outlet is turned off the line away from the presser, in binary angle units (about 40°).</summary>
    public const int OutletTurn = 114;

    /// <summary>How far behind the offside line a run goes, in pitch units (10 m).</summary>
    public const int RunDepth = 1_000;

    /// <summary>The nearest to the goal line a run goes, in pitch units.</summary>
    public const int RunGoalGap = 800;

    /// <summary>The least room behind the line a run needs, in pitch units (15 m).</summary>
    public const int MinimumRunSpace = 1_500;

    /// <summary>The nearest to his own goal a carrier is for a run to start (his own third has passed), in pitch units.</summary>
    public const int RunCarrierMinimum = 3_500;

    /// <summary>The farthest short of the line a forward may start a run from, in pitch units (40 m).</summary>
    public const int RunStartRange = 4_000;

    /// <summary>How far in front of the offside line a defender still counts as part of the back line, in pitch units (25 m).</summary>
    public const int BackLineDepth = 2_500;

    /// <summary>The narrowest gap between defenders a run is sent into, in pitch units (7 m).</summary>
    public const int MinimumGap = 700;

    /// <summary>The farthest across the pitch a forward goes to find his gap, in pitch units (25 m).</summary>
    public const int RunLateralRange = 2_500;

    /// <summary>The pace of a run, in basis points of top speed.</summary>
    public const int RunPaceBasisPoints = 10_000;

    /// <summary>How far in front of the back line the pocket is, in pitch units (5 m).</summary>
    public const int PocketDepth = 500;

    /// <summary>The nearest the carrier may be to the line for a forward to check into the pocket, in pitch units (18 m).</summary>
    public const int PocketCarrierGap = 1_800;

    /// <summary>The nearest a forward is to the carrier to be sent to the pocket, in pitch units (12 m).</summary>
    public const int PocketMinimumDistance = 1_200;

    /// <summary>The share of the way across to the carrier's side that the pocket is, in percent.</summary>
    public const int PocketPullPercent = 40;

    /// <summary>The pace of a check into the pocket, in basis points of top speed.</summary>
    public const int PocketPaceBasisPoints = 8_500;

    /// <summary>The distance from the middle of the pitch beyond which a player is wide, in pitch units (18 m).</summary>
    public const int WideThreshold = 1_800;

    /// <summary>How much of a winger's speed must be towards the middle for him to count as cutting inside, in basis points.</summary>
    public const int InwardBasisPoints = 3_000;

    /// <summary>The farthest a full-back goes to overlap, in pitch units (45 m).</summary>
    public const int OverlapRange = 4_500;

    /// <summary>How far beyond the winger the overlapping full-back runs, in pitch units (10 m).</summary>
    public const int OverlapAhead = 1_000;

    /// <summary>How far from the touchline the overlapping full-back runs, in pitch units (9 m).</summary>
    public const int OverlapLane = 900;

    /// <summary>The nearest to the goal line an overlap goes, in pitch units.</summary>
    public const int OverlapGoalGap = 700;

    /// <summary>The pace of an overlap at WorkRate 0, in basis points of top speed.</summary>
    public const int OverlapPaceBasisPoints = 8_500;

    /// <summary>The pace an overlap gains per point of WorkRate, in basis points of top speed.</summary>
    public const int OverlapPaceStep = 50;

    /// <summary>How far short of the offside line an onside player is held, in pitch units (1 m).</summary>
    public const int OnsideMargin = 100;

    /// <summary>How much farther (on the squared distance, in percent) a striker counts as being from a support slot.</summary>
    public const int StrikerSupportPenaltyPercent = 300;

    private const int HysteresisPercent = 85;

    /// <summary>The score a runner already running is given over a rival, so the job does not change hands every tick.</summary>
    private const int RunnerStickiness = 8;

    /// <summary>The score a forward is given over a wide midfielder, so strikers make the runs before wingers do.</summary>
    private const int StrikerPreference = 4;

    private const int BasisPoints = 10_000;

    /// <summary>The angles tried for a support slot, in binary angle units: 45°, 30°, 60°, 38°, 52° from the line of attack.</summary>
    private static readonly int[] SupportAngles = [128, 85, 171, 107, 149];

    /// <summary>The slowest a winger may be moving to count as cutting inside (1.5 m/s), in fixed units per tick.</summary>
    private static readonly int MinimumCutSpeed = TickSpatialUnits.SpeedToFixedPerTick(150);

    /// <summary>Gets how far a supporter stands from the carrier under a passing style, in pitch units.</summary>
    /// <param name="style">The passing style.</param>
    public static int SupportDistance(MatchPassingStyle style) => style switch
    {
        MatchPassingStyle.ShortPassing => ShortSupportDistance,
        MatchPassingStyle.DirectPassing => DirectSupportDistance,
        _ => MixedSupportDistance,
    };

    /// <summary>Gets how many players a side with a mentality sends running behind the line at once.</summary>
    /// <param name="mentality">The mentality.</param>
    public static int RunBudget(MatchMentality mentality) => mentality switch
    {
        MatchMentality.Defensive => 0,
        MatchMentality.Positive => 2,
        MatchMentality.Attacking => 3,
        _ => 1,
    };

    /// <summary>Gives every attacker his job for this tick.</summary>
    /// <param name="situation">The moment of play.</param>
    /// <param name="orders">Receives one order per attacker, in the same order as the situation's attackers.</param>
    public static void Assign(in TickAttackingSituation situation, Span<TickAttackingOrder> orders)
    {
        var count = situation.Attackers.Length;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(situation.Defenders.Length, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(orders.Length, count);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(situation.CarrierIndex, Math.Max(1, count));

        Span<bool> busy = stackalloc bool[TickTacticalGeometry.TeamSize];

        for (var index = 0; index < count; index++)
        {
            orders[index] = new TickAttackingOrder(
                situation.Specs[index].Family == MatchPositionFamily.Goalkeeper ? TickAttackingRole.Keeper : TickAttackingRole.Holding,
                situation.Anchors[index],
                0);
        }

        if (situation.CarrierIndex < 0)
        {
            return;
        }

        var frame = Frame.Of(situation);
        var carrier = situation.Attackers[situation.CarrierIndex];

        busy[situation.CarrierIndex] = true;
        orders[situation.CarrierIndex] = new TickAttackingOrder(
            TickAttackingRole.Carrier,
            new SpatialPoint(TickSpatialUnits.ToUnits(carrier.X), TickSpatialUnits.ToUnits(carrier.Y)),
            0);

        AssignOverlap(situation, frame, orders, busy);
        AssignSupport(situation, frame, orders, busy);
        AssignOutlet(situation, frame, orders, busy);
        AssignRuns(situation, frame, orders, busy);
        AssignPocket(situation, frame, orders, busy);
        KeepOnside(situation, frame, orders);
    }

    /// <summary>Copies the orders into the anchors and paces the steering takes.</summary>
    /// <param name="orders">The orders.</param>
    /// <param name="anchors">Receives the points to go to.</param>
    /// <param name="paces">Receives the paces, in basis points of top speed (0 is the holding pace).</param>
    public static void ToSteering(ReadOnlySpan<TickAttackingOrder> orders, Span<SpatialPoint> anchors, Span<int> paces)
    {
        for (var index = 0; index < orders.Length; index++)
        {
            anchors[index] = orders[index].Target;
            paces[index] = orders[index].PaceBasisPoints;
        }
    }

    /// <summary>Gets one bit per player supporting the carrier (a triangle corner or the outlet), to feed the next tick.</summary>
    /// <param name="orders">The orders.</param>
    public static int SupporterMask(ReadOnlySpan<TickAttackingOrder> orders) =>
        MaskOf(orders, TickAttackingRole.Support, TickAttackingRole.Outlet);

    /// <summary>Gets one bit per player running behind the line, to feed the next tick.</summary>
    /// <param name="orders">The orders.</param>
    public static int RunnerMask(ReadOnlySpan<TickAttackingOrder> orders) =>
        MaskOf(orders, TickAttackingRole.Runner, TickAttackingRole.Runner);

    /// <summary>Tells whether a pass lane is free of defenders: none stands within <see cref="InterceptReach"/> of it.</summary>
    /// <param name="defenders">The defending side's players.</param>
    /// <param name="isHome">Whether the attacking side is the home side (it sets the point of view of the coordinates).</param>
    /// <param name="fromX">The passer's X, in the attacking side's own point of view, in pitch units.</param>
    /// <param name="fromY">The passer's Y, in the attacking side's own point of view, in pitch units.</param>
    /// <param name="toX">The receiver's X, in the attacking side's own point of view, in pitch units.</param>
    /// <param name="toY">The receiver's Y, in the attacking side's own point of view, in pitch units.</param>
    public static bool LaneClear(ReadOnlySpan<TickPlayerState> defenders, bool isHome, int fromX, int fromY, int toX, int toY)
    {
        var limit = (long)InterceptReach * InterceptReach;

        for (var index = 0; index < defenders.Length; index++)
        {
            var x = MirrorX(TickSpatialUnits.ToUnits(defenders[index].X), isHome);
            var y = MirrorY(TickSpatialUnits.ToUnits(defenders[index].Y), isHome);

            if (SegmentDistanceSquared(x, y, fromX, fromY, toX, toY) < limit)
            {
                return false;
            }
        }

        return true;
    }

    private static int MaskOf(ReadOnlySpan<TickAttackingOrder> orders, TickAttackingRole one, TickAttackingRole other)
    {
        var mask = 0;

        for (var index = 0; index < orders.Length; index++)
        {
            if (orders[index].Role == one || orders[index].Role == other)
            {
                mask |= 1 << index;
            }
        }

        return mask;
    }

    private static void AssignOverlap(in TickAttackingSituation situation, in Frame frame, Span<TickAttackingOrder> orders, Span<bool> busy)
    {
        if (situation.Mentality == MatchMentality.Defensive)
        {
            return;
        }

        var carrierFamily = situation.Specs[situation.CarrierIndex].Family;
        var middle = SpatialPitch.GoalYCenter;

        if ((carrierFamily != MatchPositionFamily.Midfield && carrierFamily != MatchPositionFamily.Attack)
            || Math.Abs(frame.CarrierY - middle) < WideThreshold
            || situation.Attackers[situation.CarrierIndex].Speed < MinimumCutSpeed)
        {
            return;
        }

        // The carrier's heading in the side's own point of view: mirroring both axes turns the away side half a turn.
        var heading = situation.IsHome ? frame.CarrierHeading : frame.CarrierHeading + TickTrigonometry.HalfTurn;
        var lean = TickTrigonometry.Sin(heading);
        var inward = frame.CarrierY < middle ? lean : -lean;

        if (inward < InwardBasisPoints)
        {
            return;
        }

        var best = -1;
        var bestDistance = long.MaxValue;
        var range = (long)OverlapRange * OverlapRange;

        for (var index = 0; index < situation.Attackers.Length; index++)
        {
            var spec = situation.Specs[index];

            if (busy[index]
                || spec.Family != MatchPositionFamily.Defence
                || Math.Abs(spec.OwnY - middle) < WideThreshold
                || (spec.OwnY < middle) != (frame.CarrierY < middle))
            {
                continue;
            }

            var distance = DistanceSquared(situation.Attackers[index], frame.IsHome, frame.CarrierX, frame.CarrierY);

            if (distance <= range && distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        if (best < 0)
        {
            return;
        }

        var x = Math.Min(Math.Min(frame.CarrierX + OverlapAhead, SpatialPitch.PitchLength - OverlapGoalGap), OnsideLimit(frame));
        var y = frame.CarrierY < middle ? OverlapLane : SpatialPitch.PitchWidth - OverlapLane;

        busy[best] = true;
        orders[best] = new TickAttackingOrder(
            TickAttackingRole.Overlap,
            Mirror(x, y, situation.IsHome),
            Math.Min(BasisPoints, OverlapPaceBasisPoints + (OverlapPaceStep * situation.Skills[best].WorkRate)));
    }

    private static void AssignSupport(in TickAttackingSituation situation, in Frame frame, Span<TickAttackingOrder> orders, Span<bool> busy)
    {
        var distance = SupportDistance(situation.Passing);
        Span<SpatialPoint> slots = stackalloc SpatialPoint[2];
        Span<bool> filled = stackalloc bool[2];

        slots[0] = SupportPoint(situation, frame, -1, distance);
        slots[1] = SupportPoint(situation, frame, 1, distance);

        var range = (long)SupporterRange * SupporterRange;

        for (var round = 0; round < slots.Length; round++)
        {
            var bestSlot = -1;
            var bestPlayer = -1;
            var bestScore = long.MaxValue;

            for (var slot = 0; slot < slots.Length; slot++)
            {
                if (filled[slot])
                {
                    continue;
                }

                for (var index = 0; index < situation.Attackers.Length; index++)
                {
                    if (busy[index])
                    {
                        continue;
                    }

                    var gap = DistanceSquared(situation.Attackers[index], frame.IsHome, slots[slot].X, slots[slot].Y);

                    if (gap > range)
                    {
                        continue;
                    }

                    // A striker counts as farther (his job is to run, not to link), and the player who held a slot last tick
                    // as 15% nearer, so it only changes hands for a clear reason.
                    var score = situation.Specs[index].Family == MatchPositionFamily.Attack ? gap * StrikerSupportPenaltyPercent / 100 : gap;

                    if (((situation.PreviousSupporters >> index) & 1) != 0)
                    {
                        score = score * HysteresisPercent * HysteresisPercent / BasisPoints;
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestSlot = slot;
                        bestPlayer = index;
                    }
                }
            }

            if (bestSlot < 0)
            {
                return;
            }

            filled[bestSlot] = true;
            busy[bestPlayer] = true;

            var skills = situation.Skills[bestPlayer];
            var commitment = SupportCommitmentBasisPoints + (SupportCommitmentStep * ((skills.Positioning + skills.Anticipation) / 2));
            var anchor = situation.Anchors[bestPlayer];
            var ideal = Mirror(slots[bestSlot].X, slots[bestSlot].Y, situation.IsHome);

            orders[bestPlayer] = new TickAttackingOrder(
                TickAttackingRole.Support,
                new SpatialPoint(
                    anchor.X + ((ideal.X - anchor.X) * Math.Min(BasisPoints, commitment) / BasisPoints),
                    anchor.Y + ((ideal.Y - anchor.Y) * Math.Min(BasisPoints, commitment) / BasisPoints)),
                Math.Min(BasisPoints, SupportPaceBasisPoints + (SupportPaceStep * skills.WorkRate)));
        }
    }

    /// <summary>Finds the corner of the triangle on one side of the carrier, in the side's own point of view.</summary>
    private static SpatialPoint SupportPoint(in TickAttackingSituation situation, in Frame frame, int side, int distance)
    {
        var fallback = default(SpatialPoint);
        var haveFallback = false;

        for (var attempt = 0; attempt < SupportAngles.Length; attempt++)
        {
            var angle = SupportAngles[attempt];
            var x = frame.CarrierX + (distance * TickTrigonometry.Cos(angle) / BasisPoints);
            var y = frame.CarrierY + (side * distance * TickTrigonometry.Sin(angle) / BasisPoints);

            if (y < TickTacticalGeometry.Margin || y > SpatialPitch.PitchWidth - TickTacticalGeometry.Margin)
            {
                continue;
            }

            x = Math.Min(x, OnsideLimit(frame));
            x = Math.Clamp(x, TickTacticalGeometry.Margin, SpatialPitch.PitchLength - TickTacticalGeometry.Margin);

            if (LaneClear(situation.Defenders, frame.IsHome, frame.CarrierX, frame.CarrierY, x, y))
            {
                return new SpatialPoint(x, y);
            }

            if (!haveFallback)
            {
                fallback = new SpatialPoint(x, y);
                haveFallback = true;
            }
        }

        if (haveFallback)
        {
            return fallback;
        }

        // Every angle falls off the pitch (the carrier is hard against that touchline): stand on the touchline instead.
        return new SpatialPoint(
            Math.Clamp(
                Math.Min(frame.CarrierX + (distance * TickTrigonometry.Cos(SupportAngles[0]) / BasisPoints), OnsideLimit(frame)),
                TickTacticalGeometry.Margin,
                SpatialPitch.PitchLength - TickTacticalGeometry.Margin),
            side < 0 ? TickTacticalGeometry.Margin : SpatialPitch.PitchWidth - TickTacticalGeometry.Margin);
    }

    private static void AssignOutlet(in TickAttackingSituation situation, in Frame frame, Span<TickAttackingOrder> orders, Span<bool> busy)
    {
        var pressed = (long)TickSpatialUnits.ToFixed(PressedDistance) * TickSpatialUnits.ToFixed(PressedDistance);

        if (frame.NearestDefenderSquared > pressed)
        {
            return;
        }

        // Directly away from the presser, then turned to whichever side leaves a clear lane.
        var away = TickTrigonometry.AngleOf(frame.CarrierX - frame.PresserX, frame.CarrierY - frame.PresserY);
        var target = OutletPoint(frame, away + OutletTurn);

        if (!LaneClear(situation.Defenders, frame.IsHome, frame.CarrierX, frame.CarrierY, target.X, target.Y))
        {
            var other = OutletPoint(frame, away - OutletTurn);

            if (LaneClear(situation.Defenders, frame.IsHome, frame.CarrierX, frame.CarrierY, other.X, other.Y))
            {
                target = other;
            }
        }

        var best = -1;
        var bestDistance = long.MaxValue;
        var range = (long)SupporterRange * SupporterRange;

        for (var index = 0; index < situation.Attackers.Length; index++)
        {
            if (busy[index])
            {
                continue;
            }

            var distance = DistanceSquared(situation.Attackers[index], frame.IsHome, target.X, target.Y);

            if (distance <= range && distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        if (best >= 0)
        {
            busy[best] = true;
            orders[best] = new TickAttackingOrder(
                TickAttackingRole.Outlet,
                Mirror(target.X, target.Y, situation.IsHome),
                OutletPaceBasisPoints);
        }
    }

    /// <summary>Gets the outlet point a given angle from the carrier, kept on the pitch, in the side's own point of view.</summary>
    private static SpatialPoint OutletPoint(in Frame frame, int angle) =>
        new(
            Math.Clamp(
                frame.CarrierX + (OutletDistance * TickTrigonometry.Cos(angle) / BasisPoints),
                TickTacticalGeometry.Margin,
                SpatialPitch.PitchLength - TickTacticalGeometry.Margin),
            Math.Clamp(
                frame.CarrierY + (OutletDistance * TickTrigonometry.Sin(angle) / BasisPoints),
                TickTacticalGeometry.Margin,
                SpatialPitch.PitchWidth - TickTacticalGeometry.Margin));

    private static void AssignRuns(in TickAttackingSituation situation, in Frame frame, Span<TickAttackingOrder> orders, Span<bool> busy)
    {
        var budget = RunBudget(situation.Mentality);
        var space = frame.DefenderLine;

        if (budget == 0 || space < MinimumRunSpace || frame.CarrierX < RunCarrierMinimum)
        {
            return;
        }

        var free = (long)TickSpatialUnits.ToFixed(TickDefensiveAI.FreeCarrierDistance) * TickSpatialUnits.ToFixed(TickDefensiveAI.FreeCarrierDistance);
        var closed = (long)TickSpatialUnits.ToFixed(TickDefensiveAI.ClosedDownDistance) * TickSpatialUnits.ToFixed(TickDefensiveAI.ClosedDownDistance);
        var mayStart = frame.NearestDefenderSquared > free;
        var mayContinue = frame.NearestDefenderSquared > closed;

        if (!mayContinue)
        {
            return;
        }

        // The gaps between the back line's players, across the pitch: [margin, defenders..., margin].
        Span<int> edges = stackalloc int[TickTacticalGeometry.TeamSize + 2];
        var edgeCount = 0;

        edges[edgeCount++] = TickTacticalGeometry.Margin;

        for (var index = 1; index < situation.Defenders.Length; index++)
        {
            var x = MirrorX(TickSpatialUnits.ToUnits(situation.Defenders[index].X), frame.IsHome);

            if (x < frame.LineX - BackLineDepth)
            {
                continue;
            }

            var y = MirrorY(TickSpatialUnits.ToUnits(situation.Defenders[index].Y), frame.IsHome);
            var at = edgeCount++;

            while (at > 1 && edges[at - 1] > y)
            {
                edges[at] = edges[at - 1];
                at--;
            }

            edges[at] = y;
        }

        edges[edgeCount++] = SpatialPitch.PitchWidth - TickTacticalGeometry.Margin;

        Span<bool> gapUsed = stackalloc bool[TickTacticalGeometry.TeamSize + 1];
        Span<bool> tried = stackalloc bool[TickTacticalGeometry.TeamSize];
        var wideRunners = situation.Mentality is MatchMentality.Positive or MatchMentality.Attacking;
        var started = 0;

        while (started < budget)
        {
            var best = -1;
            var bestScore = int.MinValue;

            for (var index = 0; index < situation.Attackers.Length; index++)
            {
                if (busy[index] || tried[index])
                {
                    continue;
                }

                var spec = situation.Specs[index];
                var forward = spec.Family == MatchPositionFamily.Attack
                    || (wideRunners && spec.Family == MatchPositionFamily.Midfield && Math.Abs(spec.OwnY - SpatialPitch.GoalYCenter) >= WideThreshold);

                if (!forward)
                {
                    continue;
                }

                var previous = ((situation.PreviousRunners >> index) & 1) != 0;
                var px = MirrorX(TickSpatialUnits.ToUnits(situation.Attackers[index].X), frame.IsHome);

                // A new run needs a free carrier and an onside, forward-placed runner; one begun is carried through.
                if (!previous && (!mayStart || px > frame.LineX || px < frame.CarrierX - 500 || frame.LineX - px > RunStartRange))
                {
                    continue;
                }

                var skills = situation.Skills[index];
                var score = skills.Pace + skills.Acceleration + skills.Positioning + skills.Anticipation
                    + (previous ? RunnerStickiness : 0)
                    + (spec.Family == MatchPositionFamily.Attack ? StrikerPreference : 0);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = index;
                }
            }

            if (best < 0)
            {
                return;
            }

            tried[best] = true;

            var py = MirrorY(TickSpatialUnits.ToUnits(situation.Attackers[best].Y), frame.IsHome);
            var gap = -1;
            var gapScore = int.MinValue;

            for (var edge = 0; edge < edgeCount - 1; edge++)
            {
                var width = edges[edge + 1] - edges[edge];
                var centre = (edges[edge] + edges[edge + 1]) / 2;
                var across = Math.Abs(centre - py);

                if (gapUsed[edge] || width < MinimumGap || across > RunLateralRange)
                {
                    continue;
                }

                var score = width - (across / 2);

                if (score > gapScore)
                {
                    gapScore = score;
                    gap = edge;
                }
            }

            if (gap < 0)
            {
                continue;
            }

            gapUsed[gap] = true;
            busy[best] = true;
            started++;

            var targetX = SpatialPitch.PitchLength - Math.Max(RunGoalGap, space - RunDepth);

            orders[best] = new TickAttackingOrder(
                TickAttackingRole.Runner,
                Mirror(targetX, (edges[gap] + edges[gap + 1]) / 2, situation.IsHome),
                RunPaceBasisPoints);
        }
    }

    private static void AssignPocket(in TickAttackingSituation situation, in Frame frame, Span<TickAttackingOrder> orders, Span<bool> busy)
    {
        if (frame.CarrierX + PocketCarrierGap > frame.LineX)
        {
            return;
        }

        var best = -1;
        var bestDistance = long.MaxValue;
        var minimum = (long)PocketMinimumDistance * PocketMinimumDistance;

        for (var index = 0; index < situation.Attackers.Length; index++)
        {
            if (busy[index] || situation.Specs[index].Family != MatchPositionFamily.Attack)
            {
                continue;
            }

            var distance = DistanceSquared(situation.Attackers[index], frame.IsHome, frame.CarrierX, frame.CarrierY);

            if (distance >= minimum && distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        if (best < 0)
        {
            return;
        }

        var x = frame.LineX - PocketDepth;

        if (x < frame.CarrierX + (PocketMinimumDistance / 2))
        {
            return;
        }

        var py = MirrorY(TickSpatialUnits.ToUnits(situation.Attackers[best].Y), frame.IsHome);
        var y = py + ((frame.CarrierY - py) * PocketPullPercent / 100);

        busy[best] = true;
        orders[best] = new TickAttackingOrder(TickAttackingRole.Pocket, Mirror(x, y, situation.IsHome), PocketPaceBasisPoints);
    }

    /// <summary>Holds every player left at his shape position no further up the pitch than the offside limit.</summary>
    private static void KeepOnside(in TickAttackingSituation situation, in Frame frame, Span<TickAttackingOrder> orders)
    {
        var limit = OnsideLimit(frame);

        for (var index = 0; index < situation.Attackers.Length; index++)
        {
            if (orders[index].Role != TickAttackingRole.Holding)
            {
                continue;
            }

            var target = orders[index].Target;

            if (MirrorX(target.X, frame.IsHome) > limit)
            {
                orders[index] = orders[index] with { Target = new SpatialPoint(MirrorX(limit, frame.IsHome), target.Y) };
            }
        }
    }

    /// <summary>Gets the farthest up the pitch an onside player stands: the deepest of the ball, the halfway line and the offside line.</summary>
    private static int OnsideLimit(in Frame frame) =>
        Math.Max(frame.CarrierX, Math.Max(SpatialPitch.PitchLength / 2, frame.LineX - OnsideMargin));

    private static SpatialPoint Mirror(int x, int y, bool isHome) =>
        new(MirrorX(x, isHome), MirrorY(y, isHome));

    /// <summary>Converts an X between the pitch's and the attacking side's own point of view (the conversion is its own inverse).</summary>
    private static int MirrorX(int units, bool isHome) => isHome ? units : SpatialPitch.PitchLength - units;

    private static int MirrorY(int units, bool isHome) => isHome ? units : SpatialPitch.PitchWidth - units;

    /// <summary>Gets the squared distance, in pitch units, from a player to a point given in the side's own point of view.</summary>
    private static long DistanceSquared(in TickPlayerState player, bool isHome, int x, int y)
    {
        long dx = MirrorX(TickSpatialUnits.ToUnits(player.X), isHome) - x;
        long dy = MirrorY(TickSpatialUnits.ToUnits(player.Y), isHome) - y;

        return (dx * dx) + (dy * dy);
    }

    /// <summary>Gets the squared distance from a point to a segment; the ball carrier's brain reads its lanes with it.</summary>
    internal static long SegmentDistanceSquared(long px, long py, long ax, long ay, long bx, long by)
    {
        var sx = bx - ax;
        var sy = by - ay;
        var length2 = (sx * sx) + (sy * sy);
        var along = length2 == 0 ? 0 : Math.Clamp(((px - ax) * sx) + ((py - ay) * sy), 0, length2);
        var nearestX = length2 == 0 ? ax : ax + (sx * along / length2);
        var nearestY = length2 == 0 ? ay : ay + (sy * along / length2);
        var dx = px - nearestX;
        var dy = py - nearestY;

        return (dx * dx) + (dy * dy);
    }

    /// <summary>What the assignment steps share about the moment: the carrier and the offside line, in the side's own point of view.</summary>
    private readonly record struct Frame(
        bool IsHome,
        int CarrierX,
        int CarrierY,
        int CarrierHeading,
        int LineX,
        int DefenderLine,
        long NearestDefenderSquared,
        int PresserX,
        int PresserY)
    {
        public static Frame Of(in TickAttackingSituation situation)
        {
            var carrier = situation.Attackers[situation.CarrierIndex];
            var line = TickSpatialUnits.ToUnits(TickDefensiveAI.OffsideLine(situation.Defenders, !situation.IsHome));
            var nearest = long.MaxValue;
            var presser = -1;

            for (var index = 0; index < situation.Defenders.Length; index++)
            {
                long dx = situation.Defenders[index].X - carrier.X;
                long dy = situation.Defenders[index].Y - carrier.Y;
                var distance = (dx * dx) + (dy * dy);

                if (distance < nearest)
                {
                    nearest = distance;
                    presser = index;
                }
            }

            var carrierX = MirrorX(TickSpatialUnits.ToUnits(carrier.X), situation.IsHome);
            var carrierY = MirrorY(TickSpatialUnits.ToUnits(carrier.Y), situation.IsHome);

            return new Frame(
                situation.IsHome,
                carrierX,
                carrierY,
                carrier.Heading,
                SpatialPitch.PitchLength - line,
                line,
                nearest,
                presser < 0 ? carrierX - 1 : MirrorX(TickSpatialUnits.ToUnits(situation.Defenders[presser].X), situation.IsHome),
                presser < 0 ? carrierY : MirrorY(TickSpatialUnits.ToUnits(situation.Defenders[presser].Y), situation.IsHome));
        }
    }
}
