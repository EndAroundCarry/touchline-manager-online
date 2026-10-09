using System.Numerics;

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

    /// <summary>
    /// Over-estimates of the square root of one more than the top eight bits of a value, so Newton's method
    /// starts within a fifth of a per cent instead of within a half.
    /// </summary>
    /// <remarks>
    /// The value's top eight bits name the entry; the two tables hold it for an even and an odd remaining shift,
    /// both rounded up so the seed is always above the true root, which is what Newton's method needs to
    /// descend to the floor. Built with an integer bootstrap rather than a floating root, because nothing in
    /// the simulation is allowed to read a float (`tick-engine-v1`, Milestone 9).
    /// </remarks>
    private static readonly byte[] SeedEven = BuildSeedTable(multiplier: 1);

    private static readonly byte[] SeedOdd = BuildSeedTable(multiplier: 2);

    /// <summary>Gets the integer square root of a value: the largest whole number whose square is not above it.</summary>
    /// <remarks>
    /// Newton's method from above converges on the floor of the root. Seeding it from <see cref="SeedEven"/> or
    /// <see cref="SeedOdd"/> — an over-estimate of the root of one more than the value's top eight bits, shifted
    /// by the rest — starts it within a fifth of a per cent, so two divisions fewer than a seed a whole bit length
    /// out, and the tick loop takes this root per player per tick (`tick-engine-v1`, Milestone 9). The floor it
    /// returns is the same number whatever the seed.
    /// </remarks>
    /// <param name="value">The value, not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">When the value is negative.</exception>
    public static long Sqrt(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        if (value < 2)
        {
            return value;
        }

        var bits = 64 - BitOperations.LeadingZeroCount((ulong)value);
        long root;

        if (bits >= 8)
        {
            var top = (int)((ulong)value >> (bits - 8));
            var shift = (bits - 8) >> 1;

            root = ((bits - 8) & 1) == 0
                ? (long)SeedEven[top - 128] << shift
                : (long)SeedOdd[top - 128] << shift;
        }
        else
        {
            root = 1L << ((bits + 1) / 2);
        }

        while (true)
        {
            var next = (root + (value / root)) / 2;

            if (next >= root)
            {
                return root;
            }

            root = next;
        }
    }

    /// <summary>Builds one seed table: the rounded-up root of one more than the top eight bits, doubled where the shift is odd.</summary>
    /// <param name="multiplier">1 for an even remaining shift, 2 for an odd one.</param>
    private static byte[] BuildSeedTable(int multiplier)
    {
        var table = new byte[128];

        for (var index = 0; index < table.Length; index++)
        {
            // The value is below (top + 1) shifted, so its root is below the root of top + 1, doubled when the
            // shift that remains is odd. Rounding that root up keeps the seed an over-estimate, which is what
            // the iteration needs to descend onto the floor rather than stop below it.
            table[index] = (byte)(FloorRoot(multiplier * (index + 129)) + 1);
        }

        return table;
    }

    /// <summary>Gets the floor of a small square root by trial, for building the seed tables.</summary>
    /// <param name="value">A small value, at most 512.</param>
    private static int FloorRoot(int value)
    {
        var root = 0;

        while ((root + 1) * (root + 1) <= value)
        {
            root++;
        }

        return root;
    }
}
