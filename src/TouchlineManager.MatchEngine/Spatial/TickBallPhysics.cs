namespace TouchlineManager.MatchEngine.Spatial;

/// <summary>The state a tick-engine ball is in.</summary>
public enum TickBallMode
{
    /// <summary>On the grass and rolling free, slowed by friction every tick.</summary>
    Loose = 0,

    /// <summary>Locked to a player's dribbling foot, 0.3 m ahead along his heading.</summary>
    Controlled = 1,

    /// <summary>Airborne after a kick, on its first flight and before any bounce.</summary>
    Flight = 2,

    /// <summary>Airborne again after at least one bounce, losing height each time until it settles.</summary>
    Bounced = 3,
}

/// <summary>What happened at the pitch boundary or the goal frame during one tick.</summary>
public enum TickBallBoundary
{
    /// <summary>The ball is still in play.</summary>
    InPlay = 0,

    /// <summary>The ball crossed a touchline. It is dead at the crossing point.</summary>
    OutTouchline = 1,

    /// <summary>The ball crossed a goal line outside the goal frame or over the bar. It is dead at the crossing point.</summary>
    OutGoalLine = 2,

    /// <summary>The ball crossed the home goal line (X = 0) inside the frame: a goal in the home team's net.</summary>
    GoalHomeEnd = 3,

    /// <summary>The ball crossed the away goal line (X = 10,000) inside the frame: a goal in the away team's net.</summary>
    GoalAwayEnd = 4,

    /// <summary>The ball struck a post and was deflected. It is still in play.</summary>
    HitPost = 5,

    /// <summary>The ball struck the crossbar and was deflected. It is still in play.</summary>
    HitCrossbar = 6,
}

/// <summary>
/// The physical ball of the 10 Hz tick engine: position, velocity, flight, bounce, rolling friction, and the goal
/// frame.
/// </summary>
/// <remarks>
/// <para>
/// Everything is integer fixed point (<see cref="TickSpatialUnits"/>), advanced once per tick by <see cref="Step"/>.
/// One instance is created per match and mutated in place, so the tick loop allocates nothing.
/// </para>
/// <para>Per tick, in order:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Rolling</b> (Z = 0): position advances by the velocity, then the velocity is multiplied by
/// <see cref="GroundFrictionBasisPoints"/> (0.96). Below <see cref="StopSpeedFixed"/> the ball stops.
/// </description></item>
/// <item><description>
/// <b>Airborne</b>: position advances, gravity pulls the vertical velocity down by
/// <see cref="TickSpatialUnits.GravityFixed"/>, and the horizontal velocity loses <c>0.3%</c> to air drag. A ball
/// reaching the ground with downward speed bounces: vertical speed keeps <see cref="RestitutionBasisPoints"/>
/// (0.65), the horizontal speed loses 15% to the turf, and a bounce too weak to lift the ball settles it as
/// <see cref="TickBallMode.Loose"/>.
/// </description></item>
/// <item><description>
/// <b>Boundary</b>: the line the ball crossed decides the result. A goal-line crossing is tested against the frame
/// (posts 16 units to either side of the goal mouth, bar at Z 35); the rest is a goal, a deflection, or dead ball.
/// </description></item>
/// </list>
/// <para>
/// A lofted launch is solved, not simulated and hoped for: <see cref="LaunchLofted"/> picks the vertical speed so the
/// ball is back on the grass exactly at the end of a whole number of ticks, and the horizontal speed so the air drag
/// is paid for, so it lands on the target (to within the integer rounding of a few fixed units).
/// </para>
/// </remarks>
public sealed class TickBallPhysics
{
    /// <summary>Rolling friction per tick, in basis points: the speed keeps 96%.</summary>
    public const int GroundFrictionBasisPoints = 9_600;

    /// <summary>Horizontal air drag per tick, in basis points: the speed keeps 99.7%.</summary>
    public const int AirDragBasisPoints = 9_970;

    /// <summary>Vertical speed kept by a bounce, in basis points.</summary>
    public const int RestitutionBasisPoints = 6_500;

    /// <summary>Horizontal speed kept by a bounce, in basis points.</summary>
    public const int BounceFrictionBasisPoints = 8_500;

