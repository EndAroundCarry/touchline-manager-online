namespace TouchlineManager.MatchEngine.Spatial;

/// <summary>
/// Integer geometry on the normalized pitch (`engine-v10`).
/// </summary>
/// <remarks>
/// The simulation has no floating point (ADR-0013), so <see cref="SpatialPoint.DistanceTo"/>, which returns a
/// double, is never read by an outcome. Distances here are whole units, rounded down, from an integer square root
/// that gives the same answer on every machine.
/// </remarks>
public static class SpatialMath
{
    /// <summary>Gets the square of the distance between two points.</summary>
    /// <param name="from">One point.</param>
    /// <param name="to">The other.</param>
    public static long DistanceSquared(SpatialPoint from, SpatialPoint to)
    {
        long dx = from.X - to.X;
        long dy = from.Y - to.Y;

        return (dx * dx) + (dy * dy);
    }

    /// <summary>Gets the distance between two points, in whole pitch units, rounded down.</summary>
    /// <param name="from">One point.</param>
    /// <param name="to">The other.</param>
    public static int Distance(SpatialPoint from, SpatialPoint to) =>
        (int)Sqrt(DistanceSquared(from, to));

    /// <summary>Gets the integer square root of a value: the largest whole number whose square is not above it.</summary>
    /// <param name="value">The value, not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">When the value is negative.</exception>
    public static long Sqrt(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        if (value < 2)
        {
            return value;
        }

        // Newton's method from above converges on the floor of the root in a handful of steps.
        var root = value;
        var next = (root + 1) / 2;

        while (next < root)
        {
            root = next;
            next = (root + (value / root)) / 2;
        }

        return root;
    }
}
