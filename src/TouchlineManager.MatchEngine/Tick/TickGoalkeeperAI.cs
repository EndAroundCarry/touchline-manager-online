using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>What the goalkeeper is doing without a shot to face.</summary>
internal enum TickKeeperMode
{
    /// <summary>Standing his ground: on the arc between the ball and the middle of his goal, or on his anchor while the ball is far.</summary>
    Guard = 0,

    /// <summary>Charging out to smother a lone attacker or to collect a loose ball in his area.</summary>
    Rush = 1,
}

/// <summary>Who has the ball, as the goalkeeper's side sees it.</summary>
internal enum TickKeeperPossession
{
    /// <summary>Nobody has it at his feet.</summary>
    Loose = 0,

    /// <summary>One of the goalkeeper's own side has it.</summary>
    HeldByOwn = 1,

    /// <summary>One of the opposition has it.</summary>
    HeldByOpponent = 2,
}

/// <summary>How a goalkeeper's challenge on a lone attacker ended.</summary>
internal enum TickSmotherOutcome
{
    /// <summary>He gathered the ball at the attacker's feet.</summary>
    Claimed = 0,

    /// <summary>He got a hand to it and knocked it away from the attacker; it is loose.</summary>
    Spilled = 1,

    /// <summary>The attacker rode the challenge and went round him; the goalkeeper is left on the floor.</summary>
    Beaten = 2,

    /// <summary>The challenge was a foul: a penalty, as the keeper is in his own area.</summary>
    Foul = 3,
}

/// <summary>
/// What the goalkeeper should do this tick.
/// </summary>
/// <param name="Mode">Whether he guards his goal or charges out.</param>
/// <param name="Intent">Where he runs and how hard; the caller feeds it to <see cref="TickPlayerPhysics.Step"/>.</param>
internal readonly record struct TickKeeperOrder(TickKeeperMode Mode, TickMoveIntent Intent);

/// <summary>
/// Everything the goalkeeper's positioning reads about one moment of play.
/// </summary>
internal readonly ref struct TickKeeperSituation
{
    /// <summary>Gets a value indicating whether the goalkeeper is the home side's (defending X = 0).</summary>
    public required bool IsHome { get; init; }

    /// <summary>Gets the goalkeeper's body.</summary>
    public required TickPlayerState Keeper { get; init; }

    /// <summary>Gets the goalkeeper's skills.</summary>
    public required TickPlayerSkills Skills { get; init; }

    /// <summary>Gets his anchor from <see cref="TickTacticalGeometry.Resolve"/>, in absolute pitch units: where he stands while the ball is far.</summary>
    public required SpatialPoint Anchor { get; init; }

    /// <summary>Gets the ball.</summary>
    public required TickBallPhysics Ball { get; init; }

    /// <summary>Gets who has the ball.</summary>
    public required TickKeeperPossession Possession { get; init; }

    /// <summary>Gets the goalkeeper's side, as they stand at the start of the tick; index 0 is the goalkeeper himself.</summary>
    public required ReadOnlySpan<TickPlayerState> Teammates { get; init; }

    /// <summary>Gets the opposing side, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Opponents { get; init; }

    /// <summary>Gets a value indicating whether he was already charging out last tick, which stops him pulling up half way.</summary>
    public bool WasRushing { get; init; }
}

