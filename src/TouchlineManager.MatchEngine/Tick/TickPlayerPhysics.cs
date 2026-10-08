using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// One player's moving body in the tick engine: where he is, which way he faces, how fast he runs, how tired he is.
/// </summary>
/// <remarks>
/// Positions are fixed units, speed is fixed units per tick along the heading, the heading is in binary angle units
/// (<see cref="TickTrigonometry"/>), and energy runs from 0 to <see cref="TickPlayerPhysics.EnergyFull"/>. The struct
/// is mutated in place by <see cref="TickPlayerPhysics.Step"/>, so a match holds 22 of them in one array and the tick
/// loop allocates nothing.
/// </remarks>
internal struct TickPlayerState
{
    /// <summary>The X position, in fixed units.</summary>
    public int X;

    /// <summary>The Y position, in fixed units.</summary>
    public int Y;

    /// <summary>The direction he faces and runs, in binary angle units.</summary>
    public int Heading;

    /// <summary>His speed along the heading, in fixed units per tick. Never negative.</summary>
    public int Speed;

    /// <summary>His energy: <see cref="TickPlayerPhysics.EnergyFull"/> is fully fresh, 0 is spent.</summary>
    public int Energy;

    /// <summary>Gets the X velocity, in fixed units per tick.</summary>
    public readonly int VelocityX => (int)((long)TickTrigonometry.Cos(Heading) * Speed / TickTrigonometry.Scale);

    /// <summary>Gets the Y velocity, in fixed units per tick.</summary>
    public readonly int VelocityY => (int)((long)TickTrigonometry.Sin(Heading) * Speed / TickTrigonometry.Scale);

    /// <summary>Creates a player standing at a point, fresh to a given condition.</summary>
    /// <param name="xUnits">The X position, in pitch units.</param>
    /// <param name="yUnits">The Y position, in pitch units.</param>
    /// <param name="heading">The direction he faces, in binary angle units.</param>
    /// <param name="conditionBasisPoints">His condition, 0..10,000.</param>
    public static TickPlayerState Standing(int xUnits, int yUnits, int heading, int conditionBasisPoints) =>
        new()
        {
            X = TickSpatialUnits.ToFixed(xUnits),
            Y = TickSpatialUnits.ToFixed(yUnits),
            Heading = TickTrigonometry.Normalize(heading),
            Speed = 0,
            Energy = Math.Clamp(conditionBasisPoints, 0, 10_000) * (TickPlayerPhysics.EnergyFull / 10_000),
        };
}

/// <summary>
/// Where a player wants to go this tick and how hard he wants to go there.
/// </summary>
/// <param name="TargetXUnits">The point to run to, X, in pitch units.</param>
/// <param name="TargetYUnits">The point to run to, Y, in pitch units.</param>
/// <param name="SpeedLimitBasisPoints">The share of his top speed he may use, 0..10,000 (7,000 is a jog).</param>
/// <param name="Arrive">
/// True to stop on the point (the player brakes in time and settles on it); false to run through it at the limit.
/// </param>
internal readonly record struct TickMoveIntent(int TargetXUnits, int TargetYUnits, int SpeedLimitBasisPoints, bool Arrive);