    /// <summary>A bounce whose rebound is slower than this vertical speed settles the ball on the grass.</summary>
    public const int MinReboundFixed = 2_100;

    /// <summary>A rolling ball slower than this stops: 0.5 pitch units per tick, about 5 cm/s.</summary>
    public const int StopSpeedFixed = 500;

    /// <summary>The distance a dribbled ball sits ahead of its player, in pitch units (0.3 m).</summary>
    public const int DribbleOffsetUnits = 29;

    /// <summary>How far ahead of a player the first touch of a received ball goes, in pitch units (1.0 m).</summary>
    public const int TouchLeadUnits = 100;

    /// <summary>The ticks over which a received ball is eased from where it arrived to the touch ahead.</summary>
    public const int CushionTouchTicks = 3;

    /// <summary>The ticks over which the touch is then drawn in to the dribbling offset.</summary>
    public const int CushionSettleTicks = 3;

    private const int CushionTotalTicks = CushionTouchTicks + CushionSettleTicks;

    /// <summary>The reach of a player on a ball at his feet, in pitch units (1.2 m).</summary>
    public const int GroundReceptionRadiusUnits = 120;

    /// <summary>The reach of a player on a ball in the air, in pitch units.</summary>
    public const int AerialReceptionRadiusUnits = 100;

    /// <summary>Above this height, in Z units, the ball counts as airborne for reception (the same line as <see cref="BallState.IsAerial"/>).</summary>
    public const int GroundContactZUnits = 15;

    /// <summary>Above this height, in Z units, no player can reach the ball even with a header.</summary>
    public const int HeadReachZUnits = 35;

    /// <summary>The distance within which the ball touches a post or the bar, in pitch units: a 6 cm post plus the 11 cm ball.</summary>
    public const int PostContactUnits = 16;

    /// <summary>The height band, in Z units, within which the ball touches the bar: about 17 cm.</summary>
    public const int BarContactZUnits = 3;

    /// <summary>Speed kept by a deflection off the woodwork, in basis points.</summary>
    private const int WoodworkRestitutionBasisPoints = 6_000;

    private const int BasisPoints = 10_000;

    /// <summary>The ticks since a cushioned ball was attached; <see cref="CushionTotalTicks"/> or more when there is no cushion.</summary>
    private int _cushionAge = CushionTotalTicks;

    private int _arrivalSpeedX;
    private int _arrivalSpeedY;
    private int _arrivalOffsetX;
    private int _arrivalOffsetY;

    /// <summary>Gets the X position, in fixed units.</summary>
    public int X { get; private set; }

    /// <summary>Gets the Y position, in fixed units.</summary>
    public int Y { get; private set; }

    /// <summary>Gets the height above the grass, in Z fixed units.</summary>
    public int Z { get; private set; }

    /// <summary>Gets the X velocity, in fixed units per tick.</summary>
    public int VelocityX { get; private set; }

    /// <summary>Gets the Y velocity, in fixed units per tick.</summary>
    public int VelocityY { get; private set; }

    /// <summary>Gets the vertical velocity, in Z fixed units per tick.</summary>
    public int VelocityZ { get; private set; }

    /// <summary>Gets what state the ball is in.</summary>
    public TickBallMode Mode { get; private set; }

    /// <summary>Gets the index of the player who has the ball at his feet, or -1 when nobody does.</summary>
    public int ControllerIndex { get; private set; } = -1;

    /// <summary>Gets how many times the ball has bounced since it was last launched or placed.</summary>
    public int BounceCount { get; private set; }

    /// <summary>Gets the X position, in whole pitch units.</summary>
    public int UnitX => TickSpatialUnits.ToUnits(X);

    /// <summary>Gets the Y position, in whole pitch units.</summary>
    public int UnitY => TickSpatialUnits.ToUnits(Y);

    /// <summary>Gets the height, in whole Z units.</summary>
    public int UnitZ => TickSpatialUnits.ToUnits(Z);

    /// <summary>Gets the ground point under the ball, in whole pitch units.</summary>
    public SpatialPoint GroundPoint => new(UnitX, UnitY);

