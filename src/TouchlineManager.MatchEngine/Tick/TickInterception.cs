using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>Where the ball will be one tick on and how near a player must come to touch it there.</summary>
/// <param name="X">The ball's X, in fixed units.</param>
/// <param name="Y">The ball's Y, in fixed units.</param>
/// <param name="ReachFixed">The distance within which a player takes the ball at that point, in fixed units, or 0 while it is too high to be reached.</param>
internal readonly record struct TickBallPathPoint(int X, int Y, int ReachFixed);

/// <summary>
/// Works out where a man can meet a ball that is on its way (`tick-film-v1`, Milestone 4): the first point of its path he can get to by the
/// time it gets there.
/// </summary>
/// <remarks>
/// <para>
/// A pass used to send its receiver to the spot the ball <em>stops</em> at, which for a firm ground ball lies up to twenty-five metres
/// beyond the man it was played to, so he ran away with the ball and took it when it had crawled to a halt. Meeting the ball is the
/// other way round: the path is played forward on a scratch ball once (<see cref="Trace"/>, the same <see cref="TickBallPhysics.Step"/>
/// the real ball takes, so friction, flight and bounces are all in it), and for each man the first tick is found at which the ball is
/// within his reach and he can have covered the ground to it (<see cref="EarliestReach(ReadOnlySpan{TickBallPathPoint}, in TickPlayerState, in TickPlayerProfile, out SpatialPoint)"/>).
/// </para>
/// <para>
/// What he can cover is his speed now, along the line to the point, plus his acceleration up to his top speed (tired legs and a lockout
/// included), after a turn if he faces away. It is integer arithmetic over spans, so nothing allocates.
/// </para>
/// </remarks>
internal static class TickInterception
{
    /// <summary>How many ticks of the ball's path are looked at (6 s).</summary>
    public const int PathTicks = 60;

    /// <summary>Plays a ball forward on a scratch ball and writes where it is after each tick.</summary>
    /// <remarks>
    /// A ball that stops, or leaves the pitch, stays where it is for the rest of the path: a man can still go and wait for it there.
    /// </remarks>
    /// <param name="scratch">A copy of the ball (<see cref="TickBallPhysics.CopyFrom"/>), advanced by this call.</param>
    /// <param name="path">The path to fill: at least <see cref="PathTicks"/> long.</param>
    public static void Trace(TickBallPhysics scratch, Span<TickBallPathPoint> path)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        ArgumentOutOfRangeException.ThrowIfLessThan(path.Length, PathTicks);

        var filled = 0;
        var resting = false;

        while (filled < PathTicks && !resting)
        {
            var boundary = scratch.Step();

            path[filled++] = new TickBallPathPoint(scratch.X, scratch.Y, ReachOf(scratch));

            resting = boundary is not (TickBallBoundary.InPlay or TickBallBoundary.HitPost or TickBallBoundary.HitCrossbar)
                || (!scratch.IsAirborne && scratch.GroundSpeed == 0);
        }

