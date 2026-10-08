using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>What the man with the ball has decided to do.</summary>
internal enum TickCarrierAction
{
    /// <summary>Hit the ball at goal.</summary>
    Shoot = 0,

    /// <summary>Play the ball to a teammate's feet or to a point he is running to.</summary>
    Pass = 1,

    /// <summary>Play the ball into the space ahead of a teammate who is sprinting behind the defensive line.</summary>
    ThroughBall = 2,

    /// <summary>Lift the ball from the flank into the penalty area.</summary>
    Cross = 3,

    /// <summary>Carry the ball forward into open space.</summary>
    Dribble = 4,

    /// <summary>Stand between the defender and the ball and wait for support.</summary>
    Shield = 5,

    /// <summary>Give the ball back to a safe teammate behind him.</summary>
    Recycle = 6,

    /// <summary>Hit the ball upfield and out of danger.</summary>
    Clear = 7,
}

/// <summary>
/// The carrier's choice and where it sends the ball or the man.
/// </summary>
/// <param name="Action">What he does.</param>
/// <param name="Receiver">The teammate the ball is meant for, or -1 when it goes to a point (a shot, a clearance, a cross into the area).</param>
/// <param name="Target">The point the ball is hit to or the man runs to, in pitch units. A shot's target lies beyond the goal line.</param>
/// <param name="Utility">The score the choice won with, for traces and tests.</param>
/// <param name="PaceBasisPoints">The share of his top speed a dribble or a shield is travelled at; 0 for a kick.</param>
/// <param name="EffectivePressure">The pressure on him after his composure, in basis points: the kick's error grows with it.</param>
internal readonly record struct TickCarrierDecision(
    TickCarrierAction Action,
    int Receiver,
    SpatialPoint Target,
    int Utility,
    int PaceBasisPoints,
    int EffectivePressure);

/// <summary>
/// Everything the ball carrier's brain reads about one moment of play. Index 0 of both sides is the goalkeeper.
/// </summary>
internal readonly ref struct TickCarrierSituation
{
    /// <summary>Gets a value indicating whether the carrier's side is the home side (attacking towards high X).</summary>
    public required bool IsHome { get; init; }

    /// <summary>Gets the side's mentality, which sets how readily it shoots and how much it values going forward.</summary>
    public required MatchMentality Mentality { get; init; }

    /// <summary>Gets the side's tempo, which sets how much it values going forward quickly.</summary>
    public required MatchTempo Tempo { get; init; }

    /// <summary>Gets the side's passing style.</summary>
    public required MatchPassingStyle Passing { get; init; }

    /// <summary>Gets the lanes the side's passes are asked to go through.</summary>
    public required MatchPassFocus Focus { get; init; }

    /// <summary>Gets the carrier's side's players, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Attackers { get; init; }

    /// <summary>Gets the carrier's side's board positions, in the same order.</summary>
    public required ReadOnlySpan<TickAnchorSpec> Specs { get; init; }

    /// <summary>Gets the carrier's side's skills, in the same order.</summary>
    public required ReadOnlySpan<TickPlayerSkills> Skills { get; init; }

    /// <summary>Gets the opposing side's players, as they stand at the start of the tick.</summary>
    public required ReadOnlySpan<TickPlayerState> Defenders { get; init; }

    /// <summary>Gets the index of the man with the ball.</summary>
    public required int CarrierIndex { get; init; }

    /// <summary>
    /// Gets what the off-the-ball AI has told each teammate to do this tick (<see cref="TickOffBallSupport.Assign"/>), in the
    /// same order; empty when the caller has none. The brain reads the runners from it to find the through ball.
    /// </summary>
    public ReadOnlySpan<TickAttackingOrder> Orders { get; init; }

    /// <summary>Gets how many ticks the carrier has already spent shielding the ball, which wears the option out.</summary>
    public int ShieldTicks { get; init; }
}

/// <summary>
/// The ball carrier's decision engine for the tick engine: the Football Manager brain (`tick-engine-v1`, Milestone 5).
/// </summary>
/// <remarks>
/// <para>
/// Every few ticks (<see cref="DecisionInterval"/>: one for a sharp mind, two otherwise) the carrier scores every action open to
/// him and takes the best. A score is the <em>expected value</em> of the action: the chance it comes off times what it is worth
/// if it does, minus the chance it does not times what losing the ball costs here. All of it is integer arithmetic in the side's
/// own point of view (it attacks towards high X and counts Y from its left hand), mirrored for the away side, and there is no
/// random draw in <see cref="Decide"/>: chance enters only when <see cref="Execute"/> disperses the kick.
/// </para>
/// <para><b>Pressure.</b> The nearest defender's distance (full at contact, none beyond 9 m) plus how fast he is closing (up to 30%)
/// plus a second defender within 9 m gives a raw pressure in basis points. His composure then scales it by
/// <c>140% − 4% × Composure</c> (60% at 20, 136% at 1): a calm player feels half the heat of a rattled one.</para>
/// <list type="number">
/// <item><description>
/// <b>Shoot.</b> Within 26.5 m of the goal and not behind its line. A heuristic xG: <c>0.9 × closeness² + 0.1 × closeness</c>
/// (closeness is the share of the range left), times <c>cos²</c> of the angle off the goal's axis, times 55% for every defender
/// within 2.5 m of the line to goal, times the shooter's <c>60% + 4% × (2 × Finishing + Technique) / 3</c>, times 65%, valued at
/// three times that, and scaled by mentality (70% Defensive to 130% Attacking). The aim is the far corner, 3.5 m inside the post.
/// </description></item>
/// <item><description>
/// <b>Pass.</b> Every teammate he can see (<c>22 m + 2.4 m × Vision</c>) is scored. Success is the lane (each defender within
/// 3.5 m of it, and not behind the passer, cuts up to 65% of the chance by how close he stands; a lofted ball over 35 m is cut
/// by a third as much) times the
/// accuracy of the kick (see <see cref="ErrorAngle"/>). Worth: 12 base, progress towards goal (+1.2 per unit gained, between
/// -15 m and +30 m, scaled by mentality and tempo), the receiver's space from the nearest marker (up to 10), the passing style
/// and pass focus (up to ±9), the receiver's job (+25 for a run into the space behind the line, played as a
/// <see cref="TickCarrierAction.ThroughBall"/> to where he will be; +6 for an overlapping full-back) and, for a backward ball,
/// up to +25 by how hard he is pressed (the recycle). The cost of a lost ball is 30 in his own third, 18 in midfield, 9 in the final
/// third, and grows with pressure. A receiver who is offside is not picked by a player who reads the game
/// (Decisions + Anticipation of 12 or more).
/// </description></item>
/// <item><description>
/// <b>Cross.</b> From a wide position (18 m or more off the middle) in the final quarter, the ball goes to the best-placed
/// teammate in the area (fewest defenders within 4.5 m), or to the penalty spot when there is none. Worth 33 base, +7 for each
/// attacker in the area (up to 3), -4.5 for each defender crowding the target (up to 3), +15 on the byline.
/// </description></item>
/// <item><description>
/// <b>Dribble.</b> Five headings (straight at goal and 22° and 45° either side) are raycast for open space (a defender within 3 m
/// of the line blocks it, the touchline and end line end it, 15 m at most); at least 5 m is needed, and the carry must go
/// forward. Worth <c>6 + progress</c>, less 4 per unit turned from where he faces, times a success of <c>55% + 2.5% × (2 ×
/// Dribbling + Pace + Acceleration) / 4</c> that pressure eats into. A goalkeeper never dribbles.
/// </description></item>
/// <item><description>
/// <b>Shield.</b> With no forward option worth 12 and a safe teammate behind him, under moderate pressure (30% or more), a calm
/// strong player holds the ball: <c>10 + 0.8 × (Composure + Strength) − 1 × ticks already held</c>, fading to nothing as
/// the pressure approaches 110%.
/// </description></item>
/// <item><description>
/// <b>Clear.</b> A player whose raw pressure reaches <c>50% + 3% × Composure</c> (so one of 20 composure never does) hoofs the
/// ball upfield to the wing away from the presser, worth 9 (15 in his own third): the last resort, taken only when nothing
/// scores more.
/// </description></item>
/// </list>
/// <para>
/// <see cref="Execute"/> hits the kick. The error is <c>BaseError × max(15%, 100 − (4 × Passing + Technique)) × (1 +
/// pressure)</c> in binary angle units (the plan's formula, floored so that a perfect player is not a machine), the kick leaves
/// rotated by a draw within that angle either way, and one draw is taken for every kick whatever the outcome. A pass or shot is
/// rolled along the ground below 35 m and lofted above it. Everything works on spans and a stack buffer, so a decision allocates
/// nothing.
/// </para>
/// </remarks>
internal static class TickBallCarrierBrain
{
    /// <summary>The farthest from goal a shot is considered, in pitch units (about 28 m).</summary>
    public const int ShootRange = 2_650;