    /// <summary>Gets the ground speed, in fixed units per tick.</summary>
    public int GroundSpeed => (int)SpatialMath.Sqrt(((long)VelocityX * VelocityX) + ((long)VelocityY * VelocityY));

    /// <summary>Gets a value indicating whether the ball is off the grass.</summary>
    public bool IsAirborne => Mode is TickBallMode.Flight or TickBallMode.Bounced;

    /// <summary>
    /// Copies another ball's whole state into this one, so a flight can be played forward on a scratch ball without
    /// disturbing the real one and without allocating.
    /// </summary>
    /// <param name="other">The ball to copy.</param>
    public void CopyFrom(TickBallPhysics other)
    {
        ArgumentNullException.ThrowIfNull(other);

        X = other.X;
        Y = other.Y;
        Z = other.Z;
        VelocityX = other.VelocityX;
        VelocityY = other.VelocityY;
        VelocityZ = other.VelocityZ;
        Mode = other.Mode;
        ControllerIndex = other.ControllerIndex;
        BounceCount = other.BounceCount;
        _cushionAge = other._cushionAge;
        _arrivalSpeedX = other._arrivalSpeedX;
        _arrivalSpeedY = other._arrivalSpeedY;
        _arrivalOffsetX = other._arrivalOffsetX;
        _arrivalOffsetY = other._arrivalOffsetY;
    }

    /// <summary>Puts the ball at rest on the grass at a point.</summary>
    /// <param name="xUnits">The X position, in pitch units.</param>
    /// <param name="yUnits">The Y position, in pitch units.</param>
    public void PlaceAt(int xUnits, int yUnits)
    {
        X = Math.Clamp(TickSpatialUnits.ToFixed(xUnits), 0, TickSpatialUnits.PitchLengthFixed);
        Y = Math.Clamp(TickSpatialUnits.ToFixed(yUnits), 0, TickSpatialUnits.PitchWidthFixed);
        Z = 0;
        VelocityX = 0;
        VelocityY = 0;
        VelocityZ = 0;
        Mode = TickBallMode.Loose;
        ControllerIndex = -1;
        BounceCount = 0;
    }

    /// <summary>
    /// Locks the ball to a player's dribbling foot. <see cref="Carry"/> must then be called each tick.
    /// </summary>
    /// <param name="playerIndex">The player's index.</param>
    /// <param name="cushioned">
    /// True when the ball is arriving, as a pass does: it is not snapped to his foot but runs on a tick and is eased to it
    /// (<see cref="Carry"/>), so a received ball is a first touch and not a stop. Who has the ball is the same either way.
    /// </param>
    public void Attach(int playerIndex, bool cushioned = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(playerIndex);

        _arrivalSpeedX = cushioned ? VelocityX : 0;
        _arrivalSpeedY = cushioned ? VelocityY : 0;
        _cushionAge = cushioned ? 0 : CushionTotalTicks;

        Mode = TickBallMode.Controlled;
        ControllerIndex = playerIndex;
        Z = 0;
        VelocityX = 0;
        VelocityY = 0;
        VelocityZ = 0;
        BounceCount = 0;
    }

