using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>How the goalkeeper dealt with a shot.</summary>
internal enum TickSaveOutcome
{
    /// <summary>The shot was out of his reach: it goes in.</summary>
    Beaten = 0,

    /// <summary>He held it. The ball is his.</summary>
    Caught = 1,

    /// <summary>He got a hand to it and the ball runs loose in play, away from the goal.</summary>
    Parried = 2,

    /// <summary>He tipped it round the post or over the bar. It goes out for a corner.</summary>
    TippedAround = 3,
}

/// <summary>
/// Where a shot in flight is going, found by playing it forward on a scratch ball.
/// </summary>
/// <param name="OnTarget">Whether the shot ends in the net at the goalkeeper's end if nobody touches it.</param>
/// <param name="Boundary">How the forecast flight ended: a goal, the woodwork, out of play, or <see cref="TickBallBoundary.InPlay"/> if it was still going after <see cref="TickShotStopper.ForecastTicks"/> ticks.</param>
/// <param name="Ticks">The ticks from now to that end.</param>
/// <param name="PlaneTicks">The ticks from now until the ball reaches the goalkeeper's depth (the plane across his body), or 0 if it never does.</param>
/// <param name="PlaneY">The ball's Y at that plane, in pitch units.</param>
/// <param name="PlaneZ">The ball's height at that plane, in Z units.</param>
/// <param name="PlaneSpeed">The ball's ground speed at that plane, in fixed units per tick.</param>
internal readonly record struct TickShotForecast(
    bool OnTarget,
    TickBallBoundary Boundary,
    int Ticks,
    int PlaneTicks,
    int PlaneY,
    int PlaneZ,
    int PlaneSpeed)
{
    /// <summary>A flight that is no shot: the ball is not going to the goalkeeper's goal.</summary>
    public static readonly TickShotForecast None = new(false, TickBallBoundary.InPlay, 0, 0, 0, 0, 0);
}

/// <summary>
/// What a goalkeeper can do about a shot, worked out from his body and his skills at the moment of the save.
/// </summary>
/// <param name="Needed">How far the ball passes from where he stands, sideways, in pitch units.</param>
/// <param name="ParryReach">The farthest sideways he can get a hand to the ball, in pitch units.</param>
/// <param name="CatchReach">The farthest sideways he can hold it, in pitch units.</param>
/// <param name="HeightReach">The highest he can reach, in Z units.</param>
/// <param name="HoldBasisPoints">The chance that a ball he can hold is held rather than spilled, in basis points.</param>
/// <param name="TipBasisPoints">The chance that a ball he gets a hand to is turned out for a corner, in basis points.</param>
internal readonly record struct TickSaveAssessment(
    int Needed,
    int ParryReach,
    int CatchReach,
    int HeightReach,
    int HoldBasisPoints,
    int TipBasisPoints);

