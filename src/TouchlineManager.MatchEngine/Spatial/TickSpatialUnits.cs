namespace TouchlineManager.MatchEngine.Spatial;

/// <summary>
/// Scale constants and unit conversions for the 10 Hz tick engine (`tick-engine-v1`).
/// </summary>
/// <remarks>
/// <para>
/// The pitch keeps its published coordinates (<see cref="SpatialPitch"/>: X 0..10,000, Y 0..7,000, Z 0..100), where
/// 100 pitch units are about 1.05 m. The tick simulation, however, moves things by fractions of a unit every 100 ms,
/// so its working state is held in <em>fixed point</em>: one pitch unit is <see cref="FixedScale"/> fixed units. All
/// of it is integer arithmetic (ADR-0013), so a match replays to the same byte on every CPU architecture.
/// </para>
/// <para>
/// Velocities are fixed units per tick and accelerations fixed units per tick squared. Height (Z) is on its own
/// scale, about 7 cm per Z unit, which puts the 2.44 m crossbar at Z 35 and the top of the range at about 7 m.
/// </para>
/// <para>
/// The X and Y scales are treated as equal (1.05 cm per unit) although a 68 m wide pitch squeezed into 7,000 units is
/// really 0.97 cm per unit on Y. The 8% skew is below what a viewer can see and keeps every distance a single
/// Pythagoras sum.
/// </para>
/// </remarks>
public static class TickSpatialUnits
{
    /// <summary>Ticks in one match second.</summary>
    public const int TicksPerSecond = 10;

    /// <summary>Milliseconds in one tick.</summary>
    public const int TickDeltaMs = 100;

    /// <summary>Fixed units in one pitch unit.</summary>
    public const int FixedScale = 1_000;

    /// <summary>The pitch length, in fixed units.</summary>
    public const int PitchLengthFixed = SpatialPitch.PitchLength * FixedScale;

    /// <summary>The pitch width, in fixed units.</summary>
    public const int PitchWidthFixed = SpatialPitch.PitchWidth * FixedScale;

    /// <summary>The highest ball height, in Z units.</summary>
    public const int MaxZUnits = 100;

    /// <summary>The highest ball height, in fixed units.</summary>
    public const int MaxZFixed = MaxZUnits * FixedScale;

    /// <summary>The goal's near post along the goal line, in pitch units: a goal is 7.32 m wide, as the viewer draws it, centred on the line (the possession engine's wider 10 m mouth is its own).</summary>
    public const int GoalMouthMinUnits = 3_123;

    /// <summary>The goal's far post along the goal line, in pitch units.</summary>
    public const int GoalMouthMaxUnits = 3_877;

    /// <summary>The crossbar's height, in Z units (2.44 m).</summary>
    public const int CrossbarZUnits = 35;

    /// <summary>Fixed pitch units in one metre (95.238 units, since 105 m is 10,000 units).</summary>
    private const long FixedPerMetre = 95_238;

    /// <summary>Fixed Z units in one metre of height (14.286 Z units, since one Z unit is 7 cm).</summary>
    private const long ZFixedPerMetre = 14_286;

    /// <summary>
    /// Gravity as a fixed Z-unit change per tick squared: 9.81 m/s² over a 0.1 s tick is 0.0981 m per tick², which is
    /// 1,401 Z-fixed units, taken as the even 1,402 so that a launch's half-flight is a whole number.
    /// </summary>
    public const int GravityFixed = 1_402;

    /// <summary>Converts pitch units to fixed units.</summary>
    /// <param name="units">The pitch units.</param>
    public static int ToFixed(int units) => units * FixedScale;

    /// <summary>Converts fixed units to whole pitch units, rounding towards zero.</summary>
    /// <param name="fixedValue">The fixed units.</param>
    public static int ToUnits(int fixedValue) => fixedValue / FixedScale;

    /// <summary>Converts centimetres on the ground to fixed units.</summary>
    /// <param name="centimetres">The distance in centimetres.</param>
    public static int CentimetresToFixed(int centimetres) => (int)(centimetres * FixedPerMetre / 100);

    /// <summary>Converts a ground speed in centimetres per second to fixed units per tick.</summary>
    /// <param name="centimetresPerSecond">The speed.</param>
    public static int SpeedToFixedPerTick(int centimetresPerSecond) =>
        (int)(centimetresPerSecond * FixedPerMetre / 1_000);

    /// <summary>Converts a ground acceleration in centimetres per second squared to fixed units per tick squared.</summary>
    /// <param name="centimetresPerSecondSquared">The acceleration.</param>
    public static int AccelerationToFixedPerTickSquared(int centimetresPerSecondSquared) =>
        (int)(centimetresPerSecondSquared * FixedPerMetre / 10_000);

    /// <summary>Converts a height in centimetres to Z fixed units.</summary>
    /// <param name="centimetres">The height in centimetres.</param>
    public static int HeightToZFixed(int centimetres) => (int)(centimetres * ZFixedPerMetre / 100);
}