    /// <summary>
    /// Holds a controlled ball 0.3 m ahead of its player along his heading, and gives it the player's velocity so a
    /// <see cref="Release"/> carries on at the same pace.
    /// </summary>
    /// <remarks>
    /// A cushioned ball (<see cref="Attach"/>) is not 0.3 m ahead at once. On the first tick it carries on a step the way it was
    /// travelling; over the next <see cref="CushionTouchTicks"/> its place relative to the player is eased from where it arrived to a
    /// touch <see cref="TouchLeadUnits"/> ahead along his heading, and over <see cref="CushionSettleTicks"/> more it is drawn in to the
    /// dribbling offset. Nothing about who controls the ball changes, only where the ball is drawn.
    /// </remarks>
    /// <param name="playerX">The player's X, in fixed units.</param>
    /// <param name="playerY">The player's Y, in fixed units.</param>
    /// <param name="heading">The player's heading, in binary angle units.</param>
    /// <param name="playerSpeed">The player's speed, in fixed units per tick.</param>
    public void Carry(int playerX, int playerY, int heading, int playerSpeed)
    {
        if (Mode != TickBallMode.Controlled)
        {
            throw new InvalidOperationException("Only a controlled ball can be carried.");
        }

        var offset = TickSpatialUnits.ToFixed(DribbleOffsetUnits);
        var cos = TickTrigonometry.Cos(heading);
        var sin = TickTrigonometry.Sin(heading);
        long offsetX = (long)cos * offset / TickTrigonometry.Scale;
        long offsetY = (long)sin * offset / TickTrigonometry.Scale;

        if (_cushionAge < CushionTotalTicks)
        {
            if (_cushionAge == 0)
            {
                // The ball's last step: it was moving when it reached him and has not stopped yet.
                _arrivalOffsetX = X + _arrivalSpeedX - playerX;
                _arrivalOffsetY = Y + _arrivalSpeedY - playerY;
            }

            _cushionAge++;

            var lead = TickSpatialUnits.ToFixed(TouchLeadUnits);
            long leadX = (long)cos * lead / TickTrigonometry.Scale;
            long leadY = (long)sin * lead / TickTrigonometry.Scale;

            if (_cushionAge <= CushionTouchTicks)
            {
                offsetX = _arrivalOffsetX + ((leadX - _arrivalOffsetX) * _cushionAge / CushionTouchTicks);
                offsetY = _arrivalOffsetY + ((leadY - _arrivalOffsetY) * _cushionAge / CushionTouchTicks);
            }
            else
            {
                var settled = _cushionAge - CushionTouchTicks;

                offsetX = leadX + ((offsetX - leadX) * settled / CushionSettleTicks);
                offsetY = leadY + ((offsetY - leadY) * settled / CushionSettleTicks);
            }
        }

        X = Math.Clamp(playerX + (int)offsetX, 0, TickSpatialUnits.PitchLengthFixed);
        Y = Math.Clamp(playerY + (int)offsetY, 0, TickSpatialUnits.PitchWidthFixed);
        VelocityX = (int)((long)cos * playerSpeed / TickTrigonometry.Scale);
        VelocityY = (int)((long)sin * playerSpeed / TickTrigonometry.Scale);
    }

    /// <summary>Lets a controlled ball go, rolling on with the velocity <see cref="Carry"/> gave it.</summary>
    public void Release()
    {
        Mode = TickBallMode.Loose;
        ControllerIndex = -1;
    }

    /// <summary>Gets whether a player at a position can take the ball this tick.</summary>
    /// <remarks>
    /// A ball on the grass (up to Z 15) is within reach inside 120 units; a ball in the air is within reach inside
    /// 100 units and no higher than Z 35. A ball already at someone's feet is nobody else's.
    /// </remarks>
    /// <param name="playerX">The player's X, in fixed units.</param>
    /// <param name="playerY">The player's Y, in fixed units.</param>
    public bool IsReceivableFrom(int playerX, int playerY)
    {
        if (Mode == TickBallMode.Controlled)
        {
            return false;
        }

        var heightUnits = UnitZ;

        if (heightUnits > HeadReachZUnits)
        {
            return false;
        }

        var radius = TickSpatialUnits.ToFixed(
            heightUnits > GroundContactZUnits ? AerialReceptionRadiusUnits : GroundReceptionRadiusUnits);

        long dx = X - playerX;
        long dy = Y - playerY;

        return (dx * dx) + (dy * dy) <= (long)radius * radius;
    }

