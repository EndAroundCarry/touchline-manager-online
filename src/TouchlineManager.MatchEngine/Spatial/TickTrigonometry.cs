namespace TouchlineManager.MatchEngine.Spatial;

/// <summary>
/// Integer trigonometry on binary angles for the tick engine.
/// </summary>
/// <remarks>
/// <para>
/// A heading is a whole number of 1/1,024ths of a turn (about 0.35°): 0 faces the away goal (+X), 256 faces +Y, 512
/// faces the home goal, 768 faces -Y. Sine and cosine come from one literal quarter-wave table, scaled to
/// <see cref="Scale"/> (basis points), so no floating point runs in the simulation (ADR-0013) and every machine reads
/// the same values. Math.Sin is deliberately not used: its last digit is the runtime's, not ours.
/// </para>
/// <para>
/// The inverse (<see cref="AngleOf"/>) is a binary search over that same table rather than a second table, which
/// keeps the two exactly consistent.
/// </para>
/// </remarks>
public static class TickTrigonometry
{
    /// <summary>Binary angle units in a full turn.</summary>
    public const int FullTurn = 1_024;

    /// <summary>Binary angle units in a half turn.</summary>
    public const int HalfTurn = FullTurn / 2;

    /// <summary>Binary angle units in a quarter turn.</summary>
    public const int QuarterTurn = FullTurn / 4;

    /// <summary>The scale of <see cref="Sin"/> and <see cref="Cos"/> results: 10,000 is 1.0.</summary>
    public const int Scale = 10_000;

    /// <summary>
    /// Sine of 0..256 binary angle units (0..90°), scaled by <see cref="Scale"/>. An array built once, because a
    /// span-returning collection expression allocated a fresh copy on every read.
    /// </summary>
    private static readonly short[] QuarterSine =
    [
        0, 61, 123, 184, 245, 307, 368, 429, 491, 552, 613, 674, 736, 797, 858, 919,
        980, 1041, 1102, 1163, 1224, 1285, 1346, 1407, 1467, 1528, 1589, 1649, 1710, 1770, 1830, 1891,
        1951, 2011, 2071, 2131, 2191, 2251, 2311, 2370, 2430, 2489, 2549, 2608, 2667, 2726, 2785, 2844,
        2903, 2962, 3020, 3078, 3137, 3195, 3253, 3311, 3369, 3427, 3484, 3542, 3599, 3656, 3713, 3770,
        3827, 3883, 3940, 3996, 4052, 4108, 4164, 4220, 4276, 4331, 4386, 4441, 4496, 4551, 4605, 4660,
        4714, 4768, 4822, 4876, 4929, 4982, 5035, 5088, 5141, 5194, 5246, 5298, 5350, 5402, 5453, 5505,
        5556, 5607, 5657, 5708, 5758, 5808, 5858, 5908, 5957, 6006, 6055, 6104, 6152, 6201, 6249, 6296,
        6344, 6391, 6438, 6485, 6532, 6578, 6624, 6670, 6716, 6761, 6806, 6851, 6895, 6940, 6984, 7028,
        7071, 7114, 7157, 7200, 7242, 7285, 7327, 7368, 7410, 7451, 7491, 7532, 7572, 7612, 7652, 7691,
        7730, 7769, 7807, 7846, 7883, 7921, 7958, 7995, 8032, 8068, 8105, 8140, 8176, 8211, 8246, 8280,
        8315, 8349, 8382, 8416, 8449, 8481, 8514, 8546, 8577, 8609, 8640, 8670, 8701, 8731, 8761, 8790,
        8819, 8848, 8876, 8904, 8932, 8960, 8987, 9013, 9040, 9066, 9092, 9117, 9142, 9167, 9191, 9215,
        9239, 9262, 9285, 9308, 9330, 9352, 9373, 9395, 9415, 9436, 9456, 9476, 9495, 9514, 9533, 9551,
        9569, 9587, 9604, 9621, 9638, 9654, 9670, 9685, 9700, 9715, 9729, 9743, 9757, 9770, 9783, 9796,
        9808, 9820, 9831, 9842, 9853, 9863, 9873, 9883, 9892, 9901, 9909, 9917, 9925, 9932, 9939, 9946,
        9952, 9958, 9963, 9968, 9973, 9977, 9981, 9985, 9988, 9991, 9993, 9995, 9997, 9998, 9999, 10000,
        10000,
    ];

    /// <summary>Wraps any angle into 0..<see cref="FullTurn"/> - 1.</summary>
    /// <param name="angle">The angle.</param>
    public static int Normalize(int angle) => angle & (FullTurn - 1);

    /// <summary>Gets the sine of an angle, scaled by <see cref="Scale"/>.</summary>
    /// <param name="angle">The angle in binary angle units.</param>
    public static int Sin(int angle)
    {
        var a = Normalize(angle);
        var remainder = a & (QuarterTurn - 1);
        var quadrant = a / QuarterTurn;

        // Quadrants 0 and 2 climb the table, 1 and 3 walk it back; the second half of the turn is negative.
        var magnitude = (quadrant & 1) == 0
            ? QuarterSine[remainder]
            : QuarterSine[QuarterTurn - remainder];

        return quadrant >= 2 ? -magnitude : magnitude;
    }

    /// <summary>Gets the cosine of an angle, scaled by <see cref="Scale"/>.</summary>
    /// <param name="angle">The angle in binary angle units.</param>
    public static int Cos(int angle) => Sin(angle + QuarterTurn);

    /// <summary>
    /// Gets the signed shortest turn from one angle to another: positive turns towards +Y, and the result is in
    /// -512 &lt; turn &lt;= 512.
    /// </summary>
    /// <param name="from">The starting angle.</param>
    /// <param name="to">The target angle.</param>
    public static int Difference(int from, int to)
    {
        var turn = Normalize(to - from);

        return turn > HalfTurn ? turn - FullTurn : turn;
    }

    /// <summary>Gets the heading of a vector, to the nearest binary angle unit. The zero vector faces 0.</summary>
    /// <param name="dx">The X component.</param>
    /// <param name="dy">The Y component.</param>
    public static int AngleOf(long dx, long dy)
    {
        if (dx == 0 && dy == 0)
        {
            return 0;
        }

        var ax = Math.Abs(dx);
        var ay = Math.Abs(dy);

        // Reduce the components so the products below cannot overflow, keeping their ratio.
        while (ax > 1_000_000_000L || ay > 1_000_000_000L)
        {
            ax >>= 1;
            ay >>= 1;
        }

        // Smallest first-quadrant angle whose tangent reaches ay/ax: sin(a) * ax >= cos(a) * ay. The quarter
        // table is read directly — cos(mid) is the table's mirror image — because this search runs once per
        // player per tick in the physics (`tick-engine-v1`, Milestone 9).
        var low = 0;
        var high = QuarterTurn;

        while (low < high)
        {
            var mid = (low + high) / 2;

            if (((long)QuarterSine[mid] * ax) >= ((long)QuarterSine[QuarterTurn - mid] * ay))
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        // The search lands on the first angle at or above the vector; the one below may be nearer.
        if (low > 0)
        {
            var above = Math.Abs(((long)QuarterSine[low] * ax) - ((long)QuarterSine[QuarterTurn - low] * ay));
            var below = Math.Abs(((long)QuarterSine[low - 1] * ax) - ((long)QuarterSine[QuarterTurn - low + 1] * ay));

            if (below < above)
            {
                low--;
            }
        }

        return dx >= 0
            ? (dy >= 0 ? low : Normalize(-low))
            : (dy >= 0 ? HalfTurn - low : HalfTurn + low);
    }
}