    /// <summary>The share of an xG heuristic that becomes the chance of scoring, in basis points.</summary>
    public const int MaximumGoalChance = 6_500;

    /// <summary>How many times a shot's chance is valued when it is weighed against a pass.</summary>
    public const int ShotValueMultiplier = 3;

    /// <summary>How near a defender may stand to the line to goal before he blocks the shot, in pitch units (2.5 m).</summary>
    public const int ShotBlockReach = 250;

    /// <summary>The share of a shot's chance left by each blocker, in percent.</summary>
    public const int ShotBlockPercent = 55;

    /// <summary>The blockers beyond which no shot is attempted.</summary>
    public const int ShotMaximumBlockers = 3;

    /// <summary>How far inside the far post a shot is aimed, in pitch units.</summary>
    public const int ShotAimOffset = 350;

    /// <summary>How far past the goal line a shot is aimed, in pitch units.</summary>
    public const int ShotOvershoot = 400;

    /// <summary>The speed a shot arrives at the line with, in cm/s.</summary>
    public const int ShotArrivalCentimetresPerSecond = 1_800;

    /// <summary>The speed a pass is meant to arrive at its receiver with, in cm/s.</summary>
    public const int PassArrivalCentimetresPerSecond = 400;

    /// <summary>Within this distance of a defender the carrier is under pressure (9 m), in pitch units.</summary>
    public const int PressureRange = 900;

    /// <summary>The distance at which the pressure from distance is complete (contact), in pitch units.</summary>
    public const int PressureContact = 100;

    /// <summary>The most pressure the nearest defender's distance can give, in basis points.</summary>
    public const int DistancePressure = 7_000;

    /// <summary>The most pressure a defender's closing speed can add, in basis points.</summary>
    public const int ClosingPressure = 3_000;

    /// <summary>The closing speed that adds the whole of <see cref="ClosingPressure"/>, in pitch units per tick (about 5 m/s).</summary>
    public const int FullClosingSpeed = 50;

    /// <summary>The most a second defender within <see cref="PressureRange"/> adds, in basis points.</summary>
    public const int SecondDefenderPressure = 1_500;

    /// <summary>How far a pass lane is cut by a defender standing on it, in basis points.</summary>
    public const int LaneRiskMaximum = 6_500;

    /// <summary>How near a defender must stand to a pass lane to cut it, in pitch units (3.5 m).</summary>
    public const int LaneReach = 350;

    /// <summary>The length from which a pass is lofted rather than rolled, in pitch units (about 35 m).</summary>
    public const int LoftedDistance = 3_500;

    /// <summary>The widest a receiver's catch is, laterally, in pitch units (the ball can be this far off and still be got to).</summary>
    public const int CatchWidth = 250;

    /// <summary>The base error angle of a pass, in binary angle units (about 10°).</summary>
    public const int PassBaseError = 30;

    /// <summary>The base error angle of a shot, in binary angle units (about 13°).</summary>
    public const int ShotBaseError = 36;

    /// <summary>The base error angle of a cross, in binary angle units (about 14°).</summary>
    public const int CrossBaseError = 40;

    /// <summary>The base error angle of a clearance, in binary angle units (about 18°).</summary>
    public const int ClearBaseError = 50;

    /// <summary>The share of the base error a perfect player still makes, in percent.</summary>
    public const int MinimumErrorPercent = 15;

    /// <summary>The attribute points that an error of nothing would need: <c>4 × Passing + Technique</c> against this.</summary>
    public const int ErrorSkillCeiling = 100;

    /// <summary>The farthest a teammate is seen at Vision 0, in pitch units (22 m).</summary>
    public const int VisionBase = 2_100;

    /// <summary>The distance gained per point of Vision, in pitch units (2.4 m).</summary>
    public const int VisionStep = 230;

    /// <summary>The Vision a player needs to see a run behind the line and play the through ball.</summary>
    public const int ThroughBallMinimumVision = 9;

    /// <summary>The ticks ahead of a sprinting runner a through ball is aimed.</summary>
    public const int ThroughLeadTicks = 8;

