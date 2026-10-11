using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// Combination play for the tick engine (`tick-film-v1`, Milestone 7): who runs, and where, when a short pass finds a team-mate who is
/// being closed down.
/// </summary>
/// <remarks>
/// <para>
/// The loop asks as a pass leaves the foot. Two things can follow, and they are independent:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>The return run</b> (<see cref="TryReturnRun"/>, the first half of a one-two). A midfielder or forward who has played a short ball
/// (under 15 m) to a team-mate with a defender on him runs past that defender, to a point 7 m beyond him and 4 m to the side the
/// passer is on, within 20 ticks. The ball carrier's brain gives a ball back to him a bonus when he is free
/// (<see cref="TickBallCarrierBrain.WallPassBonus"/>).
/// </description></item>
/// <item><description>
/// <b>The third man</b> (<see cref="TryThirdMan"/>). A pass forward into a forward who has his back to goal (a defender on the goal side
/// of him) and is pressed sends the nearest midfielder, who is neither of them, past him to a point 10 m beyond and 4 m away from his
/// marker, within 25 ticks. The forward's brain favours the lay-off to him (<see cref="TickBallCarrierBrain.LayOffBonus"/>).
/// </description></item>
/// </list>
/// <para>
/// Both work in the attacking side's own point of view (it attacks towards high X and counts Y from its left hand) and mirror back,
/// and both are integer arithmetic over spans: a pass allocates nothing.
/// </para>
/// </remarks>
internal static class TickCombinations
{
    /// <summary>The raw pressure, in basis points, a receiver is under for a pass to him to be a pass to a man in trouble (a defender about 5.5 m off).</summary>
    public const int PressedReceiver = 3_000;

    /// <summary>The longest pass that starts a return run, in pitch units (15 m).</summary>
    public const int ReturnMaximumPass = 1_500;

    /// <summary>How far beyond the receiver's marker the passer runs, in pitch units (7 m).</summary>
    public const int ReturnAhead = 700;

    /// <summary>How far to the passer's side of the marker the return run goes, in pitch units (4 m).</summary>
    public const int ReturnSide = 400;

    /// <summary>The least the return run gains on the passer, in pitch units (3 m): a run that goes nowhere is not made.</summary>
    public const int ReturnMinimumGain = 300;

    /// <summary>The farthest a return run goes, in pitch units (20 m).</summary>
    public const int ReturnMaximumRun = 2_000;

    /// <summary>How long a return run lasts, in ticks (2 s).</summary>
    public const int ReturnTicks = 20;

    /// <summary>The least a pass into a forward gains, in pitch units, to be a pass into his feet from behind (5 m).</summary>
    public const int ThirdManMinimumGain = 500;

    /// <summary>How near a defender stands to the forward, goal-side of him, to have him with his back to goal, in pitch units (6 m).</summary>
    public const int ThirdManMarkerReach = 600;

    /// <summary>The farthest from the forward the midfielder who runs past him starts, in pitch units (30 m).</summary>
    public const int ThirdManRange = 3_000;

    /// <summary>How far beyond the forward the third man runs, in pitch units (10 m).</summary>
    public const int ThirdManAhead = 1_000;

    /// <summary>How far to the side of the forward, away from his marker, the third man runs, in pitch units (4 m).</summary>
    public const int ThirdManSide = 400;

    /// <summary>How long the third man's run lasts, in ticks (2.5 s).</summary>
    public const int ThirdManTicks = 25;

    /// <summary>
    /// Decides whether the man who has just played a short pass runs for the ball back, and where to.
    /// </summary>
    /// <param name="isHome">Whether the side is the home side.</param>
    /// <param name="attackers">The side's players, with the goalkeeper first.</param>
    /// <param name="specs">The side's board positions, in the same order.</param>
    /// <param name="defenders">The opposing players, with the goalkeeper first.</param>
    /// <param name="passer">The passer's seat.</param>
    /// <param name="receiver">The receiver's seat.</param>
    /// <param name="target">Receives the point to run to, in absolute pitch units.</param>
    /// <returns>True when the passer runs.</returns>
    public static bool TryReturnRun(
        bool isHome,
        ReadOnlySpan<TickPlayerState> attackers,
        ReadOnlySpan<TickAnchorSpec> specs,
        ReadOnlySpan<TickPlayerState> defenders,
        int passer,
        int receiver,
        out SpatialPoint target)
    {
        target = default;

        if (specs[passer].Family is MatchPositionFamily.Goalkeeper or MatchPositionFamily.Defence
            || specs[receiver].Family == MatchPositionFamily.Goalkeeper
            || TickBallCarrierBrain.MeasurePressure(attackers[receiver], defenders) < PressedReceiver)
        {
            return false;
        }

        var px = BodyX(attackers[passer], isHome);
        var py = BodyY(attackers[passer], isHome);
        var rx = BodyX(attackers[receiver], isHome);
        var ry = BodyY(attackers[receiver], isHome);

        if (Distance(px, py, rx, ry) > ReturnMaximumPass)
        {
            return false;
        }

        var marker = NearestDefender(isHome, defenders, rx, ry, out var mx, out var my);

        if (marker < 0)
        {
            return false;
        }

        var side = py >= my ? 1 : -1;
        var tx = Math.Clamp(Math.Max(mx, rx) + ReturnAhead, TickTacticalGeometry.Margin, SpatialPitch.PitchLength - TickTacticalGeometry.Margin);
        var ty = Math.Clamp(my + (side * ReturnSide), TickTacticalGeometry.Margin, SpatialPitch.PitchWidth - TickTacticalGeometry.Margin);

        if (tx - px < ReturnMinimumGain || Distance(px, py, tx, ty) > ReturnMaximumRun)
        {
            return false;
        }

        target = new SpatialPoint(Flip(tx, SpatialPitch.PitchLength, isHome), Flip(ty, SpatialPitch.PitchWidth, isHome));

        return true;
    }