/// <summary>
/// Shot-stopping and dive physics (`tick-engine-v1`, Milestone 6).
/// </summary>
/// <remarks>
/// <para>
/// A shot is not a dice roll that the replay then dresses up: the goalkeeper runs at the ball while it is in the air, and the
/// save is judged by where he has got to. The pipeline, in the order the tick loop calls it:
/// </para>
/// <list type="number">
/// <item><description>
/// <b><see cref="Forecast"/></b> at the tick the shot is struck. The ball is copied onto a scratch ball and stepped with the real
/// <see cref="TickBallPhysics"/> until it ends (a goal, the woodwork, out of play), noting when it reaches the goalkeeper's depth and
/// at what height and speed. Nothing is guessed: the forecast <em>is</em> the physics.
/// </description></item>
/// <item><description>
/// <b><see cref="DiveIntent"/></b> each tick. The goalkeeper stands still for his reaction time (<c>350 ms − 10 ms × Reflexes</c>: 150 to
/// 340 ms), then runs flat out across the line of the ball. Where he gets to is ordinary player physics, so a quick keeper
/// covers more ground than a slow one and one on a good arc has less to cover.
/// </description></item>
/// <item><description>
/// <b><see cref="ShouldResolve"/></b> before each ball step. When the next step would carry the ball through his plane, the loop
/// calls <see cref="Assess"/>, <see cref="Resolve(in TickSaveAssessment, int, int, int)"/> and <see cref="Apply"/> <em>instead of</em> stepping.
/// </description></item>
/// </list>
/// <para>
/// <b>Reach.</b> At the moment of the save he can get a hand to a ball that passes within <c>70 + 40 + 6 × Agility</c> pitch units of
/// him sideways (arms plus a dive: 1.7 m at Agility 10 to 2.4 m at 20). He can <em>hold</em> a ball within <c>45% + 2.5% × Handling</c> of
/// that. Upward he reaches <c>20 + 0.4 × (JumpingReach + AerialAbility)</c> Z units (1.5 to 2.5 m), and holds nothing above 85% of that.
/// A ball outside the reach is <see cref="TickSaveOutcome.Beaten"/> with no draw to soften it: a corner shot from close range beats an
/// ordinary keeper, and a weak shot from long range never beats a good one.
/// </para>
/// <para>
/// <b>Inside the reach</b> two draws are always taken, whatever the outcome, so a shot consumes the same amount of the stream every
/// time. The first decides whether he holds a ball within his catching reach: <c>35% + 3.3% × Handling</c>, less 0.05% for every cm/s
/// the ball is faster than 12 m/s, between 5% and 95%. A ball he fails to hold, or cannot reach to hold, is parried: the second draw
/// turns it out for a corner with the chance <c>20% + 60% × (sideways stretch ÷ parry reach)</c>, so a comfortable save rebounds
/// into play and a full-length one goes behind. A ball above 22 Z units that is turned out goes over the bar rather than round the post.
/// </para>
/// </remarks>
internal static class TickShotStopper
{
    /// <summary>The most ticks a forecast plays forward (4 s).</summary>
    public const int ForecastTicks = 40;

    /// <summary>The goalkeeper's reaction time at Reflexes 0, in milliseconds.</summary>
    public const int ReactionBaseMilliseconds = 350;

    /// <summary>The reaction time saved per point of Reflexes, in milliseconds.</summary>
    public const int ReactionPerReflexes = 10;

    /// <summary>The goalkeeper's arm reach to either side, in pitch units (0.7 m).</summary>
    public const int BodyReach = 70;

    /// <summary>The sideways reach of a dive at Agility 0, in pitch units.</summary>
    public const int DiveBase = 40;

    /// <summary>The dive reach gained per point of Agility, in pitch units.</summary>
    public const int DivePerAgility = 6;

    /// <summary>The share of the parry reach a goalkeeper of Handling 0 can hold, in percent.</summary>
    public const int CatchShareBase = 45;

    /// <summary>The share of the parry reach gained per point of Handling, in tenths of a percent.</summary>
    public const int CatchSharePerHandlingTenths = 25;

    /// <summary>The height a goalkeeper of no jumping reach and no aerial ability reaches, in Z units (1.4 m).</summary>
    public const int HeightBase = 20;

    /// <summary>The share of a ball's height a goalkeeper may hold, as a share of his height reach, in percent.</summary>
    public const int CatchHeightPercent = 85;

    /// <summary>A turned-out ball at or above this height, in Z units, goes over the bar rather than round the post.</summary>
    public const int OverTheBarZUnits = 22;

    /// <summary>The ball speed, in cm/s, below which the speed costs the goalkeeper nothing when he tries to hold it.</summary>
    public const int EasySpeedCentimetresPerSecond = 1_200;

    /// <summary>The speed a parried ball leaves the goalkeeper at, in cm/s.</summary>
    public const int ParrySpeedCentimetresPerSecond = 800;

    /// <summary>The speed a ball tipped round the post leaves at, in cm/s.</summary>
    public const int TipSpeedCentimetresPerSecond = 500;

    /// <summary>The speed a ball tipped over the bar leaves at, in cm/s.</summary>
    public const int TipOverSpeedCentimetresPerSecond = 700;

