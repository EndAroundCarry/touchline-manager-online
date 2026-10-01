namespace TouchlineManager.MatchEngine.Spatial;

/// <summary>
/// A 3D spatial position for the match ball.
/// </summary>
/// <param name="X">X coordinate along pitch length (0..10,000).</param>
/// <param name="Y">Y coordinate along pitch width (0..7,000).</param>
/// <param name="Z">Altitude / height above pitch surface (0..100, where 0 is on ground, 100 is high aerial ball).</param>
public readonly record struct BallState(int X, int Y, int Z = 0)
{
    public SpatialPoint GroundPoint => new(X, Y);
    public bool IsAerial => Z > 15;

    public static BallState At(SpatialPoint point, int z = 0) => new(point.X, point.Y, z);
    public static BallState Center => new(5000, 3500, 0);
}

/// <summary>
/// Aerodynamic and trajectory calculations for passes, crosses, shots, and rebounds.
/// </summary>
public static class BallPhysics
{
    /// <summary>Generates keyframe positions along a ground or lofted trajectory.</summary>
    public static IReadOnlyList<(int TimeMs, BallState Position)> Trajectory(
        BallState start,
        BallState target,
        int durationMs,
        int peakAltitude = 0,
        int steps = 6)
    {
        var list = new List<(int, BallState)>(steps + 1);

        for (var i = 0; i <= steps; i++)
        {
            var fraction = (double)i / steps;
            var timeMs = (int)(fraction * durationMs);

            var x = (int)(start.X + ((target.X - start.X) * fraction));
            var y = (int)(start.Y + ((target.Y - start.Y) * fraction));

            // Parabolic curve for altitude
            var arc = 4.0 * fraction * (1.0 - fraction); // 0 at ends, 1.0 at midpoint
            var z = (int)(start.Z + ((target.Z - start.Z) * fraction) + (peakAltitude * arc));

            list.Add((timeMs, new BallState(x, y, Math.Clamp(z, 0, 100))));
        }

        return list;
    }
}
