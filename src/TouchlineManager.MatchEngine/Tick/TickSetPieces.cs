using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// Who takes a restart and who he is playing it to.
/// </summary>
/// <param name="TakerIndex">The taker's index in his side.</param>
/// <param name="OptionIndex">
/// The teammate set up to receive the short ball (the kick-off partner, the goal kick's split centre-back, the throw-in's
/// nearest outlet, the short-corner man), or -1.
/// </param>
internal readonly record struct TickSetPiecePlan(int TakerIndex, int OptionIndex);

/// <summary>
/// Everything the set-piece code reads about one restart. Index 0 of both sides is the goalkeeper.
/// </summary>
internal readonly ref struct TickSetPieceSituation
{
    /// <summary>Gets the restart.</summary>
    public required TickRestart Restart { get; init; }

    /// <summary>Gets the taking side's players, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Takers { get; init; }

    /// <summary>Gets the taking side's board positions, in the same order.</summary>
    public required ReadOnlySpan<TickAnchorSpec> TakerSpecs { get; init; }

    /// <summary>Gets the taking side's skills, in the same order.</summary>
    public required ReadOnlySpan<TickPlayerSkills> TakerSkills { get; init; }

    /// <summary>Gets the taking side's style.</summary>
    public required TickTeamStyle TakerStyle { get; init; }

    /// <summary>Gets the taking side's passing style, which decides short or long from a goal kick and a short corner.</summary>
    public required MatchPassingStyle TakerPassing { get; init; }

    /// <summary>Gets the other side's players, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Others { get; init; }

    /// <summary>Gets the other side's board positions, in the same order.</summary>
    public required ReadOnlySpan<TickAnchorSpec> OtherSpecs { get; init; }

    /// <summary>Gets the other side's skills, in the same order.</summary>
    public required ReadOnlySpan<TickPlayerSkills> OtherSkills { get; init; }

    /// <summary>Gets the other side's style.</summary>
    public required TickTeamStyle OtherStyle { get; init; }
}

/// <summary>
/// Where the 22 players stand for each dead-ball restart, who takes it and what he does with it (`tick-engine-v1`, Milestone 7).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Place"/> starts from the ordinary dynamic anchors of M2 (the ball on the restart spot, the taking side in
/// possession) and then lays the restart's own shape over them. Everything is worked out in the <em>taker's</em> point of
/// view (he attacks towards X = 10,000 and counts Y from the pitch's left, rotated for the away side) and turned back at
/// the end, and uses the anchors rather than where the players happen to be, so the answer is the same on every tick of the
/// hold and the taker never changes his mind. Every number is an integer in pitch units; every buffer is on the stack.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Kick-off.</b> Both sides in their own halves. The striker nearest the middle takes it from the centre spot with a
/// partner a few metres behind him; the opposition stay outside the 9.15 m circle.
/// </description></item>
/// <item><description>
/// <b>Goal kick.</b> The goalkeeper takes it. The centre-backs split wide of the box at 15 m either side of the middle, the
/// best passer of them the short option; the opposition are kept out of the penalty area.
/// </description></item>
/// <item><description>
/// <b>Throw-in.</b> The nearest outfield player throws; two teammates open an outlet 7 m and 15 m infield, and the nearest
/// opponents mark them goal-side. Nobody stands within 2.5 m of the thrower.
/// </description></item>
/// <item><description>
/// <b>Corner.</b> The best of the four nearest players (<c>2 × SetPieces + Crossing</c>) takes it. The five best headers
/// (<c>2 × Heading + JumpingReach</c>) take the penalty spot, six-yard centre, both posts' zones and the edge of the area; the
/// other side puts its two most advanced men up the pitch for the counter, two on the posts and marks the rest in order of
/// header, best on best; the next-nearest attacker offers a short corner. The attacking side's remaining players hold the edge
/// of the area, the defending side stays 9.15 m from the ball.
/// </description></item>
/// <item><description>
/// <b>Free kick.</b> Within <see cref="FreeKickShootRange"/> and 45° of the goal's axis the best striker of the ball
/// (<c>2 × SetPieces + Technique + Finishing</c>) shoots; a wall of 2 to 5 stands 9.15 m out, covering the near post, and the
/// keeper takes the far side. Further out and wide it is a cross, with the corner's header-and-marker shape; elsewhere the
/// nearest good passer plays it short. The defence stays 9.15 m away.
/// </description></item>
/// <item><description>
/// <b>Penalty.</b> The best taker (<c>2 × SetPieces + Finishing + Composure</c>) stands behind the spot, the keeper on his line,
/// and everyone else outside the area and the 9.15 m arc.
/// </description></item>
/// </list>
/// <para>
/// <see cref="Decide"/> then gives the taker his kick as an ordinary <see cref="TickCarrierDecision"/>, with no random draw:
/// the kick-off tap back, the short or long goal kick, the throw to the freest outlet, the cross to the header with the fewest
/// defenders on him, the penalty into the corner away from the keeper. <see cref="Execute"/> hits it, taking one dispersion
/// draw as <see cref="TickBallCarrierBrain.Execute"/> does, except that a free-kick shot is <em>lofted</em> over the wall.
/// </para>
/// </remarks>
internal static class TickSetPieces
{
    /// <summary>The farthest a side's players go up the pitch at a kick-off, in the taker's X (just short of the halfway line).</summary>
    public const int OwnHalfLimit = 4_850;

    /// <summary>The radius the opposition keep from the centre spot at a kick-off, in pitch units (9.15 m and a little more).</summary>
    public const int CentreCircleRadius = 950;

    /// <summary>The radius the opposition keep from the ball at a free kick, corner or penalty, in pitch units.</summary>
    public const int ClearanceRadius = 900;

    /// <summary>How far behind the ball the kick-off taker stands, in pitch units.</summary>
    public const int KickOffStandBack = 30;

    /// <summary>How far behind and beside the taker the kick-off partner stands, in pitch units.</summary>
    public const int PartnerBack = 220;

    /// <summary>How far beside the taker the kick-off partner stands, in pitch units.</summary>
    public const int PartnerSide = 200;

    /// <summary>How far behind the ball a goalkeeper stands to take a goal kick, in pitch units.</summary>
    public const int KeeperStandBack = 120;