    /// <summary>How far outside the nearer post a ball tipped round it is sent, in pitch units.</summary>
    public const int TipWideOfPost = 600;

    private const int BasisPoints = 10_000;
    private const int HoldBase = 3_500;
    private const int HoldPerHandling = 330;
    private const int HoldSpeedPenaltyPerCentimetre = 5;
    private const int MinimumHold = 500;
    private const int MaximumHold = 9_500;
    private const int TipBase = 2_000;
    private const int TipStretch = 6_000;
    private const int MaximumTip = 8_000;
    private const int FixedPerMetreDivisor = 95_238;
    private const int ParryAngleBase = 80;
    private const int ParryAngleSpread = 100;

    /// <summary>Gets how long the goalkeeper takes to react to a shot, in milliseconds.</summary>
    /// <param name="skills">The goalkeeper's skills.</param>
    public static int ReactionMilliseconds(in TickPlayerSkills skills) =>
        ReactionBaseMilliseconds - (ReactionPerReflexes * skills.Reflexes);

    /// <summary>
    /// Plays the ball forward on a scratch ball and reports where it ends and when it passes the goalkeeper.
    /// </summary>
    /// <param name="ball">The real ball, which is not touched.</param>
    /// <param name="scratch">A spare ball the forecast is played on; its state afterwards is meaningless.</param>
    /// <param name="keeperIsHome">Whether the goalkeeper is the home side's (defending X = 0).</param>
    /// <param name="keeperX">The goalkeeper's X in fixed units: the depth of the plane across his body.</param>
    /// <returns>The forecast, or <see cref="TickShotForecast.None"/> when the ball is held or is not going towards his goal.</returns>
    public static TickShotForecast Forecast(TickBallPhysics ball, TickBallPhysics scratch, bool keeperIsHome, int keeperX)
    {
        ArgumentNullException.ThrowIfNull(ball);
        ArgumentNullException.ThrowIfNull(scratch);

        if (ball.Mode == TickBallMode.Controlled || (keeperIsHome ? ball.VelocityX >= 0 : ball.VelocityX <= 0))
        {
            return TickShotForecast.None;
        }

        var ownKeeperX = keeperIsHome ? keeperX : TickSpatialUnits.PitchLengthFixed - keeperX;
        var planeTicks = 0;
        var planeY = 0;
        var planeZ = 0;
        var planeSpeed = 0;

        scratch.CopyFrom(ball);

        for (var tick = 1; tick <= ForecastTicks; tick++)
        {
            var boundary = scratch.Step();
            var ownX = keeperIsHome ? scratch.X : TickSpatialUnits.PitchLengthFixed - scratch.X;

            if (planeTicks == 0 && ownX <= ownKeeperX)
            {
                planeTicks = tick;
                planeY = scratch.UnitY;
                planeZ = scratch.UnitZ;
                planeSpeed = scratch.GroundSpeed;
            }

            if (boundary != TickBallBoundary.InPlay)
            {
                var goal = boundary == (keeperIsHome ? TickBallBoundary.GoalHomeEnd : TickBallBoundary.GoalAwayEnd);

                return new TickShotForecast(goal, boundary, tick, planeTicks == 0 ? tick : planeTicks, planeTicks == 0 ? scratch.UnitY : planeY, planeTicks == 0 ? scratch.UnitZ : planeZ, planeSpeed);
            }
        }

        return TickShotForecast.None;
    }

    /// <summary>
    /// Tells the goalkeeper where to go: nowhere until he has reacted, then flat out across the line of the ball at the depth he stands.
    /// </summary>
    /// <param name="keeper">The goalkeeper's body.</param>
    /// <param name="skills">The goalkeeper's skills.</param>
    /// <param name="forecast">The shot's forecast.</param>
    /// <param name="ticksSinceShot">The ticks since the forecast was made.</param>
    public static TickMoveIntent DiveIntent(in TickPlayerState keeper, in TickPlayerSkills skills, in TickShotForecast forecast, int ticksSinceShot)
    {
        var here = TickSpatialUnits.ToUnits(keeper.X);

        if (!forecast.OnTarget || ticksSinceShot * TickSpatialUnits.TickDeltaMs < ReactionMilliseconds(skills))
        {
            // Still reading the shot: he does not move his feet.
            return new TickMoveIntent(here, TickSpatialUnits.ToUnits(keeper.Y), 0, true);
        }

        return new TickMoveIntent(here, forecast.PlaneY, BasisPoints, false);
    }