/// <summary>
/// The goalkeeper's feet: arc positioning, the rush out, and the smother (`tick-engine-v1`, Milestone 6).
/// </summary>
/// <remarks>
/// <para>
/// All geometry is in the goalkeeper's own point of view (he defends X = 0 and counts Y from his left) and mirrored for the
/// away side, as every earlier tick-engine module does. The goal's middle is (0, 3,500).
/// </para>
/// <para>
/// <b>Arc.</b> He stands on the line from the middle of the goal to the ball. His depth off the goal line grows as the ball
/// comes: <c>1 m</c> while it is 27 m or more away, rising to <c>2 m + 0.15 m × Positioning</c> (2.2 m to 5 m, the plan's band)
/// once it is within 9 m, so a close shooter sees less of the goal. He keeps 40 units off the line, and stays between
/// the posts. While the ball is farther than 47 m, or his own side has it, he stands on his anchor, and between the two the
/// target slides from the anchor to the arc, so that he never snaps.
/// </para>
/// <para>
/// <b>Rush.</b> An opponent has the ball inside his rush range, with nobody of the goalkeeper's side between the ball and the goal:
/// he charges out at full pace to where the ball will be, and meets it with <see cref="Smother"/>. The range is
/// <c>9.4 m + 0.37 m × OneOnOnes + 0.16 m × Decisions</c> (10 m for a poor keeper, 20 m for an elite one; the plan's 18 m), and 25%
/// wider for a keeper already running, who cannot pull up half way. A loose, slow ball inside his area is his too when he gets
/// there first (within 110% of the nearest opponent's distance).
/// </para>
/// <para>
/// <b>Smother.</b> Inside <see cref="SmotherReachUnits"/> of the ball at an attacker's feet, one draw settles the duel:
/// <c>5 × OneOnOnes + 3 × Agility + 2 × Decisions</c> against the attacker's <c>4 × Dribbling + 4 × Composure + 2 × Pace</c>,
/// on the same band as a tackle. A win is mostly a clean claim; three in ten spill. Of the failures, a share that grows with the
/// keeper's Aggression is a foul (a penalty). Everything works on spans and value types, so the module allocates nothing.
/// </para>
/// </remarks>
internal static class TickGoalkeeperAI
{
    /// <summary>The distance at which the goalkeeper has moved fully onto the arc, in pitch units (27 m).</summary>
    public const int ArcRange = 2_600;

    /// <summary>The distance beyond which the goalkeeper stands on his anchor, in pitch units (47 m).</summary>
    public const int GuardRange = 4_500;

    /// <summary>The distance at which the goalkeeper has come out as far as he will, in pitch units (9 m).</summary>
    public const int NearRange = 900;

    /// <summary>The goalkeeper's depth off the line while the ball is far, in pitch units (about 1 m).</summary>
    public const int MinimumDepth = 100;

    /// <summary>The depth a goalkeeper of Positioning 0 reaches when the ball is close, in pitch units (2 m).</summary>
    public const int BaseDepth = 200;

    /// <summary>The depth gained per point of Positioning, in pitch units (0.15 m).</summary>
    public const int DepthPerPositioning = 15;

    /// <summary>The nearest the goalkeeper stands to his own goal line, in pitch units.</summary>
    public const int LineClearance = 40;

    /// <summary>How far inside the posts the goalkeeper stays, in pitch units.</summary>
    public const int PostClearance = 60;

    /// <summary>The rush range of a goalkeeper with no OneOnOnes and no Decisions, in pitch units (9 m).</summary>
    public const int RushBaseRange = 900;

    /// <summary>The rush range gained per point of OneOnOnes, in pitch units.</summary>
    public const int RushPerOneOnOnes = 35;

    /// <summary>The rush range gained per point of Decisions, in pitch units.</summary>
    public const int RushPerDecisions = 15;

    /// <summary>The share of the rush range a goalkeeper already running keeps beyond it, in percent.</summary>
    public const int RushCommitmentPercent = 125;

    /// <summary>How near a teammate between the ball and the goal must be to the line of the shot to cover it, in pitch units (7 m).</summary>
    public const int CoverReach = 700;

    /// <summary>How far a teammate may be beyond the ball and still count as goalside of it, in pitch units.</summary>
    public const int GoalsideTolerance = 100;

    /// <summary>The farthest from his goal line a rush takes him, in pitch units: just outside the area.</summary>
    public const int RushLimitX = 1_800;

    /// <summary>The half width of the area in which a loose ball or a lone attacker is the goalkeeper's concern, in pitch units.</summary>
    public const int AreaHalfWidth = 2_000;