    /// <summary>The X a split centre-back takes up at a goal kick, in pitch units: just outside the penalty area.</summary>
    public const int SplitCentreBackX = 1_900;

    /// <summary>How far either side of the middle a split centre-back stands at a goal kick, in pitch units (16 m).</summary>
    public const int SplitCentreBackOffset = 1_500;

    /// <summary>A defender whose anchor is this near the middle (in pitch units) counts as a centre-back for the goal kick split.</summary>
    public const int CentreBackBand = 1_300;

    /// <summary>The X an opponent is pushed to when he stands in the goal kick's penalty area, in pitch units.</summary>
    public const int AreaExit = 1_750;

    /// <summary>How far inside the line the thrower stands, in pitch units.</summary>
    public const int ThrowerInset = 40;

    /// <summary>The nearest an opponent stands to the thrower, in pitch units (2.5 m).</summary>
    public const int ThrowInClearance = 250;

    /// <summary>How far goal-side of his outlet the marker stands, in pitch units.</summary>
    public const int OutletMarkerGap = 200;

    /// <summary>The nearest outlet: ahead of and infield from the thrower (X, Y), in pitch units.</summary>
    public static readonly SpatialPoint ShortOutlet = new(300, 700);

    /// <summary>The farther outlet: ahead of and infield from the thrower (X, Y), in pitch units.</summary>
    public static readonly SpatialPoint LongOutlet = new(900, 1_500);

    /// <summary>How little a metre gained up the pitch is worth against a metre of space, as a divisor: space (up to 12 m) counts six times as much.</summary>
    public const int OutletGainDivisor = 6;

    /// <summary>The farthest a throw is aimed, in pitch units (26 m).</summary>
    public const int ThrowRange = 2_500;

    /// <summary>The farthest from the ball a free-kick taker may be (by anchor), in pitch units (37 m).</summary>
    public const int TakerRange = 3_500;

    /// <summary>The farthest a free kick is shot from, in pitch units (31 m).</summary>
    public const int FreeKickShootRange = 3_000;

    /// <summary>The X, in the taker's view, from which a free kick that is not a shot is crossed.</summary>
    public const int CrossFreeKickX = 6_500;

    /// <summary>How far the wall stands from the ball, in pitch units (9.45 m).</summary>
    public const int WallDistance = 900;

    /// <summary>The gap between wall players, in pitch units.</summary>
    public const int WallSpacing = 150;

    /// <summary>How far to the near post's side of the goal's middle the wall is lined up, in pitch units.</summary>
    public const int WallCover = 250;

    /// <summary>How far the free-kick taker stands behind the ball, in pitch units.</summary>
    public const int FreeKickStandBack = 150;

    /// <summary>How far in front of the line the keeper stands behind a wall, in pitch units.</summary>
    public const int WallKeeperDepth = 150;

    /// <summary>The peak height of a free-kick shot, in Z units: high enough to clear the wall, low enough to dip under the bar.</summary>
    public const int FreeKickApex = 44;

    /// <summary>The raw pressure on a free-kick shot, in basis points (the crowd and the wall).</summary>
    public const int FreeKickPressure = 1_500;

    /// <summary>The raw pressure on a penalty, in basis points: it is composure that eases it.</summary>
    public const int PenaltyPressure = 4_500;

    /// <summary>How far inside the post a penalty is aimed, in pitch units.</summary>
    public const int PenaltyAimOffset = 120;

    /// <summary>How far behind the spot the penalty taker stands, in pitch units.</summary>
    public const int PenaltyStandBack = 150;

    /// <summary>The attackers who go up for a corner.</summary>
    public const int CornerHeaders = 5;

    /// <summary>The attackers who go up for a crossed free kick.</summary>
    public const int FreeKickHeaders = 4;

    /// <summary>The defenders kept up the pitch for the counter-attack at a corner.</summary>
    public const int CounterReserve = 2;

    /// <summary>How far goal-side of his man a marker stands in the box, in pitch units.</summary>
    public const int BoxMarkerGap = 180;

    /// <summary>The farthest up the pitch a free attacker waits at a corner, in pitch units: the edge of the area.</summary>
    public const int RestDefenceLimit = 6_800;

    /// <summary>How far a corner taker stands off the ball, in pitch units, along each axis.</summary>
    public const int CornerTakerOffset = 90;

    /// <summary>Where the short-corner man waits: back along the touchline and infield from the flag (X back, Y in), in pitch units.</summary>
    public static readonly SpatialPoint ShortCornerOffset = new(750, 400);

    /// <summary>The number of defenders (or attackers) within this distance of a header who count as crowding him, in pitch units.</summary>
    public const int CrowdReach = TickBallCarrierBrain.CrossCrowdReach;

    /// <summary>The farthest from the spot a taker must be to count as at the ball, in pitch units (2.5 m).</summary>
    public const int ReadyDistance = 250;

    private const int TeamSize = TickTacticalGeometry.TeamSize;
    private const int Margin = 100;
    private const int Middle = SpatialPitch.GoalYCenter;
    private const int PitchLength = SpatialPitch.PitchLength;
    private const int BoxEdgeX = PitchLength - SpatialPitch.PenaltyBoxWidth;
    private const int MaxSlots = CornerHeaders;
    private const int PenaltyKeeperX = PitchLength - 50;
    private const int PostX = PitchLength - 120;
    private const int PostOffset = 300;
    private const int KeeperLineX = PitchLength - 200;
    private const int KeeperLean = 60;

    /// <summary>The box slots: (X, offset towards the taking side's flank), for the penalty spot, six-yard centre, far post, near post and the edge of the area.</summary>
    private static readonly SpatialPoint[] BoxSlots =
    [
        new(8_900, 0),
        new(9_500, 0),
        new(9_450, -700),
        new(9_450, 550),
        new(8_350, -250),
    ];

    private static readonly int ReadySpeed = TickSpatialUnits.SpeedToFixedPerTick(200);