    /// <summary>
    /// Sends the ball rolling from where it lies towards a point, so that it arrives there at a chosen speed.
    /// </summary>
    /// <remarks>
    /// Each tick the speed falls by a fixed share, so the speed lost over a run is exactly that share of the
    /// distance covered: <c>v0 = arrival + distance × (1 − friction)</c>. An arrival speed of 0 is a pass that dies at
    /// the target; a positive one is a pass the receiver meets at pace.
    /// </remarks>
    /// <param name="targetXUnits">The target X, in pitch units.</param>
    /// <param name="targetYUnits">The target Y, in pitch units.</param>
    /// <param name="arrivalSpeedFixed">The speed to arrive with, in fixed units per tick, not negative.</param>
    public void LaunchRolling(int targetXUnits, int targetYUnits, int arrivalSpeedFixed = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(arrivalSpeedFixed);

        var dx = (long)TickSpatialUnits.ToFixed(targetXUnits) - X;
        var dy = (long)TickSpatialUnits.ToFixed(targetYUnits) - Y;
        var distance = SpatialMath.Sqrt((dx * dx) + (dy * dy));

        Release();
        Z = 0;
        VelocityZ = 0;
        BounceCount = 0;

        if (distance == 0)
        {
            VelocityX = 0;
            VelocityY = 0;
            return;
        }

        var speed = arrivalSpeedFixed + (distance * (BasisPoints - GroundFrictionBasisPoints) / BasisPoints);

        VelocityX = (int)(dx * speed / distance);
        VelocityY = (int)(dy * speed / distance);
        Mode = TickBallMode.Loose;
    }