/// <summary>
/// One player's athletic limits, worked out once from his attributes.
/// </summary>
/// <remarks>
/// All of them are integers in tick units, so the profile is built once per player per match and the tick loop only
/// reads it. See <see cref="TickPlayerPhysics"/> for the formulas.
/// </remarks>
internal readonly record struct TickPlayerProfile
{
    /// <summary>Gets his top speed when fully fresh, in fixed units per tick.</summary>
    public required int TopSpeed { get; init; }

    /// <summary>Gets how much speed he gains per tick, in fixed units per tick squared.</summary>
    public required int Acceleration { get; init; }

    /// <summary>Gets how much speed he can shed per tick when braking, in fixed units per tick squared.</summary>
    public required int Deceleration { get; init; }

    /// <summary>Gets the most he can turn per tick when running flat out, in hundredths of a binary angle unit.</summary>
    public required int TurnRateAtTopSpeed { get; init; }

    /// <summary>Gets the multiplier on his energy drain, in basis points (10,000 is the average player).</summary>
    public required int DrainBasisPoints { get; init; }

    /// <summary>Builds the profile for a player's attributes.</summary>
    /// <param name="attributes">The player's frozen attributes.</param>
    public static TickPlayerProfile From(PlayerAttributesV1 attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var pace = attributes.ValueOf(MatchAttributeName.Pace);
        var acceleration = attributes.ValueOf(MatchAttributeName.Acceleration);
        var agility = attributes.ValueOf(MatchAttributeName.Agility);
        var stamina = attributes.ValueOf(MatchAttributeName.Stamina);
        var workRate = attributes.ValueOf(MatchAttributeName.WorkRate);

        return new TickPlayerProfile
        {
            TopSpeed = TickSpatialUnits.SpeedToFixedPerTick(TickPlayerPhysics.BaseSpeedCentimetresPerSecond
                + (pace * TickPlayerPhysics.PaceWeightCentimetresPerSecond)),
            Acceleration = TickSpatialUnits.AccelerationToFixedPerTickSquared(
                TickPlayerPhysics.BaseAccelerationCentimetresPerSecondSquared
                + (acceleration * TickPlayerPhysics.AccelerationWeight)),
            Deceleration = TickSpatialUnits.AccelerationToFixedPerTickSquared(
                TickPlayerPhysics.BaseDecelerationCentimetresPerSecondSquared
                + (agility * TickPlayerPhysics.DecelerationWeight)),
            TurnRateAtTopSpeed = (TickPlayerPhysics.BaseTurnDegrees + agility) * 100 * TickTrigonometry.FullTurn / 360,
            DrainBasisPoints = TickPlayerPhysics.DrainBaseBasisPoints
                - (stamina * TickPlayerPhysics.DrainStaminaWeight)
                + (workRate * TickPlayerPhysics.DrainWorkRateWeight),
        };
    }
}

/// <summary>
/// Player kinematics at 10 Hz: acceleration, braking, turning and stamina (`tick-engine-v1`, Milestone 1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Top speed</b> is <c>560 + 21 × Pace</c> cm/s (5.8 m/s at Pace 1 to 9.8 m/s at Pace 20), scaled by condition:
/// <c>70% + 30% × energy</c>, so a spent player is 30% slower, never stopped.
/// </para>
/// <para>
/// <b>Acceleration</b> is <c>300 + 10 × Acceleration</c> cm/s² (about 2 seconds to top speed); <b>braking</b> is
/// <c>600 + 15 × Agility</c> cm/s². A player who must stop on a point (<see cref="TickMoveIntent.Arrive"/>) never
/// goes faster than <c>√(2 · braking · distance)</c>, so he settles on it rather than overshooting.
/// </para>
/// <para>
/// <b>Turning</b> is limited per tick. Flat out, he can swing <c>(8 + Agility)°</c> a tick; the slower he runs the
/// more he can turn (up to four times as much standing still), and below 1 m/s he simply faces his target. While his
/// heading is still off the line he wants, his speed is cut by the cosine of the gap (never below 25%), so a sharp
/// cut costs pace instead of teleporting.
/// </para>
/// <para>
/// <b>Energy</b> falls at a rate set by speed: a walk (under 30% of top speed) recovers a little, a jog (30–70%) drains
/// at 6 millionths of the tank a tick, and above 70% the drain climbs linearly to ten times that at top
/// speed. The rate is multiplied by <c>14,500 − 450 × Stamina + 40 × WorkRate</c> basis points, so the average player
/// (Stamina 10, WorkRate 10) loses about a quarter of the tank over 90 minutes of a typical mix of running.
/// </para>
/// </remarks>
internal static class TickPlayerPhysics
{
    /// <summary>A full tank of energy: 100% is 100,000,000.</summary>
    public const int EnergyFull = 100_000_000;