    /// <summary>Tells whether a taker is standing at the ball and still enough to strike it.</summary>
    /// <param name="taker">The taker.</param>
    /// <param name="ballX">The ball's X, in fixed units.</param>
    /// <param name="ballY">The ball's Y, in fixed units.</param>
    public static bool IsTakerReady(in TickPlayerState taker, int ballX, int ballY)
    {
        long dx = taker.X - ballX;
        long dy = taker.Y - ballY;
        var limit = (long)TickSpatialUnits.ToFixed(ReadyDistance);

        return (dx * dx) + (dy * dy) <= limit * limit && taker.Speed <= ReadySpeed;
    }

    /// <summary>
    /// Tells whether a free kick is a shot: within <see cref="FreeKickShootRange"/> of the goal's middle and no wider than 45° of its axis.
    /// </summary>
    /// <param name="x">The spot's X, in the taker's view.</param>
    /// <param name="y">The spot's Y, in the taker's view.</param>
    public static bool IsShootingRange(int x, int y)
    {
        long dx = PitchLength - x;
        long dy = Math.Abs(y - Middle);

        return dx > 0 && dx >= dy && (dx * dx) + (dy * dy) <= (long)FreeKickShootRange * FreeKickShootRange;
    }

    /// <summary>Gets the number of players in a free-kick wall: 3, one more inside 22 m, one fewer when the kick is wide.</summary>
    /// <param name="x">The spot's X, in the taker's view.</param>
    /// <param name="y">The spot's Y, in the taker's view.</param>
    public static int WallSize(int x, int y)
    {
        var distance = (int)SpatialMath.Sqrt((long)(PitchLength - x) * (PitchLength - x) + ((long)(y - Middle) * (y - Middle)));
        var size = 3 + (distance <= 2_200 ? 1 : 0) - (Math.Abs(y - Middle) >= 1_600 ? 1 : 0);

        return Math.Clamp(size, 2, 5);
    }

    /// <summary>Gets a header's rank for a corner: <c>2 × Heading + JumpingReach</c>.</summary>
    /// <param name="skills">The player's skills.</param>
    public static int HeaderRank(in TickPlayerSkills skills) => (2 * skills.Heading) + skills.JumpingReach;

    /// <summary>
    /// Gets the order players are picked in for the box: the header's rank, and between equals the player whose job it is (a
    /// forward before a midfielder before a defender when attacking, the reverse when defending).
    /// </summary>
    private static int BoxKey(in TickPlayerSkills skills, MatchPositionFamily family, bool attacking)
    {
        var job = family switch
        {
            MatchPositionFamily.Attack => attacking ? 3 : 1,
            MatchPositionFamily.Midfield => 2,
            _ => attacking ? 1 : 3,
        };

        return (HeaderRank(skills) * 4) + job;
    }