    /// <summary>The most ticks ahead of a moving receiver an ordinary pass is aimed.</summary>
    public const int MaximumLeadTicks = 6;

    /// <summary>The distance of ball travel that adds one tick of lead, in pitch units.</summary>
    public const int LeadDistance = 500;

    /// <summary>The Decisions plus Anticipation from which a player reads the offside line.</summary>
    public const int OffsideAwareness = 12;

    /// <summary>The Decisions plus Anticipation from which a player decides every tick.</summary>
    public const int QuickDecisionSum = 24;

    /// <summary>The nearest to the touchline a point the brain picks is, in pitch units.</summary>
    public const int Margin = TickTacticalGeometry.Margin;

    /// <summary>The nearest to the carrier's own goal line a lateral ball counts as backward (the least a pass must go back to be a recycle), in pitch units.</summary>
    public const int RecycleMinimumGain = 200;

    /// <summary>The most the recycle is worth over a pass, at full pressure, in utility.</summary>
    public const int RecycleBonusMaximum = 2_500;

    /// <summary>The wide position from which a cross is possible: this far off the middle, in pitch units (18 m).</summary>
    public const int CrossWideThreshold = 1_800;

    /// <summary>The nearest to the goal line a cross is hit from, in pitch units.</summary>
    public const int CrossMinimumX = 7_500;

    /// <summary>The farthest from the middle of the goal a teammate counts as in the area, laterally, in pitch units.</summary>
    public const int AreaHalfWidth = 2_000;

    /// <summary>Where a cross with nobody to find goes, in pitch units: the penalty spot.</summary>
    public const int CrossFallbackX = 8_900;

    /// <summary>How near a defender must be to a cross's target to crowd it, in pitch units.</summary>
    public const int CrossCrowdReach = 450;

    /// <summary>The apex of a lofted pass is this plus the length over <see cref="LoftApexDistance"/> (Z units).</summary>
    public const int LoftApexBase = 6;

    /// <summary>The length that adds one Z unit to the apex of a lofted pass, in pitch units.</summary>
    public const int LoftApexDistance = 110;

    /// <summary>The apex of a cross, in Z units (inside the bar's height so a header can reach it late in its fall).</summary>
    public const int CrossApex = 30;

    /// <summary>The apex of a clearance, in Z units.</summary>
    public const int ClearApex = 55;

    /// <summary>The most a dribble carries the ball, in pitch units (15 m).</summary>
    public const int MaximumCarry = 1_500;

    /// <summary>The least open space ahead for a dribble to be considered, in pitch units (5 m).</summary>
    public const int MinimumCarry = 500;

    /// <summary>How near the line of a dribble a defender must be to block it, in pitch units (3 m).</summary>
    public const int CarryCorridor = 300;

    /// <summary>How far short of a blocking defender a carry ends, in pitch units (3 m).</summary>
    public const int CarryStandOff = 300;

    /// <summary>The least pressure, after composure, at which shielding the ball is considered, in basis points.</summary>
    public const int ShieldMinimumPressure = 3_000;

    /// <summary>The most a best forward option may be worth for the carrier to shield instead.</summary>
    public const int ShieldForwardGate = 1_200;

    /// <summary>The raw pressure, at Composure 0, from which a player panics and clears, in basis points.</summary>
    public const int PanicBase = 5_000;

    /// <summary>The raw pressure a point of Composure adds to the panic threshold, in basis points.</summary>
    public const int PanicStep = 300;

    /// <summary>The worth of a clearance, in utility.</summary>
    public const int ClearUtility = 900;

    /// <summary>The extra worth of a clearance from the player's own third, in utility.</summary>
    public const int ClearOwnThirdBonus = 600;

    private const int BasisPoints = 10_000;

    private const int PassBaseValue = 1_200;
    private const int BackwardLimit = 1_500;
    private const int ForwardLimit = 3_000;
    private const int SpaceValueMaximum = 1_000;
    private const int SpaceReach = 1_500;
    private const int ThroughBonus = 2_500;
    private const int OverlapBonus = 600;
    private const int PocketBonus = 300;
    private const int OwnThirdLoss = 3_000;
    private const int MidfieldLoss = 1_800;
    private const int FinalThirdLoss = 900;
    private const int OwnThird = 3_500;
    private const int MidfieldLimit = 6_500;
    private const int CrossBaseValue = 3_300;
    private const int CrossPerAttacker = 700;
    private const int CrossPerDefender = 450;
    private const int CrossByLineBonus = 1_500;
    private const int ByLineX = 9_000;
    private const int DribbleBaseValue = 600;
    private const int DribbleTurnCostPerUnit = 4;

    /// <summary>The headings a dribble tries relative to the line to goal, in binary angle units: straight, then 22° and 45° each way.</summary>
    private static readonly int[] DribbleOffsets = [0, 64, -64, 128, -128];

    /// <summary>The share of a shot's chance left after N blockers, in basis points.</summary>
    private static readonly int[] BlockerFactors = [10_000, 5_500, 3_025, 1_664, 915];

    /// <summary>Gets how many ticks pass between the carrier's decisions: 1 for a sharp mind, otherwise 2.</summary>
    /// <param name="skills">The carrier's skills.</param>
    /// <param name="tempo">The side's tempo; a high one speeds the mind up and a low one slows it.</param>
    public static int DecisionInterval(in TickPlayerSkills skills, MatchTempo tempo)
    {
        var sharpness = skills.Decisions + skills.Anticipation
            + (tempo == MatchTempo.High ? 4 : 0)
            - (tempo == MatchTempo.Low ? 4 : 0);

        return sharpness >= QuickDecisionSum ? 1 : 2;
    }