    /// <summary>Tells the loop to settle the save instead of stepping the ball: the next step would carry it through the goalkeeper's plane.</summary>
    /// <param name="forecast">The shot's forecast.</param>
    /// <param name="ticksSinceShot">The ticks since the forecast was made.</param>
    public static bool ShouldResolve(in TickShotForecast forecast, int ticksSinceShot) =>
        forecast.OnTarget && forecast.PlaneTicks - ticksSinceShot <= 1;

    /// <summary>Works out what the goalkeeper can do about the shot from where he stands.</summary>
    /// <param name="keeper">The goalkeeper's body, as it is when the ball reaches him.</param>
    /// <param name="skills">The goalkeeper's skills.</param>
    /// <param name="forecast">The shot's forecast.</param>
    public static TickSaveAssessment Assess(in TickPlayerState keeper, in TickPlayerSkills skills, in TickShotForecast forecast)
    {
        var needed = Math.Abs(forecast.PlaneY - TickSpatialUnits.ToUnits(keeper.Y));
        var parry = BodyReach + DiveBase + (DivePerAgility * skills.Agility);
        var share = CatchShareBase + (CatchSharePerHandlingTenths * skills.Handling / 10);
        var height = HeightBase + ((skills.JumpingReach + skills.AerialAbility) * 2 / 5);

        var speed = (int)((long)forecast.PlaneSpeed * 1_000 / FixedPerMetreDivisor);
        var hold = Math.Clamp(
            HoldBase + (HoldPerHandling * skills.Handling) - (Math.Max(0, speed - EasySpeedCentimetresPerSecond) * HoldSpeedPenaltyPerCentimetre),
            MinimumHold,
            MaximumHold);
        var tip = Math.Clamp(TipBase + (TipStretch * needed / Math.Max(1, parry)), 0, MaximumTip);

        return new TickSaveAssessment(needed, parry, parry * share / 100, height, hold, tip);
    }

    /// <summary>Settles a save from two draws.</summary>
    /// <param name="assessment">What the goalkeeper can reach.</param>
    /// <param name="planeZ">The ball's height as it passes him, in Z units.</param>
    /// <param name="holdRoll">A draw in basis points, 0..9,999: whether he holds the ball.</param>
    /// <param name="deflectRoll">A draw in basis points, 0..9,999: whether a ball he gets a hand to goes behind.</param>
    public static TickSaveOutcome Resolve(in TickSaveAssessment assessment, int planeZ, int holdRoll, int deflectRoll)
    {
        if (assessment.Needed > assessment.ParryReach || planeZ > assessment.HeightReach)
        {
            return TickSaveOutcome.Beaten;
        }

        if (assessment.Needed <= assessment.CatchReach
            && planeZ <= assessment.HeightReach * CatchHeightPercent / 100
            && holdRoll < assessment.HoldBasisPoints)
        {
            return TickSaveOutcome.Caught;
        }

        return deflectRoll < assessment.TipBasisPoints ? TickSaveOutcome.TippedAround : TickSaveOutcome.Parried;
    }

    /// <summary>Settles a save, taking two draws from the stream whatever the outcome.</summary>
    /// <param name="assessment">What the goalkeeper can reach.</param>
    /// <param name="planeZ">The ball's height as it passes him, in Z units.</param>
    /// <param name="random">The play stream.</param>
    /// <param name="deflectRoll">The second draw, which <see cref="Apply"/> also uses to pick the angle of a rebound.</param>
    public static TickSaveOutcome Resolve(in TickSaveAssessment assessment, int planeZ, Pcg32 random, out int deflectRoll)
    {
        ArgumentNullException.ThrowIfNull(random);

        var holdRoll = random.NextBasisPoints();

        deflectRoll = random.NextBasisPoints();

        return Resolve(assessment, planeZ, holdRoll, deflectRoll);
    }