    /// <summary>
    /// Decides whether a pass into a forward with his back to goal sends a midfielder past him, and who.
    /// </summary>
    /// <param name="isHome">Whether the side is the home side.</param>
    /// <param name="attackers">The side's players, with the goalkeeper first.</param>
    /// <param name="specs">The side's board positions, in the same order.</param>
    /// <param name="defenders">The opposing players, with the goalkeeper first.</param>
    /// <param name="passer">The passer's seat.</param>
    /// <param name="receiver">The receiver's seat.</param>
    /// <param name="seat">Receives the third man's seat.</param>
    /// <param name="target">Receives the point to run to, in absolute pitch units.</param>
    /// <returns>True when a midfielder runs.</returns>
    public static bool TryThirdMan(
        bool isHome,
        ReadOnlySpan<TickPlayerState> attackers,
        ReadOnlySpan<TickAnchorSpec> specs,
        ReadOnlySpan<TickPlayerState> defenders,
        int passer,
        int receiver,
        out int seat,
        out SpatialPoint target)
    {
        seat = -1;
        target = default;

        if (specs[receiver].Family != MatchPositionFamily.Attack
            || specs[passer].Family == MatchPositionFamily.Goalkeeper
            || TickBallCarrierBrain.MeasurePressure(attackers[receiver], defenders) < PressedReceiver)
        {
            return false;
        }

        var px = BodyX(attackers[passer], isHome);
        var rx = BodyX(attackers[receiver], isHome);
        var ry = BodyY(attackers[receiver], isHome);

        if (rx - px < ThirdManMinimumGain)
        {
            return false;
        }

        var marker = NearestDefender(isHome, defenders, rx, ry, out var mx, out var my);

        // His back to goal: the man on him is goal-side of him (or level) and close.
        if (marker < 0 || mx < rx - 100 || Distance(rx, ry, mx, my) > ThirdManMarkerReach)
        {
            return false;
        }

        var nearest = long.MaxValue;
        var range = (long)ThirdManRange * ThirdManRange;

        for (var index = 1; index < attackers.Length; index++)
        {
            if (index == passer || index == receiver || specs[index].Family != MatchPositionFamily.Midfield)
            {
                continue;
            }

            var x = BodyX(attackers[index], isHome);
            var y = BodyY(attackers[index], isHome);
            long dx = x - rx;
            long dy = y - ry;
            var squared = (dx * dx) + (dy * dy);

            // He comes from deeper than the forward, or level with him.
            if (x <= rx + 300 && squared <= range && squared < nearest)
            {
                nearest = squared;
                seat = index;
            }
        }

        if (seat < 0)
        {
            return false;
        }

        var side = ry != my ? Math.Sign(ry - my) : (ry < SpatialPitch.GoalYCenter ? 1 : -1);
        var tx = Math.Clamp(rx + ThirdManAhead, TickTacticalGeometry.Margin, SpatialPitch.PitchLength - TickTacticalGeometry.Margin);
        var ty = Math.Clamp(ry + (side * ThirdManSide), TickTacticalGeometry.Margin, SpatialPitch.PitchWidth - TickTacticalGeometry.Margin);

        target = new SpatialPoint(Flip(tx, SpatialPitch.PitchLength, isHome), Flip(ty, SpatialPitch.PitchWidth, isHome));

        return true;
    }

    /// <summary>Finds the opponent nearest a point (the goalkeeper is not one), and gives where he stands in the side's own point of view.</summary>
    private static int NearestDefender(bool isHome, ReadOnlySpan<TickPlayerState> defenders, int x, int y, out int ownX, out int ownY)
    {
        var nearest = -1;
        var best = long.MaxValue;

        ownX = 0;
        ownY = 0;

        for (var index = 1; index < defenders.Length; index++)
        {
            var dx = BodyX(defenders[index], isHome);
            var dy = BodyY(defenders[index], isHome);
            long ax = dx - x;
            long ay = dy - y;
            var squared = (ax * ax) + (ay * ay);

            if (squared < best)
            {
                best = squared;
                nearest = index;
                ownX = dx;
                ownY = dy;
            }
        }

        return nearest;
    }

    private static int Distance(int fromX, int fromY, int toX, int toY)
    {
        long dx = toX - fromX;
        long dy = toY - fromY;

        return (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Gets a body's X in pitch units, in the side's own point of view.</summary>
    private static int BodyX(in TickPlayerState body, bool isHome) => Flip(TickSpatialUnits.ToUnits(body.X), SpatialPitch.PitchLength, isHome);

    /// <summary>Gets a body's Y in pitch units, in the side's own point of view.</summary>
    private static int BodyY(in TickPlayerState body, bool isHome) => Flip(TickSpatialUnits.ToUnits(body.Y), SpatialPitch.PitchWidth, isHome);

    /// <summary>Converts a coordinate between the pitch's and the side's own point of view (the conversion is its own inverse).</summary>
    private static int Flip(int units, int extent, bool isHome) => isHome ? units : extent - units;
}