    /// <summary>Measures the raw pressure on the carrier from the defenders around him, in basis points (0 to 10,000).</summary>
    /// <param name="carrier">The carrier.</param>
    /// <param name="defenders">The defending side's players; index 0 is the goalkeeper, who does not press.</param>
    public static int MeasurePressure(in TickPlayerState carrier, ReadOnlySpan<TickPlayerState> defenders)
    {
        var nearest = long.MaxValue;
        var second = long.MaxValue;
        var nearestIndex = -1;

        for (var index = 1; index < defenders.Length; index++)
        {
            long dx = defenders[index].X - carrier.X;
            long dy = defenders[index].Y - carrier.Y;
            var squared = (dx * dx) + (dy * dy);

            if (squared < nearest)
            {
                second = nearest;
                nearest = squared;
                nearestIndex = index;
            }
            else if (squared < second)
            {
                second = squared;
            }
        }

        if (nearestIndex < 0)
        {
            return 0;
        }

        var distance = (int)(SpatialMath.Sqrt(nearest) / TickSpatialUnits.FixedScale);
        var pressure = Math.Clamp(
            (PressureRange - distance) * DistancePressure / (PressureRange - PressureContact),
            0,
            DistancePressure);

        if (pressure > 0 || distance < 2 * PressureRange)
        {
            // How fast the gap is closing: the defender's velocity relative to the carrier, along the line between them.
            var relativeX = (long)defenders[nearestIndex].VelocityX - carrier.VelocityX;
            var relativeY = (long)defenders[nearestIndex].VelocityY - carrier.VelocityY;
            var lineX = (long)carrier.X - defenders[nearestIndex].X;
            var lineY = (long)carrier.Y - defenders[nearestIndex].Y;
            var length = SpatialMath.Sqrt((lineX * lineX) + (lineY * lineY));

            if (length > 0)
            {
                var closing = ((relativeX * lineX) + (relativeY * lineY)) / length / TickSpatialUnits.FixedScale;

                pressure += (int)Math.Clamp(closing * ClosingPressure / FullClosingSpeed, 0, ClosingPressure);
            }
        }

        if (second != long.MaxValue)
        {
            var secondDistance = (int)(SpatialMath.Sqrt(second) / TickSpatialUnits.FixedScale);

            pressure += Math.Max(0, (PressureRange - secondDistance) * SecondDefenderPressure / PressureRange);
        }

        return Math.Min(BasisPoints, pressure);
    }

    /// <summary>Scales a raw pressure by the carrier's composure: 140% − 4% × Composure, so 60% at 20 and 136% at 1.</summary>
    /// <param name="pressure">The raw pressure, in basis points.</param>
    /// <param name="composure">The carrier's Composure.</param>
    public static int EffectivePressure(int pressure, int composure) =>
        pressure * (14_000 - (400 * composure)) / BasisPoints;

    /// <summary>
    /// Gets the error angle of a kick, in binary angle units: <c>BaseError × max(15%, 100 − (4 × skill + Technique)) × (1 +
    /// pressure)</c>. The kick leaves anywhere within this angle either side of its aim.
    /// </summary>
    /// <param name="skill">The attribute the kick is made with: Passing, Finishing or Crossing.</param>
    /// <param name="technique">The kicker's Technique.</param>
    /// <param name="baseError">The error of an average player with no pressure on him at the weakest skill.</param>
    /// <param name="effectivePressure">The pressure on him after his composure, in basis points.</param>
    public static int ErrorAngle(int skill, int technique, int baseError, int effectivePressure)
    {
        var percent = Math.Max(MinimumErrorPercent, ErrorSkillCeiling - ((4 * skill) + technique));
        var error = baseError * percent * (BasisPoints + Math.Max(0, effectivePressure)) / (100 * BasisPoints);

        return Math.Max(1, error);
    }

    /// <summary>
    /// Gets the heuristic chance, in basis points, that a shot from a point scores: distance, angle, blockers on the line and the
    /// shooter's finishing. It is 0 beyond <see cref="ShootRange"/> or from behind the goal line.
    /// </summary>
    /// <param name="x">The shooter's X, in the side's own point of view (the goal he attacks is at 10,000).</param>
    /// <param name="y">The shooter's Y, in the side's own point of view.</param>
    /// <param name="blockers">The defenders standing on the line to goal.</param>
    /// <param name="skills">The shooter's skills.</param>
    public static int ExpectedGoals(int x, int y, int blockers, in TickPlayerSkills skills)
    {
        var dx = (long)SpatialPitch.PitchLength - x;
        var dy = (long)SpatialPitch.GoalYCenter - y;
        var distance = (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));

        if (dx <= 0 || distance > ShootRange)
        {
            return 0;
        }

        var closeness = (ShootRange - distance) * BasisPoints / ShootRange;
        var shape = (closeness * closeness / BasisPoints * 9 / 10) + (closeness / 10);
        var cosine = (int)(dx * BasisPoints / Math.Max(1, distance));
        var angle = cosine * cosine / BasisPoints;
        var block = BlockerFactors[Math.Min(blockers, BlockerFactors.Length - 1)];
        var finish = 6_000 + (4_000 * ((2 * skills.Finishing) + skills.Technique) / 3 / 10);