    /// <summary>The depth of the area in which a loose ball is the goalkeeper's, in pitch units.</summary>
    public const int AreaDepth = SpatialPitch.PenaltyBoxWidth;

    /// <summary>A ball rolling faster than this (in cm/s) is a pass or a shot, not a loose ball to collect.</summary>
    public const int LooseBallSpeedCentimetresPerSecond = 400;

    /// <summary>The distance a goalkeeper may be from a loose ball, as a share of the nearest opponent's, to go for it, in percent.</summary>
    public const int LooseRacePercent = 110;

    /// <summary>The ticks ahead of a moving ball a rush is aimed.</summary>
    public const int RushLeadTicks = 3;

    /// <summary>The goalkeeper's reach on a ball at an attacker's feet, in pitch units (1.4 m).</summary>
    public const int SmotherReachUnits = 130;

    /// <summary>The share of won smothers that spill the ball rather than gather it, in percent.</summary>
    public const int SpilledPercent = 30;

    /// <summary>The ticks a beaten goalkeeper is on the floor (1.2 s).</summary>
    public const int BeatenLockoutTicks = TickTackleResolver.BeatenLockoutTicks;

    /// <summary>The ticks a dispossessed attacker is off balance (0.6 s).</summary>
    public const int DispossessedLockoutTicks = TickTackleResolver.DispossessedLockoutTicks;

    /// <summary>The speed a spilled ball leaves the attacker at, in cm/s.</summary>
    public const int SpillSpeedCentimetresPerSecond = 450;

    private const int BasisPoints = 10_000;
    private const int BaseClaimBasisPoints = 5_000;
    private const int ClaimSwingBasisPoints = 3_500;
    private const int ClaimReference = 15_000;
    private const int MinimumClaimBasisPoints = 1_500;
    private const int MaximumClaimBasisPoints = 8_500;
    private const int BaseFoulBasisPoints = 800;
    private const int FoulPerAggression = 40;

    /// <summary>Gets how far from the goal's middle the ball may be for the goalkeeper to charge out, in pitch units.</summary>
    /// <param name="skills">The goalkeeper's skills.</param>
    public static int RushRange(in TickPlayerSkills skills) =>
        RushBaseRange + (RushPerOneOnOnes * skills.OneOnOnes) + (RushPerDecisions * skills.Decisions);

    /// <summary>
    /// Gets how far off his line the goalkeeper stands with the ball a distance from the middle of his goal, in pitch units.
    /// </summary>
    /// <param name="skills">The goalkeeper's skills.</param>
    /// <param name="ballDistance">The distance from the middle of his goal line to the ball, in pitch units.</param>
    public static int ArcDepth(in TickPlayerSkills skills, int ballDistance)
    {
        var closeness = Math.Clamp((ArcRange - ballDistance) * BasisPoints / (ArcRange - NearRange), 0, BasisPoints);
        var deepest = BaseDepth + (DepthPerPositioning * skills.Positioning);

        return MinimumDepth + ((deepest - MinimumDepth) * closeness / BasisPoints);
    }