    /// <summary>
    /// Lofts the ball from where it lies onto a point, with a given peak height, so that it comes down on the point
    /// at the end of a whole number of ticks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The flight time follows from the peak: the vertical speed for that peak is <c>√(2·g·h)</c>, the flight is twice
    /// the time to climb, and the vertical speed is then re-solved for that whole number of ticks <c>T</c> as
    /// <c>g·(T−1)/2</c>, which puts the ball exactly on the grass at tick <c>T</c> (height after n ticks is
    /// <c>n·v − g·n·(n−1)/2</c>). The horizontal speed is the distance divided by the share of a launch speed that
    /// survives T ticks of air drag, so the drag is paid for in advance.
    /// </para>
    /// </remarks>
    /// <param name="targetXUnits">The landing X, in pitch units.</param>
    /// <param name="targetYUnits">The landing Y, in pitch units.</param>
    /// <param name="apexZUnits">The peak height, in Z units, above 0 and not above 100.</param>
    /// <returns>The flight time in ticks: the tick on which the ball lands.</returns>
    public int LaunchLofted(int targetXUnits, int targetYUnits, int apexZUnits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(apexZUnits, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(apexZUnits, TickSpatialUnits.MaxZUnits);

        const int gravity = TickSpatialUnits.GravityFixed;

        var climbSpeed = SpatialMath.Sqrt(2L * gravity * TickSpatialUnits.ToFixed(apexZUnits));
        var flightTicks = Math.Max(2, 1 + (int)(((2 * climbSpeed) + (gravity / 2)) / gravity));
        var launchSpeedZ = gravity * (flightTicks - 1) / 2;

        var dx = (long)TickSpatialUnits.ToFixed(targetXUnits) - X;
        var dy = (long)TickSpatialUnits.ToFixed(targetYUnits) - Y;

        // The distance a unit of launch speed carries over the flight: the sum of the drag series.
        long reach = 0;
        long term = BasisPoints;

        for (var tick = 0; tick < flightTicks; tick++)
        {
            reach += term;
            term = term * AirDragBasisPoints / BasisPoints;
        }

        Release();
        Mode = TickBallMode.Flight;
        BounceCount = 0;
        VelocityX = (int)(dx * BasisPoints / reach);
        VelocityY = (int)(dy * BasisPoints / reach);
        VelocityZ = launchSpeedZ;

        return flightTicks;
    }

    /// <summary>Kicks the ball with an explicit velocity. A kick with no upward speed from the grass is a ground ball.</summary>
    /// <param name="velocityX">The X velocity, in fixed units per tick.</param>
    /// <param name="velocityY">The Y velocity, in fixed units per tick.</param>
    /// <param name="velocityZ">The vertical velocity, in Z fixed units per tick.</param>
    public void Kick(int velocityX, int velocityY, int velocityZ)
    {
        Release();
        BounceCount = 0;
        VelocityX = velocityX;
        VelocityY = velocityY;

        if (velocityZ > 0 || Z > 0)
        {
            VelocityZ = velocityZ;
            Mode = TickBallMode.Flight;
        }
        else
        {
            VelocityZ = 0;
            Mode = TickBallMode.Loose;
        }
    }

    /// <summary>Puts the ball in the air at a point with no speed, to fall under gravity.</summary>
    /// <param name="xUnits">The X position, in pitch units.</param>
    /// <param name="yUnits">The Y position, in pitch units.</param>
    /// <param name="zUnits">The height, in Z units.</param>
    public void Drop(int xUnits, int yUnits, int zUnits)
    {
        PlaceAt(xUnits, yUnits);
        Z = Math.Clamp(TickSpatialUnits.ToFixed(zUnits), 0, TickSpatialUnits.MaxZFixed);
        Mode = Z > 0 ? TickBallMode.Flight : TickBallMode.Loose;
    }

    /// <summary>
    /// Advances the ball one tick (100 ms). A controlled ball does not move here; <see cref="Carry"/> moves it with its
    /// player.
    /// </summary>
    /// <returns>What happened at the boundary or the goal frame, or <see cref="TickBallBoundary.InPlay"/>.</returns>
    public TickBallBoundary Step()
    {
        if (Mode == TickBallMode.Controlled)
        {
            return TickBallBoundary.InPlay;
        }

        var previousX = X;
        var previousY = Y;
        var previousZ = Z;

        X += VelocityX;
        Y += VelocityY;

        if (IsAirborne)
        {
            StepAirborne();
        }
        else
        {
            StepRolling();
        }

        return ResolveBoundary(previousX, previousY, previousZ);
    }

    private void StepRolling()
    {
        VelocityX = (int)((long)VelocityX * GroundFrictionBasisPoints / BasisPoints);
        VelocityY = (int)((long)VelocityY * GroundFrictionBasisPoints / BasisPoints);

        if (GroundSpeed < StopSpeedFixed)
        {
            VelocityX = 0;
            VelocityY = 0;
        }
    }

    private void StepAirborne()
    {
        Z += VelocityZ;

        if (Z <= 0 && VelocityZ < 0)
        {
            Bounce();
        }
        else
        {
            VelocityZ -= TickSpatialUnits.GravityFixed;

            if (Z > TickSpatialUnits.MaxZFixed)
            {
                Z = TickSpatialUnits.MaxZFixed;
                VelocityZ = Math.Min(VelocityZ, 0);
            }

            VelocityX = (int)((long)VelocityX * AirDragBasisPoints / BasisPoints);
            VelocityY = (int)((long)VelocityY * AirDragBasisPoints / BasisPoints);
        }
    }

    private void Bounce()
    {
        var rebound = (int)((long)-VelocityZ * RestitutionBasisPoints / BasisPoints);

        BounceCount++;
        Z = 0;
        VelocityX = (int)((long)VelocityX * BounceFrictionBasisPoints / BasisPoints);
        VelocityY = (int)((long)VelocityY * BounceFrictionBasisPoints / BasisPoints);

        if (rebound < MinReboundFixed)
        {
            VelocityZ = 0;
            Mode = TickBallMode.Loose;
        }
        else
        {
            VelocityZ = rebound;
            Mode = TickBallMode.Bounced;
        }
    }

    private TickBallBoundary ResolveBoundary(int previousX, int previousY, int previousZ)
    {
        var outAlongX = X < 0 || X > TickSpatialUnits.PitchLengthFixed;
        var outAlongY = Y < 0 || Y > TickSpatialUnits.PitchWidthFixed;

        if (!outAlongX && !outAlongY)
        {
            return TickBallBoundary.InPlay;
        }

        if (outAlongX)
        {
            var lineX = X < 0 ? 0 : TickSpatialUnits.PitchLengthFixed;
            var crossY = Interpolate(previousY, Y, previousX, X, lineX);

            // Left over the side before reaching the end line: the touchline came first.
            if (crossY >= 0 && crossY <= TickSpatialUnits.PitchWidthFixed)
            {
                var crossZ = Interpolate(previousZ, Z, previousX, X, lineX);

                return ResolveGoalLine(lineX, crossY, crossZ);
            }
        }

        return ResolveTouchline(previousX, previousY, previousZ);
    }

    private TickBallBoundary ResolveGoalLine(int lineX, int crossY, int crossZ)
    {
        var postContact = TickSpatialUnits.ToFixed(PostContactUnits);
        var goalMin = TickSpatialUnits.ToFixed(TickSpatialUnits.GoalMouthMinUnits);
        var goalMax = TickSpatialUnits.ToFixed(TickSpatialUnits.GoalMouthMaxUnits);
        var bar = TickSpatialUnits.ToFixed(TickSpatialUnits.CrossbarZUnits);
        var barContact = TickSpatialUnits.ToFixed(BarContactZUnits);
        var homeEnd = lineX == 0;

        var nearPost = Math.Abs(crossY - goalMin) < postContact || Math.Abs(crossY - goalMax) < postContact;
        var withinMouth = crossY > goalMin - postContact && crossY < goalMax + postContact;

        if (withinMouth && crossZ <= bar + barContact)
        {
            if (nearPost)
            {
                return DeflectOffPost(lineX, crossY, crossZ, towardsLowY: Math.Abs(crossY - goalMin) < postContact);
            }

            if (crossZ < bar - barContact)
            {
                // Into the net: dead at the line.
                Settle(lineX, crossY, crossZ);

                return homeEnd ? TickBallBoundary.GoalHomeEnd : TickBallBoundary.GoalAwayEnd;
            }

            return DeflectOffBar(lineX, crossY, crossZ);
        }

        Settle(lineX, crossY, crossZ);

        return TickBallBoundary.OutGoalLine;
    }

    private TickBallBoundary DeflectOffPost(int lineX, int crossY, int crossZ, bool towardsLowY)
    {
        var inward = lineX == 0 ? 1 : -1;
        var along = (int)((long)Math.Abs(VelocityX) * WoodworkRestitutionBasisPoints / BasisPoints / 4);

        X = lineX + (inward * TickSpatialUnits.FixedScale);
        Y = crossY;
        Z = Math.Max(crossZ, 0);
        VelocityX = (int)((long)Math.Abs(VelocityX) * WoodworkRestitutionBasisPoints / BasisPoints) * inward;
        VelocityY = (int)((long)VelocityY * WoodworkRestitutionBasisPoints / BasisPoints) + (towardsLowY ? -along : along);

        return TickBallBoundary.HitPost;
    }

    private TickBallBoundary DeflectOffBar(int lineX, int crossY, int crossZ)
    {
        var inward = lineX == 0 ? 1 : -1;

        X = lineX + (inward * TickSpatialUnits.FixedScale);
        Y = crossY;
        Z = crossZ;
        VelocityX = (int)((long)Math.Abs(VelocityX) * WoodworkRestitutionBasisPoints / BasisPoints) * inward;
        VelocityZ = -Math.Max(
            (int)((long)Math.Abs(VelocityZ) * WoodworkRestitutionBasisPoints / BasisPoints),
            2 * TickSpatialUnits.GravityFixed);
        Mode = TickBallMode.Bounced;

        return TickBallBoundary.HitCrossbar;
    }

    private TickBallBoundary ResolveTouchline(int previousX, int previousY, int previousZ)
    {
        // Put the ball on the line where it crossed, dead.
        var lineY = Y < 0 ? 0 : TickSpatialUnits.PitchWidthFixed;
        var crossX = Interpolate(previousX, X, previousY, Y, lineY);
        var crossZ = Interpolate(previousZ, Z, previousY, Y, lineY);

        Settle(Math.Clamp(crossX, 0, TickSpatialUnits.PitchLengthFixed), lineY, crossZ);

        return TickBallBoundary.OutTouchline;
    }

    private void Settle(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = Math.Max(z, 0);
        VelocityX = 0;
        VelocityY = 0;
        VelocityZ = 0;
        Mode = TickBallMode.Loose;
    }

    /// <summary>Finds the value of one coordinate where a segment crosses a line in another.</summary>
    private static int Interpolate(int fromValue, int toValue, int fromAxis, int toAxis, int line)
    {
        long span = toAxis - fromAxis;

        if (span == 0)
        {
            return toValue;
        }

        return (int)(fromValue + ((long)(toValue - fromValue) * (line - fromAxis) / span));
    }
}