    /// <summary>Top speed at Pace 0, in cm/s.</summary>
    public const int BaseSpeedCentimetresPerSecond = 560;

    /// <summary>Top speed gained per point of Pace, in cm/s.</summary>
    public const int PaceWeightCentimetresPerSecond = 21;

    /// <summary>Acceleration at Acceleration 0, in cm/s².</summary>
    public const int BaseAccelerationCentimetresPerSecondSquared = 300;

    /// <summary>Acceleration gained per point of the Acceleration attribute, in cm/s².</summary>
    public const int AccelerationWeight = 10;

    /// <summary>Braking at Agility 0, in cm/s².</summary>
    public const int BaseDecelerationCentimetresPerSecondSquared = 600;

    /// <summary>Braking gained per point of Agility, in cm/s².</summary>
    public const int DecelerationWeight = 15;

    /// <summary>Degrees a tick a player turns flat out at Agility 0.</summary>
    public const int BaseTurnDegrees = 8;

    /// <summary>The energy drain multiplier at Stamina 0 and WorkRate 0, in basis points.</summary>
    public const int DrainBaseBasisPoints = 14_500;

    /// <summary>Drain multiplier removed per point of Stamina, in basis points.</summary>
    public const int DrainStaminaWeight = 450;

    /// <summary>Drain multiplier added per point of WorkRate, in basis points.</summary>
    public const int DrainWorkRateWeight = 40;

    /// <summary>The recovery per tick of a player walking or standing, in energy units (one hundred-millionth of the tank).</summary>
    public const int WalkRecovery = 100;

    /// <summary>The drain per tick of a jogging player at the average multiplier, in energy units.</summary>
    public const int JogDrain = 600;

    /// <summary>The drain per tick at top speed at the average multiplier, in energy units.</summary>
    public const int SprintDrain = 6_000;

    /// <summary>Below this share of top speed (basis points) a player is walking and recovers.</summary>
    public const int WalkCeilingBasisPoints = 3_000;

    /// <summary>Above this share of top speed (basis points) a player is sprinting.</summary>
    public const int SprintFloorBasisPoints = 7_000;

    private const int BasisPoints = 10_000;

    /// <summary>The slowest a tired player runs, as a share of his fresh top speed, in basis points.</summary>
    private const int TiredSpeedFloorBasisPoints = 7_000;

    /// <summary>Below this speed (1 m/s) a player just faces where he is going.</summary>
    private static readonly int SnapSpeed = TickSpatialUnits.SpeedToFixedPerTick(100);

    /// <summary>The lowest share of speed a badly misaligned player keeps, in basis points.</summary>
    private const int MinAlignmentBasisPoints = 2_500;

    /// <summary>Within this distance (half a pitch unit) of an arrival point the player counts as there.</summary>
    private const int ArriveToleranceFixed = 500;

    /// <summary>Gets a player's top speed at his current energy, in fixed units per tick.</summary>
    /// <param name="player">The player.</param>
    /// <param name="profile">His athletic limits.</param>
    public static int EffectiveTopSpeed(in TickPlayerState player, in TickPlayerProfile profile)
    {
        var energyBasisPoints = (int)((long)player.Energy * BasisPoints / EnergyFull);
        var factor = TiredSpeedFloorBasisPoints
            + ((BasisPoints - TiredSpeedFloorBasisPoints) * energyBasisPoints / BasisPoints);

        return (int)((long)profile.TopSpeed * factor / BasisPoints);
    }