    /// <summary>Chooses where the goalkeeper goes this tick when there is no shot to face.</summary>
    /// <param name="situation">The moment of play.</param>
    public static TickKeeperOrder Decide(in TickKeeperSituation situation)
    {
        ArgumentNullException.ThrowIfNull(situation.Ball);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(situation.Teammates.Length, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(situation.Opponents.Length, TickTacticalGeometry.TeamSize);

        var ballX = MirrorX(situation.Ball.UnitX, situation.IsHome);
        var ballY = MirrorY(situation.Ball.UnitY, situation.IsHome);

        if (TryRush(situation, ballX, ballY, out var rush))
        {
            return new TickKeeperOrder(TickKeeperMode.Rush, new TickMoveIntent(rush.X, rush.Y, BasisPoints, false));
        }

        return Guard(situation, ballX, ballY);
    }

    /// <summary>Gets whether the goalkeeper is close enough to the ball to challenge for it at an attacker's feet.</summary>
    /// <param name="keeper">The goalkeeper's body.</param>
    /// <param name="ball">The ball.</param>
    public static bool CanSmother(in TickPlayerState keeper, TickBallPhysics ball)
    {
        ArgumentNullException.ThrowIfNull(ball);

        if (keeper.Lockout > 0 || ball.UnitZ > TickBallPhysics.GroundContactZUnits)
        {
            return false;
        }

        long dx = ball.X - keeper.X;
        long dy = ball.Y - keeper.Y;
        var reach = (long)TickSpatialUnits.ToFixed(SmotherReachUnits);

        return (dx * dx) + (dy * dy) < reach * reach;
    }

    /// <summary>Gets the chance, in basis points, that the goalkeeper wins the ball from a lone attacker.</summary>
    /// <param name="keeper">The goalkeeper's skills.</param>
    /// <param name="attacker">The attacker's skills.</param>
    public static int ClaimChanceBasisPoints(in TickPlayerSkills keeper, in TickPlayerSkills attacker)
    {
        var save = ((5 * keeper.OneOnOnes) + (3 * keeper.Agility) + (2 * keeper.Decisions)) * 100;
        var beat = ((4 * attacker.Dribbling) + (4 * attacker.Composure) + (2 * attacker.Pace)) * 100;

        return Probability.Band(
            BaseClaimBasisPoints + Probability.Swing(save - beat, ClaimSwingBasisPoints, ClaimReference),
            MinimumClaimBasisPoints,
            MaximumClaimBasisPoints);
    }

    /// <summary>Gets the chance, in basis points, that a smother which fails is a foul.</summary>
    /// <param name="keeper">The goalkeeper's skills.</param>
    public static int FoulChanceBasisPoints(in TickPlayerSkills keeper) =>
        BaseFoulBasisPoints + (FoulPerAggression * keeper.Aggression);

    /// <summary>Settles a smother from one draw.</summary>
    /// <param name="keeper">The goalkeeper's skills.</param>
    /// <param name="attacker">The attacker's skills.</param>
    /// <param name="roll">A draw in basis points, 0..9,999.</param>
    public static TickSmotherOutcome Smother(in TickPlayerSkills keeper, in TickPlayerSkills attacker, int roll)
    {
        var win = ClaimChanceBasisPoints(keeper, attacker);

        if (roll < win)
        {
            return roll < win * SpilledPercent / 100 ? TickSmotherOutcome.Spilled : TickSmotherOutcome.Claimed;
        }

        // The roll is rescaled over the failures so one draw decides all of it.
        var failed = (roll - win) * BasisPoints / (BasisPoints - win);

        return failed < FoulChanceBasisPoints(keeper) ? TickSmotherOutcome.Foul : TickSmotherOutcome.Beaten;
    }

    /// <summary>Carries a smother's result onto the two bodies and the ball.</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Claimed.</b> The ball is the goalkeeper's; the attacker is off balance for <see cref="DispossessedLockoutTicks"/>.</description></item>
    /// <item><description><b>Spilled.</b> The ball runs off away from the goalkeeper at <see cref="SpillSpeedCentimetresPerSecond"/>; the attacker is off balance.</description></item>
    /// <item><description><b>Beaten.</b> The goalkeeper is out of the play for <see cref="BeatenLockoutTicks"/>; the attacker keeps the ball.</description></item>
    /// <item><description><b>Foul.</b> The ball stops where it is, dead; the penalty belongs to the match state machine.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="outcome">What <see cref="Smother"/> decided.</param>
    /// <param name="keeper">The goalkeeper, updated in place.</param>
    /// <param name="attacker">The attacker, updated in place.</param>
    /// <param name="keeperIndex">The goalkeeper's index, as the ball knows its controller.</param>
    /// <param name="keeperIsHome">Whether the goalkeeper is the home side's.</param>
    /// <param name="ball">The ball, updated in place.</param>
    public static void ApplySmother(
        TickSmotherOutcome outcome,
        ref TickPlayerState keeper,
        ref TickPlayerState attacker,
        int keeperIndex,
        bool keeperIsHome,
        TickBallPhysics ball)
    {
        ArgumentNullException.ThrowIfNull(ball);

        switch (outcome)
        {
            case TickSmotherOutcome.Claimed:
                attacker.Lockout = DispossessedLockoutTicks;
                attacker.Speed /= 2;
                ball.Attach(keeperIndex);
                break;

            case TickSmotherOutcome.Spilled:
                attacker.Lockout = DispossessedLockoutTicks;
                attacker.Speed /= 2;
                Spill(keeper, keeperIsHome, ball);
                break;

            case TickSmotherOutcome.Beaten:
                keeper.Lockout = BeatenLockoutTicks;
                keeper.Speed = (int)((long)keeper.Speed * TickPlayerPhysics.StumbleSpeedBasisPoints / BasisPoints);
                break;

            default:
                ball.PlaceAt(ball.UnitX, ball.UnitY);
                keeper.Speed = 0;
                attacker.Speed = 0;
                break;
        }
    }

    private static TickKeeperOrder Guard(in TickKeeperSituation situation, int ballX, int ballY)
    {
        var distance = Distance(0, SpatialPitch.GoalYCenter, ballX, ballY);
        var threat = situation.Possession == TickKeeperPossession.HeldByOwn
            ? 0
            : Math.Clamp((GuardRange - distance) * BasisPoints / (GuardRange - ArcRange), 0, BasisPoints);

        var anchorX = MirrorX(situation.Anchor.X, situation.IsHome);
        var anchorY = MirrorY(situation.Anchor.Y, situation.IsHome);
        var depth = ArcDepth(situation.Skills, distance);
        var dx = (long)ballX;
        var dy = (long)ballY - SpatialPitch.GoalYCenter;
        var length = Math.Max(1, (int)SpatialMath.Sqrt((dx * dx) + (dy * dy)));

        var arcX = Math.Max(LineClearance, (int)(dx * depth / length));
        var arcY = Math.Clamp(
            SpatialPitch.GoalYCenter + (int)(dy * depth / length),
            SpatialPitch.GoalYMin + PostClearance,
            SpatialPitch.GoalYMax - PostClearance);

        var targetX = anchorX + ((arcX - anchorX) * threat / BasisPoints);
        var targetY = anchorY + ((arcY - anchorY) * threat / BasisPoints);
        var speed = 7_000 + (3_000 * threat / BasisPoints);

        return new TickKeeperOrder(
            TickKeeperMode.Guard,
            new TickMoveIntent(MirrorX(targetX, situation.IsHome), MirrorY(targetY, situation.IsHome), speed, true));
    }

    private static bool TryRush(in TickKeeperSituation situation, int ballX, int ballY, out SpatialPoint target)
    {
        target = default;

        if (situation.Possession == TickKeeperPossession.HeldByOwn || situation.Keeper.Lockout > 0)
        {
            return false;
        }

        if (situation.Possession == TickKeeperPossession.HeldByOpponent
            ? !LoneAttacker(situation, ballX, ballY)
            : !LooseBallIsHis(situation, ballX, ballY))
        {
            return false;
        }

        // Where the ball will be by the time he is there.
        var aimX = situation.Ball.UnitX + (situation.Ball.VelocityX * RushLeadTicks / TickSpatialUnits.FixedScale);
        var aimY = situation.Ball.UnitY + (situation.Ball.VelocityY * RushLeadTicks / TickSpatialUnits.FixedScale);
        var ownX = Math.Clamp(MirrorX(aimX, situation.IsHome), LineClearance, RushLimitX);
        var ownY = Math.Clamp(MirrorY(aimY, situation.IsHome), TickTacticalGeometry.Margin, SpatialPitch.PitchWidth - TickTacticalGeometry.Margin);

        target = new SpatialPoint(MirrorX(ownX, situation.IsHome), MirrorY(ownY, situation.IsHome));

        return true;
    }

    /// <summary>Tells whether an opponent with the ball is inside the goalkeeper's range with nobody of his own side between him and the goal.</summary>
    private static bool LoneAttacker(in TickKeeperSituation situation, int ballX, int ballY)
    {
        var range = RushRange(situation.Skills);

        if (situation.WasRushing)
        {
            range = range * RushCommitmentPercent / 100;
        }

        if (Distance(0, SpatialPitch.GoalYCenter, ballX, ballY) > range || Math.Abs(ballY - SpatialPitch.GoalYCenter) > AreaHalfWidth)
        {
            return false;
        }

        var reach = (long)CoverReach * CoverReach;

        for (var index = 1; index < situation.Teammates.Length; index++)
        {
            var x = MirrorX(TickSpatialUnits.ToUnits(situation.Teammates[index].X), situation.IsHome);
            var y = MirrorY(TickSpatialUnits.ToUnits(situation.Teammates[index].Y), situation.IsHome);

            if (x <= ballX + GoalsideTolerance
                && TickOffBallSupport.SegmentDistanceSquared(x, y, ballX, ballY, 0, SpatialPitch.GoalYCenter) < reach)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Tells whether a slow ball in the goalkeeper's area is his to collect: he is nearer to it than anybody else, near enough.</summary>
    private static bool LooseBallIsHis(in TickKeeperSituation situation, int ballX, int ballY)
    {
        var ball = situation.Ball;

        if (ballX > AreaDepth
            || Math.Abs(ballY - SpatialPitch.GoalYCenter) > AreaHalfWidth
            || ball.UnitZ > TickBallPhysics.HeadReachZUnits
            || ball.GroundSpeed > TickSpatialUnits.SpeedToFixedPerTick(LooseBallSpeedCentimetresPerSecond))
        {
            return false;
        }

        var keeperDistance = FixedDistance(situation.Keeper.X, situation.Keeper.Y, ball.X, ball.Y);
        var nearest = long.MaxValue;

        for (var index = 0; index < situation.Opponents.Length; index++)
        {
            nearest = Math.Min(nearest, FixedDistance(situation.Opponents[index].X, situation.Opponents[index].Y, ball.X, ball.Y));
        }

        return keeperDistance * 100 <= nearest * LooseRacePercent;
    }

    private static void Spill(in TickPlayerState keeper, bool keeperIsHome, TickBallPhysics ball)
    {
        var dx = (long)ball.X - keeper.X;
        var dy = (long)ball.Y - keeper.Y;
        var length = SpatialMath.Sqrt((dx * dx) + (dy * dy));
        var speed = TickSpatialUnits.SpeedToFixedPerTick(SpillSpeedCentimetresPerSecond);

        if (length == 0)
        {
            // Nothing to push away from: out towards the middle of the pitch.
            ball.Kick(keeperIsHome ? speed : -speed, 0, 0);

            return;
        }

        ball.Kick((int)(dx * speed / length), (int)(dy * speed / length), 0);
    }

    private static int Distance(int fromX, int fromY, int toX, int toY)
    {
        long dx = toX - fromX;
        long dy = toY - fromY;

        return (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    private static long FixedDistance(int fromX, int fromY, int toX, int toY)
    {
        long dx = toX - fromX;
        long dy = toY - fromY;

        return SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Converts an X between the pitch's and the goalkeeper's own point of view (the conversion is its own inverse).</summary>
    private static int MirrorX(int units, bool isHome) => isHome ? units : SpatialPitch.PitchLength - units;

    private static int MirrorY(int units, bool isHome) => isHome ? units : SpatialPitch.PitchWidth - units;
}