    /// <summary>
    /// Places the 22 players for a restart and says who takes it. Call it every tick of the hold; it answers the same each time.
    /// </summary>
    /// <param name="situation">The restart and the two sides.</param>
    /// <param name="takerTargets">Receives where each of the taking side's players goes, in pitch units.</param>
    /// <param name="otherTargets">Receives where each of the other side's players goes, in pitch units.</param>
    /// <returns>The taker and the short option.</returns>
    public static TickSetPiecePlan Place(in TickSetPieceSituation situation, Span<SpatialPoint> takerTargets, Span<SpatialPoint> otherTargets)
    {
        var count = situation.TakerSpecs.Length;
        var otherCount = situation.OtherSpecs.Length;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, TeamSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(otherCount, TeamSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(takerTargets.Length, count);
        ArgumentOutOfRangeException.ThrowIfLessThan(otherTargets.Length, otherCount);

        var home = situation.Restart.TakerIsHome;
        var spotX = situation.Restart.SpotX;
        var spotY = situation.Restart.SpotY;
        Span<SpatialPoint> own = stackalloc SpatialPoint[TeamSize];
        Span<SpatialPoint> opp = stackalloc SpatialPoint[TeamSize];

        for (var index = 0; index < count; index++)
        {
            own[index] = Flip(home, TickTacticalGeometry.Resolve(situation.TakerSpecs[index], situation.TakerStyle, home, true, spotX, spotY));
        }

        for (var index = 0; index < otherCount; index++)
        {
            opp[index] = Flip(home, TickTacticalGeometry.Resolve(situation.OtherSpecs[index], situation.OtherStyle, !home, false, spotX, spotY));
        }

        var layout = new Layout
        {
            Own = own[..count],
            Opp = opp[..otherCount],
            OwnSpecs = situation.TakerSpecs,
            OwnSkills = situation.TakerSkills,
            OppSpecs = situation.OtherSpecs,
            OppSkills = situation.OtherSkills,
            Spot = Flip(home, new SpatialPoint(spotX, spotY)),
        };

        var plan = situation.Restart.Kind switch
        {
            TickRestartKind.KickOff => LayOutKickOff(layout),
            TickRestartKind.GoalKick => LayOutGoalKick(layout),
            TickRestartKind.ThrowIn => LayOutThrowIn(layout),
            TickRestartKind.Corner => LayOutCorner(layout),
            TickRestartKind.FreeKick => LayOutFreeKick(layout),
            _ => LayOutPenalty(layout),
        };

        for (var index = 0; index < count; index++)
        {
            takerTargets[index] = Flip(home, own[index]);
        }

        for (var index = 0; index < otherCount; index++)
        {
            otherTargets[index] = Flip(home, opp[index]);
        }

        return plan;
    }

    /// <summary>Gives the taker his kick. There is no random draw here; <see cref="Execute"/> takes the one draw.</summary>
    /// <param name="situation">The restart and the two sides, with the players where they now stand.</param>
    /// <param name="plan">The plan <see cref="Place"/> returned.</param>
    /// <returns>The decision, with its target in absolute pitch units.</returns>
    public static TickCarrierDecision Decide(in TickSetPieceSituation situation, in TickSetPiecePlan plan)
    {
        var count = situation.Takers.Length;
        var otherCount = situation.Others.Length;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, TeamSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(otherCount, TeamSize);
        ArgumentOutOfRangeException.ThrowIfNegative(plan.TakerIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(plan.TakerIndex, count);

        var home = situation.Restart.TakerIsHome;
        Span<SpatialPoint> mine = stackalloc SpatialPoint[TeamSize];
        Span<SpatialPoint> theirs = stackalloc SpatialPoint[TeamSize];

        for (var index = 0; index < count; index++)
        {
            mine[index] = Flip(home, new SpatialPoint(TickSpatialUnits.ToUnits(situation.Takers[index].X), TickSpatialUnits.ToUnits(situation.Takers[index].Y)));
        }

        for (var index = 0; index < otherCount; index++)
        {
            theirs[index] = Flip(home, new SpatialPoint(TickSpatialUnits.ToUnits(situation.Others[index].X), TickSpatialUnits.ToUnits(situation.Others[index].Y)));
        }

        var view = new Layout
        {
            Own = mine[..count],
            Opp = theirs[..otherCount],
            OwnSpecs = situation.TakerSpecs,
            OwnSkills = situation.TakerSkills,
            OppSpecs = situation.OtherSpecs,
            OppSkills = situation.OtherSkills,
            Spot = Flip(home, new SpatialPoint(situation.Restart.SpotX, situation.Restart.SpotY)),
        };

        var composure = situation.TakerSkills[plan.TakerIndex].Composure;
        var decision = situation.Restart.Kind switch
        {
            TickRestartKind.KickOff => PlayShort(view, plan, 0),
            TickRestartKind.GoalKick => DecideGoalKick(view, plan, situation.TakerPassing),
            TickRestartKind.ThrowIn => DecideOutlet(view, plan, ThrowRange),
            TickRestartKind.Corner => DecideCorner(view, plan, situation.TakerPassing),
            TickRestartKind.FreeKick => DecideFreeKick(view, plan, composure),
            _ => DecidePenalty(view, plan, composure),
        };

        return decision with { Target = Flip(home, decision.Target) };
    }

    /// <summary>
    /// Hits the restart. Everything goes through <see cref="TickBallCarrierBrain.Execute"/> except a free-kick shot, which is
    /// lofted to <see cref="FreeKickApex"/> so that it passes over the wall. One dispersion draw is taken for every kick.
    /// </summary>
    /// <param name="kind">The kind of restart.</param>
    /// <param name="decision">The taker's decision.</param>
    /// <param name="skills">The taker's skills.</param>
    /// <param name="ball">The ball, on the restart spot.</param>
    /// <param name="random">The play stream.</param>
    /// <returns>True when the ball was kicked.</returns>
    public static bool Execute(
        TickRestartKind kind,
        in TickCarrierDecision decision,
        in TickPlayerSkills skills,
        TickBallPhysics ball,
        Pcg32 random)
    {
        if (kind != TickRestartKind.FreeKick || decision.Action != TickCarrierAction.Shoot)
        {
            return TickBallCarrierBrain.Execute(decision, skills, ball, random, TickBallCarrierBrain.SetPieceShotBaseError);
        }

        ArgumentNullException.ThrowIfNull(ball);
        ArgumentNullException.ThrowIfNull(random);

        var error = TickBallCarrierBrain.ErrorAngle(skills.SetPieces, skills.Technique, TickBallCarrierBrain.SetPieceShotBaseError, decision.EffectivePressure);
        var turn = random.NextRange(-error, error);
        var fromX = ball.UnitX;
        var fromY = ball.UnitY;
        long dx = decision.Target.X - fromX;
        long dy = decision.Target.Y - fromY;
        var cos = TickTrigonometry.Cos(turn);
        var sin = TickTrigonometry.Sin(turn);

        ball.LaunchLofted(
            fromX + (int)(((dx * cos) - (dy * sin)) / TickTrigonometry.Scale),
            fromY + (int)(((dx * sin) + (dy * cos)) / TickTrigonometry.Scale),
            FreeKickApex);

        return true;
    }

    // ---- Layouts -------------------------------------------------------------------------------------------------------------

    private static TickSetPiecePlan LayOutKickOff(in Layout layout)
    {
        var own = layout.Own;
        var opp = layout.Opp;
        var spot = layout.Spot;
        var count = own.Length;

        for (var index = 0; index < count; index++)
        {
            own[index] = new SpatialPoint(Math.Min(own[index].X, OwnHalfLimit), own[index].Y);
        }

        for (var index = 0; index < opp.Length; index++)
        {
            var point = new SpatialPoint(Math.Max(opp[index].X, PitchLength - OwnHalfLimit), opp[index].Y);
            var dy = Math.Abs(point.Y - spot.Y);

            if (dy < CentreCircleRadius && Distance(point, spot) < CentreCircleRadius)
            {
                point = new SpatialPoint(spot.X + (int)SpatialMath.Sqrt(((long)CentreCircleRadius * CentreCircleRadius) - ((long)dy * dy)), point.Y);
            }

            opp[index] = point;
        }

        Span<int> keys = stackalloc int[TeamSize];
        Span<bool> used = stackalloc bool[TeamSize];

        // The taker: the forward nearest the middle of the pitch, or failing that the most advanced outfield player.
        for (var index = 0; index < count; index++)
        {
            keys[index] = index == 0 ? int.MaxValue
                : layout.OwnSpecs[index].Family == MatchPositionFamily.Attack ? Math.Abs(layout.OwnSpecs[index].OwnY - Middle)
                : int.MaxValue - 1;
        }

        var taker = PickMin(keys, used, count);

        if (taker < 0)
        {
            return new TickSetPiecePlan(0, -1);
        }

        used[taker] = true;

        // His partner: another forward, or the midfielder nearest the middle.
        for (var index = 0; index < count; index++)
        {
            var spec = layout.OwnSpecs[index];

            keys[index] = index == 0 ? int.MaxValue
                : (spec.Family == MatchPositionFamily.Attack ? 0 : 100_000)
                    + (int)Distance(new SpatialPoint(spec.OwnX, spec.OwnY), SpatialPoint.Center);
        }

        var partner = PickMin(keys, used, count);

        own[taker] = new SpatialPoint(spot.X - KickOffStandBack, spot.Y);

        if (partner >= 0)
        {
            var side = layout.OwnSpecs[partner].OwnY >= Middle ? 1 : -1;

            own[partner] = new SpatialPoint(spot.X - PartnerBack, spot.Y + (side * PartnerSide));
        }

        return new TickSetPiecePlan(taker, partner);
    }

    private static TickSetPiecePlan LayOutGoalKick(in Layout layout)
    {
        var own = layout.Own;
        var opp = layout.Opp;
        var spot = layout.Spot;

        own[0] = new SpatialPoint(Math.Max(Margin, spot.X - KeeperStandBack), spot.Y);

        // The centre-backs split wide of the area; the better passer of them is the short option.
        var option = -1;
        var optionPassing = int.MinValue;
        var lastSide = 0;

        for (var index = 1; index < own.Length; index++)
        {
            var spec = layout.OwnSpecs[index];

            if (spec.Family != MatchPositionFamily.Defence || Math.Abs(spec.OwnY - Middle) > CentreBackBand)
            {
                continue;
            }

            var side = spec.OwnY < Middle ? -1 : (spec.OwnY > Middle ? 1 : (lastSide == 0 ? -1 : -lastSide));

            if (side == lastSide)
            {
                side = -side;
            }

            lastSide = side;
            own[index] = new SpatialPoint(SplitCentreBackX, Middle + (side * SplitCentreBackOffset));

            if (layout.OwnSkills[index].Passing > optionPassing)
            {
                optionPassing = layout.OwnSkills[index].Passing;
                option = index;
            }
        }

        for (var index = 0; index < opp.Length; index++)
        {
            if (opp[index].X <= SpatialPitch.PenaltyBoxWidth && opp[index].Y >= SpatialPitch.PenaltyBoxYMin && opp[index].Y <= SpatialPitch.PenaltyBoxYMax)
            {
                opp[index] = new SpatialPoint(AreaExit, opp[index].Y);
            }
        }

        return new TickSetPiecePlan(0, option);
    }

    private static TickSetPiecePlan LayOutThrowIn(in Layout layout)
    {
        var own = layout.Own;
        var opp = layout.Opp;
        var spot = layout.Spot;
        var inward = spot.Y < Middle ? 1 : -1;
        Span<int> keys = stackalloc int[TeamSize];
        Span<bool> used = stackalloc bool[TeamSize];
        Span<bool> marked = stackalloc bool[TeamSize];

        for (var index = 0; index < own.Length; index++)
        {
            keys[index] = index == 0 ? int.MaxValue : (int)Distance(own[index], spot);
        }

        var taker = PickMin(keys, used, own.Length);

        if (taker < 0)
        {
            return new TickSetPiecePlan(0, -1);
        }

        used[taker] = true;

        var near = PickMin(keys, used, own.Length);

        if (near >= 0)
        {
            used[near] = true;
        }

        var far = PickMin(keys, used, own.Length);

        own[taker] = new SpatialPoint(spot.X, spot.Y + (inward * ThrowerInset));

        if (near >= 0)
        {
            own[near] = Inside(new SpatialPoint(spot.X + ShortOutlet.X, spot.Y + (inward * ShortOutlet.Y)));
            MarkOutlet(opp, marked, own[near]);
        }

        if (far >= 0)
        {
            own[far] = Inside(new SpatialPoint(spot.X + LongOutlet.X, spot.Y + (inward * LongOutlet.Y)));
            MarkOutlet(opp, marked, own[far]);
        }

        for (var index = 0; index < opp.Length; index++)
        {
            opp[index] = PushAway(opp[index], spot, ThrowInClearance);
        }

        return new TickSetPiecePlan(taker, near);
    }

    /// <summary>The nearest opponent (by anchor) not already marking an outlet takes up a goal-side mark of this one.</summary>
    private static void MarkOutlet(Span<SpatialPoint> opp, Span<bool> marked, SpatialPoint outlet)
    {
        var best = -1;
        var bestDistance = long.MaxValue;
        var mark = Inside(new SpatialPoint(outlet.X + OutletMarkerGap, outlet.Y));

        for (var index = 1; index < opp.Length; index++)
        {
            if (marked[index])
            {
                continue;
            }

            var distance = Distance(opp[index], outlet);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        if (best >= 0)
        {
            marked[best] = true;
            opp[best] = mark;
        }
    }

    private static TickSetPiecePlan LayOutCorner(in Layout layout)
    {
        var own = layout.Own;
        var spot = layout.Spot;
        var inward = spot.Y < Middle ? 1 : -1;
        Span<int> keys = stackalloc int[TeamSize];
        Span<bool> candidate = stackalloc bool[TeamSize];
        Span<bool> none = stackalloc bool[TeamSize];

        // The four nearest outfield players are the candidates; the best striker of a dead ball among them takes it.
        for (var index = 0; index < own.Length; index++)
        {
            keys[index] = index == 0 ? int.MaxValue : (int)Distance(own[index], spot);
        }

        var pool = Math.Min(4, own.Length - 1);

        for (var rank = 0; rank < pool; rank++)
        {
            var pick = PickMin(keys, candidate, own.Length);

            if (pick >= 0)
            {
                candidate[pick] = true;
            }
        }

        for (var index = 0; index < own.Length; index++)
        {
            keys[index] = index > 0 && candidate[index]
                ? -((2 * layout.OwnSkills[index].SetPieces) + layout.OwnSkills[index].Crossing)
                : int.MaxValue;
        }

        var taker = PickMin(keys, none, own.Length);

        if (taker < 0)
        {
            return new TickSetPiecePlan(0, -1);
        }

        var option = AssembleBox(layout, side: -inward, taker, CornerHeaders, corner: true);

        own[taker] = new SpatialPoint(spot.X - CornerTakerOffset, spot.Y + (inward * CornerTakerOffset));

        if (option >= 0)
        {
            own[option] = new SpatialPoint(spot.X - ShortCornerOffset.X, spot.Y + (inward * ShortCornerOffset.Y));
        }

        KeepClear(layout.Opp, spot, ClearanceRadius);

        return new TickSetPiecePlan(taker, option);
    }

    private static TickSetPiecePlan LayOutFreeKick(in Layout layout)
    {
        var own = layout.Own;
        var opp = layout.Opp;
        var spot = layout.Spot;
        var shot = IsShootingRange(spot.X, spot.Y);
        var cross = !shot && spot.X >= CrossFreeKickX;
        Span<int> keys = stackalloc int[TeamSize];
        Span<bool> none = stackalloc bool[TeamSize];

        for (var index = 0; index < own.Length; index++)
        {
            var skills = layout.OwnSkills[index];
            var score = shot ? (2 * skills.SetPieces) + skills.Technique + skills.Finishing
                : cross ? (2 * skills.SetPieces) + skills.Crossing + skills.Technique
                : (2 * skills.SetPieces) + skills.Passing;

            keys[index] = index > 0 && Distance(own[index], spot) <= TakerRange ? -score : int.MaxValue;
        }

        var taker = PickMin(keys, none, own.Length);

        if (taker < 0)
        {
            for (var index = 0; index < own.Length; index++)
            {
                keys[index] = index == 0 ? int.MaxValue : (int)Distance(own[index], spot);
            }

            taker = PickMin(keys, none, own.Length);
        }

        if (taker < 0)
        {
            return new TickSetPiecePlan(0, -1);
        }

        if (shot)
        {
            LayOutWall(layout, taker);

            return new TickSetPiecePlan(taker, -1);
        }

        if (cross)
        {
            AssembleBox(layout, side: spot.Y < Middle ? -1 : 1, taker, FreeKickHeaders, corner: false);
        }

        own[taker] = new SpatialPoint(spot.X - FreeKickStandBack, spot.Y);
        KeepClear(opp, spot, ClearanceRadius);

        return new TickSetPiecePlan(taker, -1);
    }

    private static void LayOutWall(in Layout layout, int taker)
    {
        var own = layout.Own;
        var opp = layout.Opp;
        var spot = layout.Spot;
        var nearSign = spot.Y <= Middle ? -1 : 1;
        var size = WallSize(spot.X, spot.Y);
        Span<int> keys = stackalloc int[TeamSize];
        Span<bool> inWall = stackalloc bool[TeamSize];

        // The taker steps back along the line to goal.
        var goal = new SpatialPoint(PitchLength, Middle);
        var toGoal = Distance(spot, goal);

        own[taker] = new SpatialPoint(
            spot.X - (int)(((long)(goal.X - spot.X) * FreeKickStandBack) / Math.Max(1, toGoal)),
            spot.Y - (int)(((long)(goal.Y - spot.Y) * FreeKickStandBack) / Math.Max(1, toGoal)));

        // The wall is lined up on the near post's side of the goal: the keeper takes the far one.
        var cover = new SpatialPoint(PitchLength, Middle + (nearSign * WallCover));
        var length = Math.Max(1, Distance(spot, cover));
        var ux = (int)((cover.X - spot.X) * 1_000L / length);
        var uy = (int)((cover.Y - spot.Y) * 1_000L / length);
        var centreX = spot.X + (ux * WallDistance / 1_000);
        var centreY = spot.Y + (uy * WallDistance / 1_000);

        for (var index = 0; index < opp.Length; index++)
        {
            keys[index] = index == 0 ? int.MaxValue : (int)Distance(opp[index], spot);
        }

        for (var member = 0; member < size; member++)
        {
            var pick = PickMin(keys, inWall, opp.Length);

            if (pick < 0)
            {
                break;
            }

            inWall[pick] = true;

            var offset = ((2 * member) - (size - 1)) * WallSpacing / 2;

            opp[pick] = Inside(new SpatialPoint(centreX - (uy * offset / 1_000), centreY + (ux * offset / 1_000)));
        }

        opp[0] = new SpatialPoint(PitchLength - WallKeeperDepth, Middle - (nearSign * WallCover));

        for (var index = 1; index < opp.Length; index++)
        {
            if (!inWall[index])
            {
                opp[index] = PushAway(opp[index], spot, ClearanceRadius);
            }
        }
    }

    private static TickSetPiecePlan LayOutPenalty(in Layout layout)
    {
        var own = layout.Own;
        var opp = layout.Opp;
        var spot = layout.Spot;
        Span<int> keys = stackalloc int[TeamSize];
        Span<bool> none = stackalloc bool[TeamSize];

        for (var index = 0; index < own.Length; index++)
        {
            var skills = layout.OwnSkills[index];

            keys[index] = index == 0 ? int.MaxValue : -((2 * skills.SetPieces) + skills.Finishing + skills.Composure);
        }

        var taker = PickMin(keys, none, own.Length);

        if (taker < 0)
        {
            return new TickSetPiecePlan(0, -1);
        }

        for (var index = 1; index < own.Length; index++)
        {
            own[index] = KeepOutOfPenalty(own[index], spot);
        }

        for (var index = 1; index < opp.Length; index++)
        {
            opp[index] = KeepOutOfPenalty(opp[index], spot);
        }

        own[taker] = new SpatialPoint(spot.X - PenaltyStandBack, spot.Y);

        if (opp.Length > 0)
        {
            opp[0] = new SpatialPoint(PenaltyKeeperX, Middle);
        }

        return new TickSetPiecePlan(taker, -1);
    }

    /// <summary>
    /// Puts the taking side's headers into the box and the other side's markers goal-side of them. Returns the short option.
    /// </summary>
    private static int AssembleBox(in Layout layout, int side, int taker, int slots, bool corner)
    {
        var own = layout.Own;
        var opp = layout.Opp;
        var spot = layout.Spot;
        Span<int> keys = stackalloc int[TeamSize];
        Span<bool> used = stackalloc bool[TeamSize];
        Span<bool> oppUsed = stackalloc bool[TeamSize];
        Span<int> owner = stackalloc int[MaxSlots];

        for (var index = 0; index < own.Length; index++)
        {
            keys[index] = index == 0 || index == taker ? int.MaxValue : -BoxKey(layout.OwnSkills[index], layout.OwnSpecs[index].Family, attacking: true);
        }

        for (var slot = 0; slot < slots; slot++)
        {
            var pick = PickMin(keys, used, own.Length);

            owner[slot] = pick;

            if (pick >= 0)
            {
                used[pick] = true;
                own[pick] = BoxSlot(slot, side);
            }
        }

        // The next-nearest attacker offers the short corner; the rest wait at the edge of the area.
        var option = -1;

        if (corner)
        {
            for (var index = 0; index < own.Length; index++)
            {
                keys[index] = index == 0 || index == taker ? int.MaxValue : (int)Distance(own[index], spot);
            }

            option = PickMin(keys, used, own.Length);

            if (option >= 0)
            {
                used[option] = true;
            }
        }

        for (var index = 1; index < own.Length; index++)
        {
            if (!used[index] && index != taker)
            {
                own[index] = new SpatialPoint(Math.Min(own[index].X, RestDefenceLimit), own[index].Y);
            }
        }

        // The other side: the most advanced men stay up for the counter, two stand on the posts, the rest mark by header.
        if (corner)
        {
            for (var rank = 0; rank < CounterReserve; rank++)
            {
                for (var index = 0; index < opp.Length; index++)
                {
                    keys[index] = index == 0 ? int.MaxValue : opp[index].X;
                }

                var pick = PickMin(keys, oppUsed, opp.Length);

                if (pick >= 0)
                {
                    oppUsed[pick] = true;
                    opp[pick] = new SpatialPoint(Math.Min(opp[pick].X, 6_500), opp[pick].Y);
                }
            }

            for (var post = 0; post < 2; post++)
            {
                for (var index = 0; index < opp.Length; index++)
                {
                    keys[index] = index == 0 ? int.MaxValue : BoxKey(layout.OppSkills[index], layout.OppSpecs[index].Family, attacking: false);
                }

                var pick = PickMin(keys, oppUsed, opp.Length);

                if (pick >= 0)
                {
                    oppUsed[pick] = true;
                    opp[pick] = new SpatialPoint(PostX, Middle + ((post == 0 ? side : -side) * PostOffset));
                }
            }
        }

        for (var slot = 0; slot < slots; slot++)
        {
            for (var index = 0; index < opp.Length; index++)
            {
                keys[index] = index == 0 ? int.MaxValue : -BoxKey(layout.OppSkills[index], layout.OppSpecs[index].Family, attacking: false);
            }

            var pick = PickMin(keys, oppUsed, opp.Length);

            if (pick < 0)
            {
                break;
            }

            oppUsed[pick] = true;

            var post = BoxSlot(slot, side);

            opp[pick] = owner[slot] >= 0
                ? new SpatialPoint(Math.Min(post.X + BoxMarkerGap, PitchLength - Margin), post.Y)
                : new SpatialPoint(9_350, Middle + (side * 550));
        }

        for (var index = 1; index < opp.Length; index++)
        {
            if (!oppUsed[index])
            {
                opp[index] = new SpatialPoint(Math.Max(opp[index].X, 8_000), opp[index].Y);
            }
        }

        if (opp.Length > 0)
        {
            opp[0] = new SpatialPoint(KeeperLineX, Middle + (side * KeeperLean));
        }

        return option;
    }

    private static SpatialPoint BoxSlot(int slot, int side) =>
        new(BoxSlots[slot].X, Middle + (side * BoxSlots[slot].Y));

    // ---- Decisions -----------------------------------------------------------------------------------------------------------

    private static TickCarrierDecision PlayShort(in Layout view, in TickSetPiecePlan plan, int pressure)
    {
        if (plan.OptionIndex < 0)
        {
            return new TickCarrierDecision(TickCarrierAction.Clear, -1, new SpatialPoint(6_000, Middle), 0, 0, pressure);
        }

        return new TickCarrierDecision(TickCarrierAction.Pass, plan.OptionIndex, view.Own[plan.OptionIndex], 0, 0, pressure);
    }

    private static TickCarrierDecision DecideGoalKick(in Layout view, in TickSetPiecePlan plan, MatchPassingStyle style)
    {
        var pressed = 0;

        for (var index = 1; index < view.Opp.Length; index++)
        {
            if (Distance(view.Opp[index], view.Spot) <= 2_500)
            {
                pressed++;
            }
        }

        var playShort = style switch
        {
            MatchPassingStyle.ShortPassing => true,
            MatchPassingStyle.DirectPassing => false,
            _ => pressed < 2,
        };

        if (playShort && plan.OptionIndex >= 0)
        {
            return PlayShort(view, plan, 0);
        }

        // Long: to the best header among the forwards (or, failing them, the midfield).
        var target = -1;
        var best = int.MinValue;

        for (var index = 1; index < view.Own.Length; index++)
        {
            var family = view.OwnSpecs[index].Family;

            if (family == MatchPositionFamily.Goalkeeper || family == MatchPositionFamily.Defence)
            {
                continue;
            }

            var rank = HeaderRank(view.OwnSkills[index]) + (family == MatchPositionFamily.Attack ? 1_000 : 0);

            if (rank > best)
            {
                best = rank;
                target = index;
            }
        }

        return target < 0
            ? new TickCarrierDecision(TickCarrierAction.Clear, -1, new SpatialPoint(6_000, Middle), 0, 0, 0)
            : new TickCarrierDecision(TickCarrierAction.Pass, target, view.Own[target], 0, 0, 0);
    }

    private static TickCarrierDecision DecideOutlet(in Layout view, in TickSetPiecePlan plan, int range)
    {
        var taker = view.Own[plan.TakerIndex];
        var receiver = -1;
        var best = int.MinValue;

        for (var index = 1; index < view.Own.Length; index++)
        {
            if (index == plan.TakerIndex || Distance(view.Own[index], taker) > range)
            {
                continue;
            }

            var space = 1_200;

            for (var other = 0; other < view.Opp.Length; other++)
            {
                space = (int)Math.Min(space, Distance(view.Opp[other], view.Own[index]));
            }

            var score = space + (Math.Clamp(view.Own[index].X - taker.X, -300, 900) / OutletGainDivisor);

            if (score > best)
            {
                best = score;
                receiver = index;
            }
        }

        if (receiver < 0)
        {
            receiver = plan.OptionIndex >= 0 ? plan.OptionIndex : 0;
        }

        return new TickCarrierDecision(TickCarrierAction.Pass, receiver, view.Own[receiver], 0, 0, 0);
    }

    private static TickCarrierDecision DecideCorner(in Layout view, in TickSetPiecePlan plan, MatchPassingStyle style)
    {
        if (style == MatchPassingStyle.ShortPassing && plan.OptionIndex >= 0)
        {
            var space = long.MaxValue;

            for (var index = 0; index < view.Opp.Length; index++)
            {
                space = Math.Min(space, Distance(view.Opp[index], view.Own[plan.OptionIndex]));
            }

            if (space > 1_000)
            {
                return PlayShort(view, plan, 0);
            }
        }

        return DecideCross(view, plan, 0);
    }

    private static TickCarrierDecision DecideCross(in Layout view, in TickSetPiecePlan plan, int pressure)
    {
        var receiver = -1;
        var best = int.MaxValue;

        for (var index = 1; index < view.Own.Length; index++)
        {
            var point = view.Own[index];

            if (index == plan.TakerIndex || point.X < BoxEdgeX || point.Y < SpatialPitch.PenaltyBoxYMin || point.Y > SpatialPitch.PenaltyBoxYMax)
            {
                continue;
            }

            var crowd = 0;

            for (var other = 1; other < view.Opp.Length; other++)
            {
                if (Distance(view.Opp[other], point) <= CrowdReach)
                {
                    crowd++;
                }
            }

            var key = (crowd * 1_000) - HeaderRank(view.OwnSkills[index]);

            if (key < best)
            {
                best = key;
                receiver = index;
            }
        }

        return receiver < 0
            ? new TickCarrierDecision(TickCarrierAction.Cross, -1, BoxSlot(0, 0), 0, 0, pressure)
            : new TickCarrierDecision(TickCarrierAction.Cross, receiver, view.Own[receiver], 0, 0, pressure);
    }

    private static TickCarrierDecision DecideFreeKick(in Layout view, in TickSetPiecePlan plan, int composure)
    {
        var spot = view.Spot;

        if (IsShootingRange(spot.X, spot.Y))
        {
            // At the far post, away from the wall's side.
            var nearSign = spot.Y <= Middle ? -1 : 1;
            var aim = new SpatialPoint(PitchLength + TickBallCarrierBrain.ShotOvershoot, Middle - (nearSign * (TickSpatialUnits.GoalMouthMaxUnits - Middle - TickBallCarrierBrain.ShotAimOffset)));

            return new TickCarrierDecision(TickCarrierAction.Shoot, -1, aim, 0, 0, TickBallCarrierBrain.EffectivePressure(FreeKickPressure, composure));
        }

        return spot.X >= CrossFreeKickX
            ? DecideCross(view, plan, 0)
            : DecideOutlet(view, plan, 3_500);
    }

    private static TickCarrierDecision DecidePenalty(in Layout view, in TickSetPiecePlan plan, int composure)
    {
        // Into the corner on the side the keeper is not standing.
        var keeperY = view.Opp.Length > 0 ? view.Opp[0].Y : Middle;
        var toHigh = keeperY <= Middle;
        var aimY = toHigh ? TickSpatialUnits.GoalMouthMaxUnits - PenaltyAimOffset : TickSpatialUnits.GoalMouthMinUnits + PenaltyAimOffset;
        var aim = new SpatialPoint(PitchLength + TickBallCarrierBrain.ShotOvershoot, aimY);

        return new TickCarrierDecision(TickCarrierAction.Shoot, -1, aim, 0, 0, TickBallCarrierBrain.EffectivePressure(PenaltyPressure, composure));
    }

    // ---- Geometry ------------------------------------------------------------------------------------------------------------

    /// <summary>Turns a point into the taker's view and back (the two are the same half-turn of the pitch).</summary>
    private static SpatialPoint Flip(bool takerIsHome, SpatialPoint point) =>
        takerIsHome ? point : new SpatialPoint(PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);

    private static long Distance(SpatialPoint one, SpatialPoint other)
    {
        long dx = one.X - other.X;
        long dy = one.Y - other.Y;

        return SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    private static SpatialPoint Inside(SpatialPoint point) =>
        new(Math.Clamp(point.X, Margin, PitchLength - Margin), Math.Clamp(point.Y, Margin, SpatialPitch.PitchWidth - Margin));

    /// <summary>Moves a point radially away from a centre until it is a radius from it, if it was closer.</summary>
    private static SpatialPoint PushAway(SpatialPoint point, SpatialPoint centre, int radius)
    {
        long dx = point.X - centre.X;
        long dy = point.Y - centre.Y;
        var distance = SpatialMath.Sqrt((dx * dx) + (dy * dy));

        if (distance >= radius)
        {
            return point;
        }

        if (distance == 0)
        {
            return Inside(new SpatialPoint(centre.X + radius, centre.Y));
        }

        return Inside(new SpatialPoint(
            centre.X + (int)(dx * radius / distance),
            centre.Y + (int)(dy * radius / distance)));
    }

    private static void KeepClear(Span<SpatialPoint> side, SpatialPoint centre, int radius)
    {
        for (var index = 0; index < side.Length; index++)
        {
            side[index] = PushAway(side[index], centre, radius);
        }
    }

    /// <summary>Moves a player out of the penalty area and the 9.15 m arc round the spot.</summary>
    private static SpatialPoint KeepOutOfPenalty(SpatialPoint point, SpatialPoint spot)
    {
        if (point.X >= BoxEdgeX && point.Y >= SpatialPitch.PenaltyBoxYMin && point.Y <= SpatialPitch.PenaltyBoxYMax)
        {
            point = new SpatialPoint(BoxEdgeX - 50, point.Y);
        }

        return PushAway(point, spot, ClearanceRadius);
    }

    /// <summary>Finds the lowest key among players not yet used, or -1 when every key is the maximum or the player is used. Ties go to the lower index.</summary>
    private static int PickMin(ReadOnlySpan<int> keys, ReadOnlySpan<bool> used, int count)
    {
        var best = -1;

        for (var index = 0; index < count; index++)
        {
            if (!used[index] && keys[index] != int.MaxValue && (best < 0 || keys[index] < keys[best]))
            {
                best = index;
            }
        }

        return best;
    }

    /// <summary>Both sides of a restart in the taker's view, and what the layouts read about them.</summary>
    private readonly ref struct Layout
    {
        public required Span<SpatialPoint> Own { get; init; }

        public required Span<SpatialPoint> Opp { get; init; }

        public required ReadOnlySpan<TickAnchorSpec> OwnSpecs { get; init; }

        public required ReadOnlySpan<TickPlayerSkills> OwnSkills { get; init; }

        public required ReadOnlySpan<TickAnchorSpec> OppSpecs { get; init; }

        public required ReadOnlySpan<TickPlayerSkills> OppSkills { get; init; }

        public required SpatialPoint Spot { get; init; }
    }
}