    /// <summary>Advances a player one tick (100 ms) towards an intent.</summary>
    /// <param name="player">The player, updated in place.</param>
    /// <param name="profile">His athletic limits.</param>
    /// <param name="intent">Where he wants to go.</param>
    public static void Step(ref TickPlayerState player, in TickPlayerProfile profile, in TickMoveIntent intent)
    {
        var dx = (long)TickSpatialUnits.ToFixed(intent.TargetXUnits) - player.X;
        var dy = (long)TickSpatialUnits.ToFixed(intent.TargetYUnits) - player.Y;
        var distance = SpatialMath.Sqrt((dx * dx) + (dy * dy));

        var topSpeed = EffectiveTopSpeed(player, profile);
        var speedCap = (int)((long)topSpeed * Math.Clamp(intent.SpeedLimitBasisPoints, 0, BasisPoints) / BasisPoints);

        if (intent.Arrive)
        {
            speedCap = distance <= ArriveToleranceFixed
                ? 0
                : (int)Math.Min(speedCap, SpatialMath.Sqrt(2L * profile.Deceleration * distance));
        }

        var misalignment = Turn(ref player, profile, topSpeed, dx, dy);
        var alignment = Math.Max(MinAlignmentBasisPoints, TickTrigonometry.Cos(misalignment));
        var desiredSpeed = (int)((long)speedCap * alignment / BasisPoints);

        player.Speed = desiredSpeed > player.Speed
            ? Math.Min(desiredSpeed, player.Speed + profile.Acceleration)
            : Math.Max(desiredSpeed, player.Speed - profile.Deceleration);

        Advance(ref player, intent.Arrive, distance);
        SpendEnergy(ref player, profile);
    }

    /// <summary>Turns the player towards a vector as far as his agility allows and returns the angle still to go.</summary>
    private static int Turn(ref TickPlayerState player, in TickPlayerProfile profile, int topSpeed, long dx, long dy)
    {
        if (dx == 0 && dy == 0)
        {
            return 0;
        }

        var wanted = TickTrigonometry.AngleOf(dx, dy);

        if (player.Speed < SnapSpeed)
        {
            player.Heading = wanted;

            return 0;
        }

        var gap = TickTrigonometry.Difference(player.Heading, wanted);
        var speedBasisPoints = topSpeed == 0
            ? 0
            : (int)Math.Min(BasisPoints, (long)player.Speed * BasisPoints / topSpeed);

        // Four times the flat-out rate standing still, falling linearly to the flat-out rate at top speed.
        var turnLimit = (int)((long)profile.TurnRateAtTopSpeed
            * (BasisPoints + (3 * (BasisPoints - speedBasisPoints))) / BasisPoints / 100);
        var turn = Math.Clamp(gap, -turnLimit, turnLimit);

        player.Heading = TickTrigonometry.Normalize(player.Heading + turn);

        return gap - turn;
    }

    private static void Advance(ref TickPlayerState player, bool arrive, long distance)
    {
        var step = player.Speed;

        if (arrive && step >= distance)
        {
            // Never run past the point he was told to stop on.
            step = (int)distance;
            player.Speed = 0;
        }

        player.X = Math.Clamp(
            player.X + (int)((long)TickTrigonometry.Cos(player.Heading) * step / TickTrigonometry.Scale),
            0,
            TickSpatialUnits.PitchLengthFixed);
        player.Y = Math.Clamp(
            player.Y + (int)((long)TickTrigonometry.Sin(player.Heading) * step / TickTrigonometry.Scale),
            0,
            TickSpatialUnits.PitchWidthFixed);
    }

    private static void SpendEnergy(ref TickPlayerState player, in TickPlayerProfile profile)
    {
        var shareOfTop = profile.TopSpeed == 0
            ? 0
            : (int)Math.Min(BasisPoints, (long)player.Speed * BasisPoints / profile.TopSpeed);

        if (shareOfTop < WalkCeilingBasisPoints)
        {
            player.Energy = Math.Min(EnergyFull, player.Energy + WalkRecovery);

            return;
        }

        var drain = shareOfTop <= SprintFloorBasisPoints
            ? JogDrain
            : JogDrain + ((SprintDrain - JogDrain) * (shareOfTop - SprintFloorBasisPoints) / (BasisPoints - SprintFloorBasisPoints));

        player.Energy = Math.Max(0, player.Energy - (int)((long)drain * profile.DrainBasisPoints / BasisPoints));
    }
}