    /// <summary>Carries a save onto the goalkeeper and the ball.</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Beaten.</b> Nothing changes; the loop steps the ball on into the net.</description></item>
    /// <item><description><b>Caught.</b> He dives onto the ball and holds it.</description></item>
    /// <item><description><b>Parried.</b> The ball runs off at <see cref="ParrySpeedCentimetresPerSecond"/> upfield, 28° to 63° off the axis of the goal towards the flank the shot came from.</description></item>
    /// <item><description><b>TippedAround.</b> The ball is sent past the nearer post (or over the bar if it was high), and the pitch boundary turns it into a corner.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="outcome">What <see cref="Resolve(in TickSaveAssessment, int, int, int)"/> decided.</param>
    /// <param name="keeper">The goalkeeper, updated in place.</param>
    /// <param name="keeperIndex">The goalkeeper's index, as the ball knows its controller.</param>
    /// <param name="keeperIsHome">Whether the goalkeeper is the home side's.</param>
    /// <param name="ball">The ball, updated in place.</param>
    /// <param name="deflectRoll">The second draw of the save.</param>
    public static void Apply(TickSaveOutcome outcome, ref TickPlayerState keeper, int keeperIndex, bool keeperIsHome, TickBallPhysics ball, int deflectRoll)
    {
        ArgumentNullException.ThrowIfNull(ball);

        if (outcome == TickSaveOutcome.Beaten)
        {
            return;
        }

        // He has dived to meet the ball, so he ends the save on it.
        keeper.X = ball.X;
        keeper.Y = ball.Y;
        keeper.Speed = 0;
        keeper.Heading = keeperIsHome ? 0 : TickTrigonometry.HalfTurn;

        switch (outcome)
        {
            case TickSaveOutcome.Caught:
                ball.Attach(keeperIndex);
                break;

            case TickSaveOutcome.Parried:
                Parry(keeperIsHome, ball, deflectRoll);
                break;

            default:
                TipOut(keeperIsHome, ball);
                break;
        }
    }

    private static void Parry(bool keeperIsHome, TickBallPhysics ball, int deflectRoll)
    {
        var angle = ParryAngleBase + (deflectRoll % ParryAngleSpread);
        var speed = TickSpatialUnits.SpeedToFixedPerTick(ParrySpeedCentimetresPerSecond);
        var side = ball.UnitY >= SpatialPitch.GoalYCenter ? 1 : -1;
        var upfield = (int)((long)TickTrigonometry.Cos(angle) * speed / TickTrigonometry.Scale);
        var across = (int)((long)TickTrigonometry.Sin(angle) * speed / TickTrigonometry.Scale);

        ball.Kick(keeperIsHome ? upfield : -upfield, side * across, 0);
    }

    private static void TipOut(bool keeperIsHome, TickBallPhysics ball)
    {
        var lineX = keeperIsHome ? 0 : SpatialPitch.PitchLength;

        if (ball.UnitZ >= OverTheBarZUnits)
        {
            // Pushed up and back over the bar: high and fast enough to clear it before it comes down.
            var speed = TickSpatialUnits.SpeedToFixedPerTick(TipOverSpeedCentimetresPerSecond);

            ball.Kick(keeperIsHome ? -speed : speed, 0, 8 * TickSpatialUnits.GravityFixed);

            return;
        }

        var side = ball.UnitY >= SpatialPitch.GoalYCenter ? 1 : -1;
        var wide = SpatialPitch.GoalYCenter + (side * (((SpatialPitch.GoalYMax - SpatialPitch.GoalYMin) / 2) + TipWideOfPost));

        ball.LaunchRolling(lineX, wide, TickSpatialUnits.SpeedToFixedPerTick(TipSpeedCentimetresPerSecond));
    }
}