        return (int)((long)shape * angle / BasisPoints * block / BasisPoints * finish / BasisPoints * MaximumGoalChance / BasisPoints);
    }

    /// <summary>Tells whether a receiver was offside at the moment the carrier would play the ball to him.</summary>
    /// <param name="situation">The moment of play.</param>
    /// <param name="receiver">The teammate's index.</param>
    public static bool IsReceiverOffside(in TickCarrierSituation situation, int receiver) =>
        TickDefensiveAI.IsOffside(
            situation.Attackers[receiver].X,
            situation.Attackers[situation.CarrierIndex].X,
            situation.Defenders,
            !situation.IsHome);

    /// <summary>Chooses what the carrier does with the ball this decision.</summary>
    /// <param name="situation">The moment of play.</param>
    /// <returns>The best-scoring action; ties go to the earlier candidate, so the choice is deterministic.</returns>
    public static TickCarrierDecision Decide(in TickCarrierSituation situation)
    {
        var count = situation.Attackers.Length;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(situation.Defenders.Length, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfNegative(situation.CarrierIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(situation.CarrierIndex, count);

        var view = View.Of(situation);
        var best = Best.StartingAt(view.X, view.Y);
        var scan = new PassScan(int.MinValue, false);

        ScorePasses(situation, view, ref best, ref scan);
        ScoreCross(situation, view, ref best);
        ScoreShot(situation, view, ref best);
        ScoreDribble(situation, view, ref best);
        ScoreShield(situation, view, scan, ref best);
        ScoreClearance(situation, view, ref best);

        var target = Mirror(best.X, best.Y, situation.IsHome);

        return new TickCarrierDecision(best.Action, best.Receiver, target, Math.Max(best.Utility, int.MinValue + 1), best.Pace, view.Effective);
    }

    /// <summary>
    /// Hits the ball as decided. A shot, pass, cross or clearance leaves the ball rotated from its aim by a draw within the
    /// kick's error angle (one draw, always); a dribble or a shield is the caller's to steer and takes no draw.
    /// </summary>
    /// <param name="decision">What the carrier decided.</param>
    /// <param name="skills">The carrier's skills.</param>
    /// <param name="ball">The ball, which the carrier has at his feet; it is released and launched.</param>
    /// <param name="random">The play stream.</param>
    /// <returns>True when the ball was kicked.</returns>
    public static bool Execute(in TickCarrierDecision decision, in TickPlayerSkills skills, TickBallPhysics ball, Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(ball);
        ArgumentNullException.ThrowIfNull(random);

        if (decision.Action is TickCarrierAction.Dribble or TickCarrierAction.Shield)
        {
            return false;
        }

        var error = decision.Action switch
        {
            TickCarrierAction.Shoot => ErrorAngle(skills.Finishing, skills.Technique, ShotBaseError, decision.EffectivePressure),
            TickCarrierAction.Cross => ErrorAngle(skills.Crossing, skills.Technique, CrossBaseError, decision.EffectivePressure),
            TickCarrierAction.Clear => ErrorAngle(skills.Passing, skills.Technique, ClearBaseError, decision.EffectivePressure),
            _ => ErrorAngle(skills.Passing, skills.Technique, PassBaseError, decision.EffectivePressure),
        };

        var turn = random.NextRange(-error, error);
        var fromX = ball.UnitX;
        var fromY = ball.UnitY;
        long dx = decision.Target.X - fromX;
        long dy = decision.Target.Y - fromY;
        var cos = TickTrigonometry.Cos(turn);
        var sin = TickTrigonometry.Sin(turn);
        var targetX = fromX + (int)(((dx * cos) - (dy * sin)) / TickTrigonometry.Scale);
        var targetY = fromY + (int)(((dx * sin) + (dy * cos)) / TickTrigonometry.Scale);
        var distance = (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));

        switch (decision.Action)
        {
            case TickCarrierAction.Shoot:
                ball.LaunchRolling(targetX, targetY, TickSpatialUnits.SpeedToFixedPerTick(ShotArrivalCentimetresPerSecond));
                break;

            case TickCarrierAction.Cross:
                ball.LaunchLofted(ClampX(targetX), ClampY(targetY), CrossApex);
                break;

            case TickCarrierAction.Clear:
                ball.LaunchLofted(ClampX(targetX), ClampY(targetY), ClearApex);
                break;

            default:
                if (distance >= LoftedDistance)
                {
                    ball.LaunchLofted(ClampX(targetX), ClampY(targetY), Math.Clamp(LoftApexBase + (distance / LoftApexDistance), 8, 45));
                }
                else
                {
                    ball.LaunchRolling(ClampX(targetX), ClampY(targetY), TickSpatialUnits.SpeedToFixedPerTick(PassArrivalCentimetresPerSecond));
                }

                break;
        }

        return true;
    }

    private static void ScorePasses(in TickCarrierSituation situation, in View view, ref Best best, ref PassScan scan)
    {
        var carrierSkills = situation.Skills[situation.CarrierIndex];
        var vision = VisionBase + (VisionStep * carrierSkills.Vision);
        var aware = carrierSkills.Decisions + carrierSkills.Anticipation >= OffsideAwareness;
        var progressPercent = ProgressPercent(situation.Mentality, situation.Tempo);
        var loss = LossOfBall(view);

        for (var index = 0; index < situation.Attackers.Length; index++)
        {
            if (index == situation.CarrierIndex)
            {
                continue;
            }

            var receiver = situation.Attackers[index];
            var x = MirrorX(TickSpatialUnits.ToUnits(receiver.X), situation.IsHome);
            var y = MirrorY(TickSpatialUnits.ToUnits(receiver.Y), situation.IsHome);
            var sight = Distance(view.X, view.Y, x, y);

            if (sight > vision)
            {
                continue;
            }

            var run = situation.Orders.Length > index ? situation.Orders[index].Role : TickAttackingRole.Holding;
            var through = run == TickAttackingRole.Runner && carrierSkills.Vision >= ThroughBallMinimumVision;

            if (aware && IsReceiverOffside(situation, index))
            {
                continue;
            }

            // Where the ball goes: to where he will be by the time it gets there.
            var lead = through ? ThroughLeadTicks : Math.Min(MaximumLeadTicks, sight / LeadDistance);
            var velocityX = situation.IsHome ? receiver.VelocityX : -receiver.VelocityX;
            var velocityY = situation.IsHome ? receiver.VelocityY : -receiver.VelocityY;
            var aimX = Math.Clamp(x + (velocityX * lead / TickSpatialUnits.FixedScale), Margin, SpatialPitch.PitchLength - Margin);
            var aimY = Math.Clamp(y + (velocityY * lead / TickSpatialUnits.FixedScale), Margin, SpatialPitch.PitchWidth - Margin);
            var length = Distance(view.X, view.Y, aimX, aimY);
            var lofted = length >= LoftedDistance;

            var survive = LaneSurvival(situation, view.X, view.Y, aimX, aimY, lofted);
            var accuracy = Accuracy(carrierSkills.Passing, carrierSkills.Technique, PassBaseError, length, lofted, view.Effective);
            var success = survive * accuracy / BasisPoints;

            var gain = aimX - view.X;
            var value = PassBaseValue
                + (Math.Clamp(gain, -BackwardLimit, ForwardLimit) * progressPercent / 100)
                + SpaceValue(situation, aimX, aimY)
                + StyleValue(situation.Passing, gain, length)
                + FocusValue(situation.Focus, aimY)
                + (through ? ThroughBonus : 0)
                + (run == TickAttackingRole.Overlap ? OverlapBonus : 0)
                + (run == TickAttackingRole.Pocket ? PocketBonus : 0);

            var backward = gain < -RecycleMinimumGain;

            if (backward)
            {
                value += view.Effective * RecycleBonusMaximum / BasisPoints;
            }

            var utility = Expected(success, value, loss);
            var action = through ? TickCarrierAction.ThroughBall : (backward ? TickCarrierAction.Recycle : TickCarrierAction.Pass);

            if (gain >= 0)
            {
                scan = scan with { BestForward = Math.Max(scan.BestForward, utility) };
            }
            else if (backward && success >= 7_000)
            {
                scan = scan with { SafeBehind = true };
            }

            best.Consider(action, index, aimX, aimY, utility, 0);
        }
    }

    private static void ScoreCross(in TickCarrierSituation situation, in View view, ref Best best)
    {
        if (situation.Specs[situation.CarrierIndex].Family == MatchPositionFamily.Goalkeeper
            || Math.Abs(view.Y - SpatialPitch.GoalYCenter) < CrossWideThreshold
            || view.X < CrossMinimumX)
        {
            return;
        }

        var carrierSkills = situation.Skills[situation.CarrierIndex];
        var aware = carrierSkills.Decisions + carrierSkills.Anticipation >= OffsideAwareness;
        var areaX = SpatialPitch.PitchLength - SpatialPitch.PenaltyBoxWidth;
        var receiver = -1;
        var targetX = CrossFallbackX;
        var targetY = SpatialPitch.GoalYCenter;
        var receiverCrowd = int.MaxValue;
        var inArea = 0;

        for (var index = 0; index < situation.Attackers.Length; index++)
        {
            if (index == situation.CarrierIndex)
            {
                continue;
            }

            var x = MirrorX(TickSpatialUnits.ToUnits(situation.Attackers[index].X), situation.IsHome);
            var y = MirrorY(TickSpatialUnits.ToUnits(situation.Attackers[index].Y), situation.IsHome);

            if (x < areaX || Math.Abs(y - SpatialPitch.GoalYCenter) > AreaHalfWidth || (aware && IsReceiverOffside(situation, index)))
            {
                continue;
            }

            inArea++;

            var crowd = CrowdAround(situation, x, y);

            if (crowd < receiverCrowd)
            {
                receiverCrowd = crowd;
                receiver = index;
                targetX = x;
                targetY = y;
            }
        }

        var crowding = receiver < 0 ? CrowdAround(situation, targetX, targetY) : receiverCrowd;
        var length = Distance(view.X, view.Y, targetX, targetY);
        var survive = BasisPoints - (Math.Min(3, crowding) * 500);
        var accuracy = Accuracy(carrierSkills.Crossing, carrierSkills.Technique, CrossBaseError, length, lofted: true, view.Effective);
        var success = survive * accuracy / BasisPoints;
        var value = CrossBaseValue
            + (CrossPerAttacker * Math.Min(3, inArea))
            - (CrossPerDefender * Math.Min(3, crowding))
            + (view.X >= ByLineX ? CrossByLineBonus : 0);

        best.Consider(TickCarrierAction.Cross, receiver, targetX, targetY, Expected(success, value, LossOfBall(view)), 0);
    }

    private static void ScoreShot(in TickCarrierSituation situation, in View view, ref Best best)
    {
        if (situation.Specs[situation.CarrierIndex].Family == MatchPositionFamily.Goalkeeper)
        {
            return;
        }

        var skills = situation.Skills[situation.CarrierIndex];
        var blockers = 0;
        var limit = (long)ShotBlockReach * ShotBlockReach;

        for (var index = 1; index < situation.Defenders.Length; index++)
        {
            var x = MirrorX(TickSpatialUnits.ToUnits(situation.Defenders[index].X), situation.IsHome);
            var y = MirrorY(TickSpatialUnits.ToUnits(situation.Defenders[index].Y), situation.IsHome);

            if (x > view.X && TickOffBallSupport.SegmentDistanceSquared(x, y, view.X, view.Y, SpatialPitch.PitchLength, SpatialPitch.GoalYCenter) < limit)
            {
                blockers++;
            }
        }

        if (blockers > ShotMaximumBlockers)
        {
            return;
        }

        var chance = ExpectedGoals(view.X, view.Y, blockers, skills);

        if (chance <= 0)
        {
            return;
        }

        var utility = chance * ShotValueMultiplier * ShotEagerness(situation.Mentality) / 100;
        var aimY = view.Y <= SpatialPitch.GoalYCenter
            ? SpatialPitch.GoalYCenter + ShotAimOffset
            : SpatialPitch.GoalYCenter - ShotAimOffset;

        best.Consider(TickCarrierAction.Shoot, -1, SpatialPitch.PitchLength + ShotOvershoot, aimY, utility, 0);
    }

    private static void ScoreDribble(in TickCarrierSituation situation, in View view, ref Best best)
    {
        if (situation.Specs[situation.CarrierIndex].Family == MatchPositionFamily.Goalkeeper)
        {
            return;
        }

        var skills = situation.Skills[situation.CarrierIndex];
        var mean = ((2 * skills.Dribbling) + skills.Pace + skills.Acceleration) / 4;
        var success = Math.Clamp(5_500 + (250 * mean), 2_000, 9_800) * (BasisPoints - (view.Effective * 45 / 100)) / BasisPoints;
        var progressPercent = ProgressPercent(situation.Mentality, situation.Tempo);
        var loss = LossOfBall(view);
        var toGoal = TickTrigonometry.AngleOf(SpatialPitch.PitchLength - view.X, SpatialPitch.GoalYCenter - view.Y);

        for (var attempt = 0; attempt < DribbleOffsets.Length; attempt++)
        {
            var heading = TickTrigonometry.Normalize(toGoal + DribbleOffsets[attempt]);
            var cos = TickTrigonometry.Cos(heading);

            if (cos <= 0)
            {
                continue;
            }

            var space = OpenSpace(situation, view, heading);

            if (space < MinimumCarry)
            {
                continue;
            }

            var progress = space * cos / TickTrigonometry.Scale;
            var turn = Math.Abs(TickTrigonometry.Difference(view.Heading, heading));
            var value = DribbleBaseValue + (progress * progressPercent / 100) - (turn * DribbleTurnCostPerUnit);

            var targetX = view.X + (space * cos / TickTrigonometry.Scale);
            var targetY = view.Y + (space * TickTrigonometry.Sin(heading) / TickTrigonometry.Scale);

            best.Consider(
                TickCarrierAction.Dribble,
                -1,
                targetX,
                targetY,
                Expected(success, value, loss),
                DribblePace(situation.Tempo, view.Effective));
        }
    }

    private static void ScoreShield(in TickCarrierSituation situation, in View view, in PassScan scan, ref Best best)
    {
        var skills = situation.Skills[situation.CarrierIndex];

        if (view.Effective < ShieldMinimumPressure || scan.BestForward > ShieldForwardGate || !scan.SafeBehind || view.Nearest < 0)
        {
            return;
        }

        var worth = 1_000 + (80 * (skills.Composure + skills.Strength)) - (100 * situation.ShieldTicks);
        var utility = worth * Math.Max(0, 11_000 - view.Effective) / BasisPoints;

        if (utility <= 0)
        {
            return;
        }

        // Back into the defender, a half metre away from him.
        var awayX = view.X - view.NearestX;
        var awayY = view.Y - view.NearestY;
        var length = (int)Math.Max(1, SpatialMath.Sqrt(((long)awayX * awayX) + ((long)awayY * awayY)));
        var targetX = Math.Clamp(view.X + (awayX * 50 / length), Margin, SpatialPitch.PitchLength - Margin);
        var targetY = Math.Clamp(view.Y + (awayY * 50 / length), Margin, SpatialPitch.PitchWidth - Margin);

        best.Consider(TickCarrierAction.Shield, -1, targetX, targetY, utility, 3_000);
    }

    private static void ScoreClearance(in TickCarrierSituation situation, in View view, ref Best best)
    {
        var skills = situation.Skills[situation.CarrierIndex];

        if (view.Pressure < PanicBase + (PanicStep * skills.Composure))
        {
            return;
        }

        // Hoofed upfield to the wing away from the man closing him down.
        var targetX = Math.Min(SpatialPitch.PitchLength - Margin, view.X + 3_000);
        var targetY = view.Nearest >= 0 && view.NearestY > view.Y ? 900 : SpatialPitch.PitchWidth - 900;
        var utility = ClearUtility + (view.X < OwnThird ? ClearOwnThirdBonus : 0);

        best.Consider(TickCarrierAction.Clear, -1, targetX, targetY, utility, 0);
    }

    /// <summary>Gets what an action is worth: the chance it comes off times its worth, less the chance it fails times what a lost ball costs.</summary>
    private static int Expected(int success, int value, int loss) =>
        (int)(((long)success * value / BasisPoints) - ((long)(BasisPoints - success) * loss / BasisPoints));

    /// <summary>Gets what losing the ball costs here, rising with how hard he is pressed.</summary>
    private static int LossOfBall(in View view)
    {
        var zone = view.X < OwnThird ? OwnThirdLoss : (view.X < MidfieldLimit ? MidfieldLoss : FinalThirdLoss);

        return zone * (BasisPoints + (view.Effective * 3 / 2)) / BasisPoints;
    }

    /// <summary>Gets how much going forward is worth, in percent per unit gained: the mentality's and the tempo's.</summary>
    private static int ProgressPercent(MatchMentality mentality, MatchTempo tempo)
    {
        var percent = mentality switch
        {
            MatchMentality.Defensive => 90,
            MatchMentality.Cautious => 105,
            MatchMentality.Positive => 135,
            MatchMentality.Attacking => 150,
            _ => 120,
        };

        return percent + (tempo == MatchTempo.High ? 15 : 0) - (tempo == MatchTempo.Low ? 15 : 0);
    }

    private static int ShotEagerness(MatchMentality mentality) => mentality switch
    {
        MatchMentality.Defensive => 70,
        MatchMentality.Cautious => 85,
        MatchMentality.Positive => 115,
        MatchMentality.Attacking => 130,
        _ => 100,
    };

    private static int DribblePace(MatchTempo tempo, int effective)
    {
        var pace = 8_000 + (tempo == MatchTempo.High ? 600 : 0) - (tempo == MatchTempo.Low ? 600 : 0) + (effective < 2_000 ? 1_000 : 0);

        return Math.Min(BasisPoints, pace);
    }

    /// <summary>Gets the style bonus for a ball of a length and a gain: short passing likes short balls, direct passing long forward ones.</summary>
    private static int StyleValue(MatchPassingStyle style, int gain, int length) => style switch
    {
        MatchPassingStyle.ShortPassing => length <= 2_200 ? 600 : (length >= LoftedDistance ? -800 : 0),
        MatchPassingStyle.DirectPassing => gain >= 1_500 && length >= 2_500 ? 900 : (length <= 1_200 ? -500 : 0),
        _ => 0,
    };

    /// <summary>Gets the pass-focus bonus for a ball aimed at a lane, in the side's own point of view (Y counts from its left).</summary>
    private static int FocusValue(MatchPassFocus focus, int y)
    {
        var offset = y - SpatialPitch.GoalYCenter;
        var central = Math.Abs(offset) < 1_200;
        var left = offset <= -1_200;

        return focus switch
        {
            MatchPassFocus.Centre => central ? 500 : -300,
            MatchPassFocus.CentreAndLeft => central ? 300 : (left ? 300 : -300),
            MatchPassFocus.CentreAndRight => central ? 300 : (left ? -300 : 300),
            MatchPassFocus.Wings => central ? -300 : 500,
            _ => 0,
        };
    }

    /// <summary>Gets what the receiver's room is worth: up to 1,000 for being 17 m or more from the nearest marker.</summary>
    private static int SpaceValue(in TickCarrierSituation situation, int x, int y)
    {
        var nearest = long.MaxValue;

        for (var index = 1; index < situation.Defenders.Length; index++)
        {
            var dx = (long)MirrorX(TickSpatialUnits.ToUnits(situation.Defenders[index].X), situation.IsHome) - x;
            var dy = (long)MirrorY(TickSpatialUnits.ToUnits(situation.Defenders[index].Y), situation.IsHome) - y;

            nearest = Math.Min(nearest, (dx * dx) + (dy * dy));
        }

        var distance = nearest == long.MaxValue ? SpaceReach + 200 : (int)SpatialMath.Sqrt(nearest);

        return Math.Clamp(distance - 200, 0, SpaceReach) * SpaceValueMaximum / SpaceReach;
    }

    /// <summary>Gets the chance, in basis points, that a ball along a lane gets through every defender near it.</summary>
    private static int LaneSurvival(in TickCarrierSituation situation, int fromX, int fromY, int toX, int toY, bool lofted)
    {
        var survive = BasisPoints;

        for (var index = 1; index < situation.Defenders.Length; index++)
        {
            var x = MirrorX(TickSpatialUnits.ToUnits(situation.Defenders[index].X), situation.IsHome);
            var y = MirrorY(TickSpatialUnits.ToUnits(situation.Defenders[index].Y), situation.IsHome);

            // A defender behind the passer, relative to the way the ball goes, cannot get to it: his threat is the pressure.
            if ((((long)x - fromX) * (toX - fromX)) + (((long)y - fromY) * (toY - fromY)) <= 0)
            {
                continue;
            }

            var gap = (int)SpatialMath.Sqrt(TickOffBallSupport.SegmentDistanceSquared(x, y, fromX, fromY, toX, toY));

            if (gap < LaneReach)
            {
                survive = survive * (BasisPoints - ((LaneReach - gap) * LaneRiskMaximum / LaneReach)) / BasisPoints;
            }
        }

        // A ball over the top is out of most defenders' reach for most of its flight.
        return lofted ? BasisPoints - ((BasisPoints - survive) / 3) : survive;
    }

    /// <summary>
    /// Gets the chance, in basis points, that a kick with a given error lands where its receiver can get to it: the share of
    /// the lateral spread of the error (a long ball spreads more) that falls inside the catch.
    /// </summary>
    private static int Accuracy(int skill, int technique, int baseError, int length, bool lofted, int effective)
    {
        var error = ErrorAngle(skill, technique, baseError, effective);

        if (lofted)
        {
            error = error * 3 / 2;
        }

        var spread = (long)length * TickTrigonometry.Sin(error) / TickTrigonometry.Scale;

        return spread <= CatchWidth ? BasisPoints : (int)Math.Max(1_500, (long)CatchWidth * BasisPoints / spread);
    }

    private static int CrowdAround(in TickCarrierSituation situation, int x, int y)
    {
        var crowd = 0;
        var limit = (long)CrossCrowdReach * CrossCrowdReach;

        for (var index = 1; index < situation.Defenders.Length; index++)
        {
            var dx = (long)MirrorX(TickSpatialUnits.ToUnits(situation.Defenders[index].X), situation.IsHome) - x;
            var dy = (long)MirrorY(TickSpatialUnits.ToUnits(situation.Defenders[index].Y), situation.IsHome) - y;

            if ((dx * dx) + (dy * dy) < limit)
            {
                crowd++;
            }
        }

        return crowd;
    }

    /// <summary>Casts a ray from the carrier on a heading and gives the open space along it, in pitch units, up to a carry.</summary>
    private static int OpenSpace(in TickCarrierSituation situation, in View view, int heading)
    {
        var cos = TickTrigonometry.Cos(heading);
        var sin = TickTrigonometry.Sin(heading);
        var space = MaximumCarry;

        if (cos > 0)
        {
            space = Math.Min(space, (SpatialPitch.PitchLength - Margin - view.X) * TickTrigonometry.Scale / cos);
        }

        if (sin > 0)
        {
            space = Math.Min(space, (SpatialPitch.PitchWidth - Margin - view.Y) * TickTrigonometry.Scale / sin);
        }
        else if (sin < 0)
        {
            space = Math.Min(space, (view.Y - Margin) * TickTrigonometry.Scale / -sin);
        }

        for (var index = 1; index < situation.Defenders.Length; index++)
        {
            long dx = MirrorX(TickSpatialUnits.ToUnits(situation.Defenders[index].X), situation.IsHome) - view.X;
            long dy = MirrorY(TickSpatialUnits.ToUnits(situation.Defenders[index].Y), situation.IsHome) - view.Y;
            var ahead = ((dx * cos) + (dy * sin)) / TickTrigonometry.Scale;
            var across = Math.Abs(((dy * cos) - (dx * sin)) / TickTrigonometry.Scale);

            if (ahead > 0 && across < CarryCorridor)
            {
                space = Math.Min(space, (int)ahead - CarryStandOff);
            }
        }

        return Math.Max(0, space);
    }

    private static int Distance(int fromX, int fromY, int toX, int toY)
    {
        long dx = toX - fromX;
        long dy = toY - fromY;

        return (int)SpatialMath.Sqrt((dx * dx) + (dy * dy));
    }

    private static SpatialPoint Mirror(int x, int y, bool isHome) =>
        new(MirrorX(x, isHome), MirrorY(y, isHome));

    /// <summary>Converts an X between the pitch's and the side's own point of view (the conversion is its own inverse).</summary>
    private static int MirrorX(int units, bool isHome) => isHome ? units : SpatialPitch.PitchLength - units;

    private static int MirrorY(int units, bool isHome) => isHome ? units : SpatialPitch.PitchWidth - units;

    private static int ClampX(int x) => Math.Clamp(x, 0, SpatialPitch.PitchLength);

    private static int ClampY(int y) => Math.Clamp(y, 0, SpatialPitch.PitchWidth);

    /// <summary>What the passes showed: the best forward one, and whether any safe ball back exists.</summary>
    private readonly record struct PassScan(int BestForward, bool SafeBehind);

    /// <summary>The best-scoring action so far.</summary>
    private struct Best
    {
        public TickCarrierAction Action;
        public int Receiver;
        public int X;
        public int Y;
        public int Utility;
        public int Pace;

        public static Best StartingAt(int x, int y) =>
            new() { Action = TickCarrierAction.Shield, Receiver = -1, X = x, Y = y, Utility = int.MinValue, Pace = 0 };

        public void Consider(TickCarrierAction action, int receiver, int x, int y, int utility, int pace)
        {
            if (utility > Utility)
            {
                Action = action;
                Receiver = receiver;
                X = x;
                Y = y;
                Utility = utility;
                Pace = pace;
            }
        }
    }

    /// <summary>What every scoring step shares about the carrier, in the side's own point of view.</summary>
    private readonly record struct View(
        int X,
        int Y,
        int Heading,
        int Pressure,
        int Effective,
        int Nearest,
        int NearestX,
        int NearestY)
    {
        public static View Of(in TickCarrierSituation situation)
        {
            var carrier = situation.Attackers[situation.CarrierIndex];
            var pressure = MeasurePressure(carrier, situation.Defenders);
            var nearest = -1;
            var nearestSquared = long.MaxValue;

            for (var index = 1; index < situation.Defenders.Length; index++)
            {
                long dx = situation.Defenders[index].X - carrier.X;
                long dy = situation.Defenders[index].Y - carrier.Y;
                var squared = (dx * dx) + (dy * dy);

                if (squared < nearestSquared)
                {
                    nearestSquared = squared;
                    nearest = index;
                }
            }

            return new View(
                MirrorX(TickSpatialUnits.ToUnits(carrier.X), situation.IsHome),
                MirrorY(TickSpatialUnits.ToUnits(carrier.Y), situation.IsHome),
                situation.IsHome ? carrier.Heading : TickTrigonometry.Normalize(carrier.Heading + TickTrigonometry.HalfTurn),
                pressure,
                EffectivePressure(pressure, situation.Skills[situation.CarrierIndex].Composure),
                nearest,
                nearest < 0 ? 0 : MirrorX(TickSpatialUnits.ToUnits(situation.Defenders[nearest].X), situation.IsHome),
                nearest < 0 ? 0 : MirrorY(TickSpatialUnits.ToUnits(situation.Defenders[nearest].Y), situation.IsHome));
        }
    }
}