        for (var tick = filled; tick < PathTicks; tick++)
        {
            path[tick] = path[filled - 1];
        }
    }

    /// <summary>Finds the first point of a ball's path a man can get to by the time the ball does.</summary>
    /// <param name="scratch">A copy of the ball, advanced by this call.</param>
    /// <param name="body">The man, as he stands now.</param>
    /// <param name="profile">His athletic limits.</param>
    /// <param name="point">The point, in pitch units; the point the ball ends at when he cannot reach it.</param>
    /// <returns>The ticks until he and the ball are there, or -1 when he cannot reach it within <see cref="PathTicks"/>.</returns>
    public static int EarliestReach(TickBallPhysics scratch, in TickPlayerState body, in TickPlayerProfile profile, out SpatialPoint point)
    {
        Span<TickBallPathPoint> path = stackalloc TickBallPathPoint[PathTicks];

        Trace(scratch, path);

        return EarliestReach(path, body, profile, out point);
    }

    /// <summary>Finds the first point of a traced path a man can get to by the time the ball does.</summary>
    /// <param name="path">The path from <see cref="Trace"/>.</param>
    /// <param name="body">The man, as he stands now.</param>
    /// <param name="profile">His athletic limits.</param>
    /// <param name="point">The point, in pitch units; the point the ball ends at when he cannot reach it.</param>
    /// <param name="aim">
    /// Where the ball was meant to go, in pitch units, for the man it was played to: he adjusts only to a ball that comes within
    /// <paramref name="aimRadius"/> of it, so a pass that goes wide of him is wide of him. Ignored when the radius is 0.
    /// </param>
    /// <param name="aimRadius">How far from <paramref name="aim"/> a point of the path may be and still be gone for, in pitch units; 0 for any point.</param>
    /// <returns>The ticks until he and the ball are there, or -1 when he cannot reach it within <see cref="PathTicks"/>.</returns>
    public static int EarliestReach(
        ReadOnlySpan<TickBallPathPoint> path,
        in TickPlayerState body,
        in TickPlayerProfile profile,
        out SpatialPoint point,
        SpatialPoint aim = default,
        int aimRadius = 0)
    {
        var aimX = TickSpatialUnits.ToFixed(aim.X);
        var aimY = TickSpatialUnits.ToFixed(aim.Y);
        var aimLimit = (long)TickSpatialUnits.ToFixed(aimRadius) * TickSpatialUnits.ToFixed(aimRadius);

        var top = (long)TickPlayerPhysics.EffectiveTopSpeed(body, profile);

        if (body.Lockout > 0)
        {
            top = top * TickPlayerPhysics.StumbleSpeedBasisPoints / 10_000;
        }

        for (var index = 0; index < path.Length; index++)
        {
            var spot = path[index];

            if (spot.ReachFixed == 0)
            {
                continue;
            }

            if (aimLimit > 0)
            {
                long ax = spot.X - aimX;
                long ay = spot.Y - aimY;

                if ((ax * ax) + (ay * ay) > aimLimit)
                {
                    continue;
                }
            }

            var ticks = index + 1;
            long dx = spot.X - body.X;
            long dy = spot.Y - body.Y;
            var squared = (dx * dx) + (dy * dy);
            var farthest = (top * ticks) + spot.ReachFixed;

            // Out of range even at top speed for the whole time: the cheap test that most men fail, with no root and no turn.
            if (squared > farthest * farthest)
            {
                continue;
            }

            var distance = SpatialMath.Sqrt(squared);
            var needed = distance - spot.ReachFixed;

            if (needed <= 0)
            {
                point = new SpatialPoint(TickSpatialUnits.ToUnits(spot.X), TickSpatialUnits.ToUnits(spot.Y));

                return ticks;
            }

            var wanted = TickTrigonometry.AngleOf(dx, dy);
            var gap = Math.Abs(TickTrigonometry.Difference(body.Heading, wanted));
            var turnRate = Math.Max(1, profile.TurnRateAtTopSpeed * TurnRateMidFactor / 100 / 100);
            var turnTicks = gap / turnRate;
            var alongLine = Math.Max(0L, (long)body.Speed * TickTrigonometry.Cos(wanted - body.Heading) / TickTrigonometry.Scale);

            if (ReachableDistance(ticks - turnTicks, alongLine, top, profile.Acceleration) >= needed)
            {
                point = new SpatialPoint(TickSpatialUnits.ToUnits(spot.X), TickSpatialUnits.ToUnits(spot.Y));

                return ticks;
            }
        }

        var last = path.Length > 0 ? path[^1] : default;

        point = new SpatialPoint(TickSpatialUnits.ToUnits(last.X), TickSpatialUnits.ToUnits(last.Y));

        return -1;
    }

    /// <summary>
    /// A man's turn per tick is estimated at this many hundredths of his flat-out rate: between the flat-out rate and the four times it he
    /// has standing still.
    /// </summary>
    private const int TurnRateMidFactor = 250;

    /// <summary>Gets how far a man covers in some ticks: his speed now, rising by his acceleration each tick up to his top speed.</summary>
    private static long ReachableDistance(int ticks, long speed, long top, long acceleration)
    {
        if (ticks <= 0)
        {
            return 0;
        }

        speed = Math.Min(speed, top);

        if (speed >= top || acceleration <= 0)
        {
            return ticks * speed;
        }

        var accelerating = Math.Min(ticks, (top - speed) / acceleration);
        var distance = (speed * accelerating) + (acceleration * accelerating * (accelerating + 1) / 2);

        return distance + ((ticks - accelerating) * top);
    }

    private static int ReachOf(TickBallPhysics ball)
    {
        if (ball.UnitZ > TickBallPhysics.HeadReachZUnits)
        {
            return 0;
        }

        return TickSpatialUnits.ToFixed(
            ball.UnitZ > TickBallPhysics.GroundContactZUnits
                ? TickBallPhysics.AerialReceptionRadiusUnits
                : TickBallPhysics.GroundReceptionRadiusUnits);
    }
}
