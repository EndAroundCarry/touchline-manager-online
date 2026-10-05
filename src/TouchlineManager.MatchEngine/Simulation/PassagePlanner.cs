using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// The raw geometry draws a possession's strike at goal is placed from (`engine-v5`).
/// </summary>
/// <remarks>
/// Every draw is on the basis-point scale, 0…9,999. They are taken when the possession is planned, whether or
/// not a shot ever happens, so the geometry stream's shape never depends on how the possession turns out; the
/// outcome decides which targets are read and the draws decide where they are.
/// </remarks>
/// <param name="Lateral">Where across the goal mouth, or beside it, the strike goes.</param>
/// <param name="Height">How high the strike arrives.</param>
/// <param name="Depth">How far in front of the line the goalkeeper gets to it.</param>
/// <param name="MissVariant">Whether an off-target strike goes over the bar or wide.</param>
/// <param name="WoodworkVariant">Whether a woodwork hit is a post or the bar; and whether a missed penalty is stopped.</param>
/// <param name="Side">Which post or which side of the goal the strike is nearest.</param>
/// <param name="Rebound">How far out a woodwork rebound travels.</param>
/// <param name="Block">How far in front of the shooter a block is made.</param>
/// <param name="Jitter">The small lateral spread of a rebound or a block.</param>
internal readonly record struct StrikeDraws(
    int Lateral,
    int Height,
    int Depth,
    int MissVariant,
    int WoodworkVariant,
    int Side,
    int Rebound,
    int Block,
    int Jitter);

/// <summary>
/// Where one strike at goal finishes, for every outcome it could have (`engine-v5`).
/// </summary>
/// <remarks>
/// The shot's outcome is decided by the play draws, after the possession is planned, so every target is
/// computed up front and the outcome picks one. A target is where the ball <em>arrives</em>: the recorded
/// strike is a waypoint from the origin to it.
/// </remarks>
/// <param name="Origin">Where the strike is taken from.</param>
/// <param name="GoalTarget">Inside the goal mouth.</param>
/// <param name="GoalAltitude">The altitude it arrives at, under the bar.</param>
/// <param name="SaveTarget">Where the goalkeeper gets to it, near the line and between the posts.</param>
/// <param name="SaveAltitude">The altitude it arrives at, under the bar.</param>
/// <param name="MissTarget">Out of play, wide of a post or over the bar.</param>
/// <param name="MissAltitude">The altitude it arrives at.</param>
/// <param name="WoodworkTarget">The post or the crossbar.</param>
/// <param name="WoodworkAltitude">The altitude of the post or bar it hits.</param>
/// <param name="ReboundPoint">Where the ball comes back to after the woodwork, in front of goal.</param>
/// <param name="BlockPoint">A few metres in front of the shooter, where a block stops it.</param>
/// <param name="PenaltySaved">Whether a missed penalty is shown being stopped rather than put wide.</param>
internal sealed record StrikePlan(
    SpatialPoint Origin,
    SpatialPoint GoalTarget,
    int GoalAltitude,
    SpatialPoint SaveTarget,
    int SaveAltitude,
    SpatialPoint MissTarget,
    int MissAltitude,
    SpatialPoint WoodworkTarget,
    int WoodworkAltitude,
    SpatialPoint ReboundPoint,
    SpatialPoint BlockPoint,
    bool PenaltySaved);

/// <summary>
/// One possession's planned geometry: where the ball starts, where the pressure point is, and where each
/// outcome would leave it (`engine-v4`, completed in `engine-v5`).
/// </summary>
/// <remarks>
/// The plan is drawn from the possession's own derived stream, never the play stream, so the ball's path is a
/// fact of the seed without being a fact that can move the scoreline. The outcome decides which of the
/// anchors below is used; the path between them is the same whichever one it is.
/// </remarks>
/// <param name="Approach">The ball's path from the start to the pressure point, in order.</param>
/// <param name="ApproachEndsInCross">Whether the last approach touch is a cross rather than a ground pass.</param>
/// <param name="PressurePoint">Where the defending side engages and a foul would be committed.</param>
/// <param name="TurnoverPoint">Where a plain turnover would leave the ball; its lateral spread steers a clearance.</param>
/// <param name="OffsidePoint">Where an offside would be given; its lateral spread steers the offside pass.</param>
/// <param name="CornerPoint">The corner flag a corner is taken from.</param>
/// <param name="CornerOutPoint">Where the ball leaves play on the goal line for the corner.</param>
/// <param name="HeaderPoint">Where a corner or a free kick is delivered into the box.</param>
/// <param name="EntryPoint">Where an open-play attack enters the final third.</param>
/// <param name="ShotPoint">Where an open-play shot is taken from.</param>
/// <param name="PenaltySpot">The penalty spot.</param>
/// <param name="BoxFoulPoint">Where a foul that gives a penalty is committed, inside the box.</param>
/// <param name="Zone">The shot zone the possession's chance, if it has one, is taken from.</param>
/// <param name="ScrambleCutBasisPoints">How far along the approach a lost scramble is cut.</param>
/// <param name="ProgressionCutBasisPoints">How far along the approach a failed progression is cut.</param>
/// <param name="Strike">The raw draws a strike's targets are placed from.</param>
/// <param name="IsHome">Whether the possession side attacks towards the high end of the pitch.</param>
internal sealed record PlannedPassage(
    IReadOnlyList<SpatialPoint> Approach,
    bool ApproachEndsInCross,
    SpatialPoint PressurePoint,
    SpatialPoint TurnoverPoint,
    SpatialPoint OffsidePoint,
    SpatialPoint CornerPoint,
    SpatialPoint CornerOutPoint,
    SpatialPoint HeaderPoint,
    SpatialPoint EntryPoint,
    SpatialPoint ShotPoint,
    SpatialPoint PenaltySpot,
    SpatialPoint BoxFoulPoint,
    ShotZone Zone,
    int ScrambleCutBasisPoints,
    int ProgressionCutBasisPoints,
    StrikeDraws Strike,
    bool IsHome);

/// <summary>
/// Builds the ball path a possession is played along (`engine-v4`).
/// </summary>
/// <remarks>
/// <para>
/// The old model gave every possession an absolute random point in the possessing side's own half, so the
/// "ball" never related to the play. This planner replaces it with a real passage: the ball begins where the
/// last one left it — or at a restart — progresses forward through three to eight touches with lateral drift,
/// and reaches the final third by the time a chance is taken. Everything here is drawn from a
/// per-possession stream derived from the match seed and the possession ordinal, so it is reproducible and
/// yet cannot shift a single play draw.
/// </para>
/// <para>
/// The pressure point is the one spatial fact the outcome formulas read: a foul happens where the defender
/// engages, and its distance up the pitch is what decides whether the resulting free kick is in shooting
/// range. That band is a rule constant so the distribution is tunable, not a side effect of where the ball
/// happened to be.
/// </para>
/// <para>
/// In `engine-v5` the plan also places everything the film needs at the end of a possession: the entry into
/// the final third, the box, the corner, and where a strike finishes. New draws are always appended after the
/// existing ones, so a draw's meaning never changes under it.
/// </para>
/// </remarks>
internal static class PassagePlanner
{
    /// <summary>A stride that keeps each possession's geometry stream distinct from the seed and the others'.</summary>
    private const ulong StreamStride = 1_000_003UL;

    /// <summary>The goal posts' inset: a strike that goes in is aimed this far inside them.</summary>
    private const int PostInset = 100;

    /// <summary>
    /// How far inside a post the goalkeeper's save is, at nearest: he stands in front of the goal, not on a post.
    /// </summary>
    private const int KeeperReach = 200;

    /// <summary>How far the ball has travelled, at least, by the time the goalkeeper gets to it.</summary>
    private const int SaveLead = 100;

    /// <summary>How far past a post, either way, a strike that goes over the bar may be.</summary>
    private const int OverBarMargin = 200;

    /// <summary>How far a rebound's lateral spread may carry it either way, across the pitch.</summary>
    private const int ReboundSpread = 700;

    /// <summary>How far a block's lateral spread may carry it either way, across the pitch.</summary>
    private const int BlockSpread = 150;

    /// <summary>How near the goal line a block is ever made.</summary>
    private const int BlockLineMargin = 50;

    /// <summary>How near a touchline a clearance or an offside pass is ever taken.</summary>
    private const int TouchlineMargin = 600;

    /// <summary>How far a through ball to an offside player goes beyond the ball, at least.</summary>
    private const int OffsideLead = 400;

    /// <summary>Creates the per-possession geometry stream for the possession about to be played.</summary>
    /// <param name="state">The match state.</param>
    /// <returns>The stream, seeded from the match seed and the possession ordinal.</returns>
    public static Pcg32 CreateStream(MatchState state) =>
        new(unchecked((state.Input.Seed * StreamStride) + (ulong)state.PossessionOrdinal));

    /// <summary>Gets where a side restarts from its own goal area, after a save or a goal kick.</summary>
    /// <param name="defendingSide">The side whose goal area it is.</param>
    /// <param name="rules">The rules in force.</param>
    public static SpatialPoint GoalAreaSpot(MatchSide defendingSide, EngineRulesV2 rules) =>
        FromAttack(rules.GoalAreaXBasisPoints, SpatialPitch.PitchWidth / 2, defendingSide == MatchSide.Home);

    /// <summary>Plans one possession's geometry.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="side">The side in possession.</param>
    /// <param name="derived">The possession's geometry stream.</param>
    /// <param name="restart">The dead ball the possession is played from, or null when it begins from play.</param>
    /// <returns>The plan, with every point already in pitch coordinates.</returns>
    public static PlannedPassage Plan(MatchState state, MatchSide side, Pcg32 derived, PendingRestart? restart)
    {
        var rules = state.Rules;
        var isHome = side == MatchSide.Home;

        var start = restart?.Spot ?? state.Ball.GroundPoint.Clamp();
        var startAttackX = AttackingX(start.X, isHome);
        var startAttackY = AttackingY(start.Y, isHome);

        // The pressure point is where the defender engages. Its band spans the middle and attacking thirds so
        // a foul can genuinely be committed in free-kick range; it never falls behind where the ball already is.
        var pressureAttackX = Math.Clamp(
            derived.NextRange(rules.PressurePointXMinBasisPoints, rules.PressurePointXMaxBasisPoints),
            startAttackX,
            SpatialPitch.PitchLength);
        var pressureAttackY = FocusLateral(
            derived.NextRange(0, SpatialPitch.PitchWidth),
            state.SideOf(side).Instructions.PassFocus,
            rules);
        var pressure = FromAttack(pressureAttackX, pressureAttackY, isHome);

        var (approach, approachEndsInCross) = Approach(
            derived,
            rules,
            start,
            startAttackX,
            startAttackY,
            pressureAttackX,
            pressureAttackY,
            isHome,
            state.SideOf(side).Instructions.PassFocus);

        var zone = ChooseZone(derived, rules, state.SideOf(side).Instructions.PassFocus);
        var shotAttackX = derived.NextRange(rules.ShotFinalThirdXMinBasisPoints, rules.ShotFinalThirdXMaxBasisPoints);
        var shotAttackY = ZoneY(zone, derived, rules);
        var shotPoint = FromAttack(shotAttackX, shotAttackY, isHome);

        var cornerY = derived.NextInt(2) == 0 ? 0 : SpatialPitch.PitchWidth;

        var turnover = FromAttack(rules.TurnoverMiddleThirdXBasisPoints, derived.NextRange(0, SpatialPitch.PitchWidth), isHome);
        var offside = FromAttack(rules.OffsideLineXBasisPoints, derived.NextRange(0, SpatialPitch.PitchWidth), isHome);

        // Everything from here on is new in engine-v5, and so is drawn after everything above.
        var entryFraction = derived.NextRange(rules.EntryFractionMinBasisPoints, rules.EntryFractionMaxBasisPoints);
        var entry = FromAttack(
            Math.Max(Lerp(pressureAttackX, shotAttackX, entryFraction), rules.FinalThirdEntryXBasisPoints),
            Lerp(pressureAttackY, shotAttackY, entryFraction),
            isHome);

        var boxFoul = FromAttack(
            derived.NextRange(rules.BoxXMinBasisPoints, rules.BoxXMaxBasisPoints),
            derived.NextRange(rules.BoxYMinBasisPoints, rules.BoxYMaxBasisPoints),
            isHome);

        var delivery = FromAttack(
            derived.NextRange(rules.BoxXMinBasisPoints, rules.BoxXMaxBasisPoints),
            derived.NextRange(rules.BoxYMinBasisPoints, rules.BoxYMaxBasisPoints),
            isHome);

        // The corner goes out over the goal line some way from the flag, on the flag's side of the pitch.
        var cornerOffset = derived.NextRange(rules.CornerOutOfPlayMinBasisPoints, rules.CornerOutOfPlayMaxBasisPoints);
        var cornerOut = FromAttack(
            SpatialPitch.PitchLength,
            cornerY == 0 ? cornerOffset : SpatialPitch.PitchWidth - cornerOffset,
            isHome);

        var scrambleCut = derived.NextRange(rules.ScrambleCutMinBasisPoints, rules.ScrambleCutMaxBasisPoints);
        var progressionCut = derived.NextRange(rules.ProgressionCutMinBasisPoints, rules.ProgressionCutMaxBasisPoints);

        var strike = new StrikeDraws(
            derived.NextBasisPoints(),
            derived.NextBasisPoints(),
            derived.NextBasisPoints(),
            derived.NextBasisPoints(),
            derived.NextBasisPoints(),
            derived.NextBasisPoints(),
            derived.NextBasisPoints(),
            derived.NextBasisPoints(),
            derived.NextBasisPoints());

        return new PlannedPassage(
            Approach: approach,
            ApproachEndsInCross: approachEndsInCross,
            PressurePoint: pressure,
            TurnoverPoint: turnover,
            OffsidePoint: offside,
            CornerPoint: FromAttack(SpatialPitch.PitchLength, cornerY, isHome),
            CornerOutPoint: cornerOut,
            HeaderPoint: delivery,
            EntryPoint: entry,
            ShotPoint: shotPoint,
            PenaltySpot: FromAttack(SpatialPitch.PenaltySpotAwayX, SpatialPitch.PenaltySpotY, isHome),
            BoxFoulPoint: boxFoul,
            Zone: zone,
            ScrambleCutBasisPoints: scrambleCut,
            ProgressionCutBasisPoints: progressionCut,
            Strike: strike,
            IsHome: isHome);
    }

    /// <summary>
    /// Cuts the approach off part of the way along: the ball is lost before it reaches the pressure point.
    /// </summary>
    /// <remarks>
    /// The cut is measured in segments rather than distance, so it is integer arithmetic throughout; the
    /// approach's touches are spaced evenly enough that the difference is not visible. The result always begins
    /// at the approach's start and ends where the ball was lost.
    /// </remarks>
    /// <param name="plan">The possession's plan.</param>
    /// <param name="fractionBasisPoints">How far along the approach the ball is lost, 0…10,000.</param>
    /// <returns>The approach as far as it got.</returns>
    public static IReadOnlyList<SpatialPoint> CutApproach(PlannedPassage plan, int fractionBasisPoints)
    {
        var points = plan.Approach;
        var segments = points.Count - 1;
        var scaled = (long)Math.Clamp(fractionBasisPoints, 0, EngineRulesV2.Certain) * segments;
        var whole = (int)(scaled / EngineRulesV2.Certain);
        var remainder = (int)(scaled % EngineRulesV2.Certain);

        var cut = new List<SpatialPoint>(whole + 2);

        for (var index = 0; index <= whole && index < points.Count; index++)
        {
            cut.Add(points[index]);
        }

        if (remainder > 0 && whole < segments)
        {
            var from = points[whole];
            var to = points[whole + 1];
            var lost = new SpatialPoint(
                Lerp(from.X, to.X, remainder),
                Lerp(from.Y, to.Y, remainder));

            if (lost != cut[^1])
            {
                cut.Add(lost);
            }
        }

        return cut;
    }

    /// <summary>
    /// Gets where a ball won by the defence is cleared to: back to the middle third, unless it is already there.
    /// </summary>
    /// <param name="plan">The possession's plan.</param>
    /// <param name="from">Where the ball was won.</param>
    /// <param name="rules">The rules in force.</param>
    /// <returns>The clearance target, or <paramref name="from"/> when a kick is not worth recording.</returns>
    public static SpatialPoint ClearanceTarget(PlannedPassage plan, SpatialPoint from, EngineRulesV2 rules)
    {
        var fromX = AttackingX(from.X, plan.IsHome);
        var fromY = AttackingY(from.Y, plan.IsHome);

        if (fromX - rules.TurnoverMiddleThirdXBasisPoints < rules.MinClearanceDistanceBasisPoints)
        {
            return from;
        }

        // The plan's turnover point decides which way across the pitch the ball is hit, and by how much.
        var lateral = (AttackingY(plan.TurnoverPoint.Y, plan.IsHome) - (SpatialPitch.PitchWidth / 2)) / 2;

        return FromAttack(
            rules.TurnoverMiddleThirdXBasisPoints,
            Math.Clamp(fromY + lateral, TouchlineMargin, SpatialPitch.PitchWidth - TouchlineMargin),
            plan.IsHome);
    }

    /// <summary>
    /// Gets where the through ball to the player caught offside is played to: ahead of the ball, on the offside line.
    /// </summary>
    /// <param name="plan">The possession's plan.</param>
    /// <param name="from">Where the ball was when the pass was played.</param>
    /// <param name="rules">The rules in force.</param>
    public static SpatialPoint OffsideTarget(PlannedPassage plan, SpatialPoint from, EngineRulesV2 rules)
    {
        var fromX = AttackingX(from.X, plan.IsHome);
        var fromY = AttackingY(from.Y, plan.IsHome);
        var lateral = (AttackingY(plan.OffsidePoint.Y, plan.IsHome) - (SpatialPitch.PitchWidth / 2)) / 2;

        return FromAttack(
            Math.Clamp(Math.Max(rules.OffsideLineXBasisPoints, fromX + OffsideLead), 0, SpatialPitch.PitchLength - 200),
            Math.Clamp(fromY + lateral, TouchlineMargin, SpatialPitch.PitchWidth - TouchlineMargin),
            plan.IsHome);
    }

    /// <summary>
    /// Places every outcome of a strike at goal taken from one point (`engine-v5`).
    /// </summary>
    /// <param name="plan">The possession's plan, whose draws the targets are placed from.</param>
    /// <param name="origin">Where the strike is taken from: the shot point, the box, the spot, or a free kick.</param>
    /// <param name="rules">The rules in force.</param>
    public static StrikePlan StrikeFrom(PlannedPassage plan, SpatialPoint origin, EngineRulesV2 rules)
    {
        var draws = plan.Strike;
        var isHome = plan.IsHome;
        var line = SpatialPitch.PitchLength;
        var mouthMin = SpatialPitch.GoalYMin;
        var mouthMax = SpatialPitch.GoalYMax;

        var originX = AttackingX(origin.X, isHome);
        var originY = AttackingY(origin.Y, isHome);

        var lowAltitude = Spread(3, rules.StrikeLowAltitudeMax, draws.Height);

        // A strike that goes in: somewhere inside the posts, under the bar.
        var goalY = Spread(mouthMin + PostInset, mouthMax - PostInset, draws.Lateral);

        // A strike that is saved: the goalkeeper gets to it a few metres off the line, in front of the posts —
        // and never behind where the shot was struck from: a shot from close in is met nearer the line.
        var saveX = Math.Max(
            line - Spread(rules.SaveDepthMinBasisPoints, rules.SaveDepthMaxBasisPoints, draws.Depth),
            Math.Min(originX + SaveLead, line - rules.SaveDepthMinBasisPoints));
        var saveY = Math.Clamp(goalY, mouthMin + KeeperReach, mouthMax - KeeperReach);

        // A strike that misses: out of play, wide of a post or over the bar.
        var over = draws.MissVariant < rules.MissOverShareBasisPoints;
        int missY;
        int missAltitude;

        if (over)
        {
            missY = Spread(mouthMin - OverBarMargin, mouthMax + OverBarMargin, draws.Lateral);
            missAltitude = Spread(rules.ShotAltitude + 15, rules.OverBarAltitudeMax, draws.Height);
        }
        else
        {
            var offset = Spread(rules.MissWideMinBasisPoints, rules.MissWideMaxBasisPoints, draws.Lateral);

            missY = draws.Side < EngineRulesV2.Certain / 2 ? mouthMin - offset : mouthMax + offset;
            missAltitude = lowAltitude;
        }

        // The woodwork: a post, or the crossbar, and then a rebound out into the box.
        var post = draws.WoodworkVariant < rules.PostShareOfWoodworkBasisPoints;
        var woodworkY = post ? (draws.Side < EngineRulesV2.Certain / 2 ? mouthMin : mouthMax) : goalY;
        var woodworkAltitude = post ? lowAltitude : rules.ShotAltitude;

        var reboundX = line - Spread(rules.ReboundDistanceMinBasisPoints, rules.ReboundDistanceMaxBasisPoints, draws.Rebound);
        var reboundY = Math.Clamp(
            woodworkY + Spread(-ReboundSpread, ReboundSpread, draws.Jitter),
            mouthMin - 1_500,
            mouthMax + 1_500);

        // A block: a few metres in front of the shooter, on the way to goal. The shot band stops short of the
        // line, so the clamp only keeps the point on the pitch and never shortens the block.
        var blockX = Math.Min(
            originX + Spread(rules.BlockDistanceMinBasisPoints, rules.BlockDistanceMaxBasisPoints, draws.Block),
            line - BlockLineMargin);
        var blockY = Math.Clamp(
            originY + Spread(-BlockSpread, BlockSpread, draws.Jitter),
            0,
            SpatialPitch.PitchWidth);

        return new StrikePlan(
            Origin: origin,
            GoalTarget: FromAttack(line, goalY, isHome),
            GoalAltitude: lowAltitude,
            SaveTarget: FromAttack(saveX, saveY, isHome),
            SaveAltitude: lowAltitude,
            MissTarget: FromAttack(line, missY, isHome),
            MissAltitude: missAltitude,
            WoodworkTarget: FromAttack(line, woodworkY, isHome),
            WoodworkAltitude: woodworkAltitude,
            ReboundPoint: FromAttack(reboundX, reboundY, isHome),
            BlockPoint: FromAttack(blockX, blockY, isHome),
            PenaltySaved: draws.WoodworkVariant < rules.PenaltySavedShareBasisPoints);
    }

    /// <summary>Builds the ball's touches from the start to the pressure point.</summary>
    /// <returns>The path, and whether its final approach touch is a cross.</returns>
    private static (IReadOnlyList<SpatialPoint> Points, bool EndsInCross) Approach(
        Pcg32 derived,
        EngineRulesV2 rules,
        SpatialPoint start,
        int startAttackX,
        int startAttackY,
        int pressureAttackX,
        int pressureAttackY,
        bool isHome,
        MatchPassFocus focus)
    {
        var points = new List<SpatialPoint> { start };

        // How many touches the distance takes, from a drawn per-touch advance, bounded by the rules' touch
        // count. A possession already in the final third needs fewer.
        var advance = Math.Max(1, derived.NextRange(rules.MinTouchAdvanceBasisPoints, rules.MaxTouchAdvanceBasisPoints));
        var distance = Math.Max(0, pressureAttackX - startAttackX);
        var minIntermediates = Math.Max(1, rules.MinPassageTouches - 2);
        var maxIntermediates = Math.Max(minIntermediates, rules.MaxPassageTouches - 2);
        var intermediates = distance <= 0
            ? 0
            : Math.Clamp((distance + advance - 1) / advance, minIntermediates, maxIntermediates);

        // A cross is delivered from the lane the ball arrives in, and how often depends on the lane and on the
        // focus (`engine-v9`). The roll is the one the engine always took.
        var arrivalLane = pressureAttackY < rules.PassLeftLaneMaxYBasisPoints
            ? PassLane.Left
            : pressureAttackY >= rules.PassRightLaneMinYBasisPoints ? PassLane.Right : PassLane.Centre;
        var endsInCross = derived.RollBasisPoints(CrossShare(focus, arrivalLane, rules));

        for (var index = 1; index <= intermediates; index++)
        {
            var fraction = index * EngineRulesV2.Certain / (intermediates + 1);
            var x = Lerp(startAttackX, pressureAttackX, fraction);
            var y = Lerp(startAttackY, pressureAttackY, fraction) + LateralDrift(derived, rules);

            // The touches between a middle start and a flank destination would otherwise all stay in the middle,
            // so the focus moves them into the lanes too (`engine-v8`).
            y = FocusLateral(int.Clamp(y, 0, SpatialPitch.PitchWidth - 1), focus, rules);

            points.Add(FromAttack(int.Clamp(x, 0, SpatialPitch.PitchLength), int.Clamp(y, 0, SpatialPitch.PitchWidth), isHome));
        }

        points.Add(FromAttack(pressureAttackX, pressureAttackY, isHome));

        return (points, endsInCross);
    }

    /// <summary>
    /// Moves a uniform lateral draw so the ball's destinations fall in the lanes a side's pass focus favours
    /// (`engine-v8`).
    /// </summary>
    /// <remarks>
    /// The draw is already taken, so the focus consumes nothing from the stream: a side with no preference gets
    /// its draw back unchanged and plays exactly as it did before the instruction existed. Otherwise the draw is
    /// read as a position in the cumulative share of the three lanes and rescaled into the lane it falls in, in
    /// integer arithmetic. Left is the low end of the attacking side's own scale, as in the shot zones.
    /// </remarks>
    /// <param name="draw">A uniform position across the pitch on the side's own scale, 0…7,000.</param>
    /// <param name="focus">The side's pass focus.</param>
    /// <param name="rules">The rules in force, which supply the lanes and the shares.</param>
    /// <returns>The position, on the side's own scale.</returns>
    internal static int FocusLateral(int draw, MatchPassFocus focus, EngineRulesV2 rules)
    {
        if (focus == MatchPassFocus.Balanced)
        {
            return draw;
        }

        var (left, centre, right) = focus switch
        {
            MatchPassFocus.Centre => (rules.PassFocusCentreFlankPercent, rules.PassFocusCentreCentrePercent, rules.PassFocusCentreFlankPercent),
            MatchPassFocus.CentreAndLeft => (rules.PassFocusPairFlankPercent, rules.PassFocusPairCentrePercent, rules.PassFocusPairOtherFlankPercent),
            MatchPassFocus.CentreAndRight => (rules.PassFocusPairOtherFlankPercent, rules.PassFocusPairCentrePercent, rules.PassFocusPairFlankPercent),
            MatchPassFocus.Wings => (rules.PassFocusWingsFlankPercent, rules.PassFocusWingsCentrePercent, rules.PassFocusWingsFlankPercent),
            _ => throw new ArgumentOutOfRangeException(nameof(focus), focus, "Unknown pass focus."),
        };

        var leftEnd = rules.PassLeftLaneMaxYBasisPoints;
        var rightStart = rules.PassRightLaneMinYBasisPoints;
        var width = (long)SpatialPitch.PitchWidth;

        // Where the draw sits among all the shares, 0…width × 100, and the shares' upper bounds on that scale.
        var position = (long)Math.Clamp(draw, 0, SpatialPitch.PitchWidth - 1) * 100;
        var leftShare = left * width;
        var centreShare = (left + centre) * width;

        if (position < leftShare)
        {
            return (int)(position * leftEnd / leftShare);
        }

        if (position < centreShare)
        {
            return leftEnd + (int)((position - leftShare) * (rightStart - leftEnd) / (centre * width));
        }

        return rightStart + (int)((position - centreShare) * (SpatialPitch.PitchWidth - rightStart) / (right * width));
    }

    /// <summary>Maps a point on the attacking side's own scale to a pitch coordinate.</summary>
    /// <param name="attackingX">The distance from the attacker's own goal, 0…10,000.</param>
    /// <param name="attackingY">The position across the pitch, 0…7,000.</param>
    /// <param name="isHome">Whether the attacker plays towards the right of the shared pitch.</param>
    private static SpatialPoint FromAttack(int attackingX, int attackingY, bool isHome)
    {
        var x = isHome ? attackingX : SpatialPitch.PitchLength - attackingX;
        var y = isHome ? attackingY : SpatialPitch.PitchWidth - attackingY;

        return new SpatialPoint(
            int.Clamp(x, 0, SpatialPitch.PitchLength),
            int.Clamp(y, 0, SpatialPitch.PitchWidth));
    }

    /// <summary>Gets how far up the pitch a pitch point is on a given side's own scale.</summary>
    private static int AttackingX(int x, bool isHome) =>
        isHome ? x : SpatialPitch.PitchLength - x;

    /// <summary>Gets where across the pitch a pitch point is on a given side's own scale.</summary>
    private static int AttackingY(int y, bool isHome) =>
        isHome ? y : SpatialPitch.PitchWidth - y;

    private static int Lerp(int from, int to, int fractionBasisPoints) =>
        (int)(from + (((long)(to - from) * fractionBasisPoints) / EngineRulesV2.Certain));

    /// <summary>Places a value between two bounds from a basis-point draw: 0 is the low bound, just under 10,000 the high.</summary>
    private static int Spread(int low, int high, int drawBasisPoints) =>
        low + (int)(((long)(high - low) * drawBasisPoints) / EngineRulesV2.Certain);

    private static int LateralDrift(Pcg32 derived, EngineRulesV2 rules)
    {
        var draw = derived.NextRange(-rules.MaxTouchLateralDriftBasisPoints, rules.MaxTouchLateralDriftBasisPoints);

        return (int)(((long)draw * SpatialPitch.PitchWidth) / EngineRulesV2.Certain);
    }

    /// <summary>Chooses the shot zone, weighted towards the middle of the pitch unless the side asks for a lane.</summary>
    private static ShotZone ChooseZone(Pcg32 derived, EngineRulesV2 rules, MatchPassFocus focus)
    {
        // One central zone, two inside channels, two wide zones, in the shares the rules give them (engine-v6
        // moved the 40/20/20/10/10 mix here from the code; engine-v9 lets the pass focus move it). The roll is
        // the one the engine always took, so only where it lands changes.
        var roll = derived.NextInt(100);
        var (central, insideLeft, wideLeft, insideRight, wideRight) = ShotZoneShares(focus, rules);
        var insideLeftEnd = central + insideLeft;
        var insideRightEnd = insideLeftEnd + insideRight;
        var wideLeftEnd = insideRightEnd + wideLeft;

        if (roll < central)
        {
            return ShotZone.Central;
        }

        if (roll < insideLeftEnd)
        {
            return ShotZone.InsideLeft;
        }

        if (roll < insideRightEnd)
        {
            return ShotZone.InsideRight;
        }

        return roll < wideLeftEnd ? ShotZone.WideLeft : ShotZone.WideRight;
    }

    /// <summary>Gets the share of final approaches crossed, by the lane the ball arrives in (`engine-v9`).</summary>
    /// <remarks>
    /// A side with no preference, and one that plays both wings, crosses from the flanks and hardly ever from the
    /// middle. A side that favours the centre plays in the middle lane, so it crosses from there too; one that
    /// favours the centre and a flank crosses most from that flank and the middle, and least from the other flank.
    /// </remarks>
    /// <param name="focus">The side's pass focus.</param>
    /// <param name="lane">The lane the ball arrives in, on the side's own scale.</param>
    /// <param name="rules">The rules in force.</param>
    internal static int CrossShare(MatchPassFocus focus, PassLane lane, EngineRulesV2 rules)
    {
        var flank = lane != PassLane.Centre;

        return focus switch
        {
            MatchPassFocus.Centre => flank ? rules.CrossFocusCentreFlankBasisPoints : rules.CrossFocusCentreCentreBasisPoints,
            MatchPassFocus.CentreAndLeft => lane switch
            {
                PassLane.Left => rules.CrossFocusPairFlankBasisPoints,
                PassLane.Centre => rules.CrossFocusPairCentreBasisPoints,
                _ => rules.CrossFocusPairOtherFlankBasisPoints,
            },
            MatchPassFocus.CentreAndRight => lane switch
            {
                PassLane.Right => rules.CrossFocusPairFlankBasisPoints,
                PassLane.Centre => rules.CrossFocusPairCentreBasisPoints,
                _ => rules.CrossFocusPairOtherFlankBasisPoints,
            },
            _ => flank ? rules.CrossShareFlankLaneBasisPoints : rules.CrossShareCentreLaneBasisPoints,
        };
    }

    /// <summary>Gets the percent of open-play shots taken from each zone, for a side's pass focus (`engine-v9`).</summary>
    /// <param name="focus">The side's pass focus.</param>
    /// <param name="rules">The rules in force.</param>
    internal static (int Central, int InsideLeft, int WideLeft, int InsideRight, int WideRight) ShotZoneShares(
        MatchPassFocus focus,
        EngineRulesV2 rules) => focus switch
        {
            MatchPassFocus.Balanced => (
                rules.ShotZoneCentralPercent,
                rules.ShotZoneInsidePercent,
                rules.ShotZoneWidePercent,
                rules.ShotZoneInsidePercent,
                rules.ShotZoneWidePercent),
            MatchPassFocus.Centre => (
                rules.ShotFocusCentreCentralPercent,
                rules.ShotFocusCentreInsidePercent,
                rules.ShotFocusCentreWidePercent,
                rules.ShotFocusCentreInsidePercent,
                rules.ShotFocusCentreWidePercent),
            MatchPassFocus.CentreAndLeft => (
                rules.ShotFocusPairCentralPercent,
                rules.ShotFocusPairInsidePercent,
                rules.ShotFocusPairWidePercent,
                rules.ShotFocusPairOtherInsidePercent,
                rules.ShotFocusPairOtherWidePercent),
            MatchPassFocus.CentreAndRight => (
                rules.ShotFocusPairCentralPercent,
                rules.ShotFocusPairOtherInsidePercent,
                rules.ShotFocusPairOtherWidePercent,
                rules.ShotFocusPairInsidePercent,
                rules.ShotFocusPairWidePercent),
            MatchPassFocus.Wings => (
                rules.ShotFocusWingsCentralPercent,
                rules.ShotFocusWingsInsidePercent,
                rules.ShotFocusWingsWidePercent,
                rules.ShotFocusWingsInsidePercent,
                rules.ShotFocusWingsWidePercent),
            _ => throw new ArgumentOutOfRangeException(nameof(focus), focus, "Unknown pass focus."),
        };

    /// <summary>
    /// Gets how much more or less often a side's progressed possession becomes a shot, for its pass focus
    /// (`engine-v9`).
    /// </summary>
    /// <param name="focus">The side's pass focus.</param>
    /// <param name="rules">The rules in force.</param>
    /// <returns>The multiplier in basis points, 10,000 for a side with no preference.</returns>
    internal static int ChanceVolume(MatchPassFocus focus, EngineRulesV2 rules) => focus switch
    {
        MatchPassFocus.Balanced => EngineRulesV2.Certain,
        MatchPassFocus.Centre => rules.ChanceVolumeCentreBasisPoints,
        MatchPassFocus.CentreAndLeft or MatchPassFocus.CentreAndRight => rules.ChanceVolumePairBasisPoints,
        MatchPassFocus.Wings => rules.ChanceVolumeWingsBasisPoints,
        _ => throw new ArgumentOutOfRangeException(nameof(focus), focus, "Unknown pass focus."),
    };

    /// <summary>Places a shot across the pitch inside its zone's band.</summary>
    private static int ZoneY(ShotZone zone, Pcg32 derived, EngineRulesV2 rules)
    {
        var middle = SpatialPitch.PitchWidth / 2;

        return zone switch
        {
            ShotZone.Central => derived.NextRange(rules.ShotCentralBandYMinBasisPoints, rules.ShotCentralBandYMaxBasisPoints),
            ShotZone.InsideLeft => derived.NextRange(rules.ShotInsideBandYMinBasisPoints, middle),
            ShotZone.InsideRight => derived.NextRange(middle, rules.ShotInsideBandYMaxBasisPoints),
            ShotZone.WideLeft => derived.NextRange(rules.ShotWideBandYMinBasisPoints, rules.ShotInsideBandYMinBasisPoints),
            _ => derived.NextRange(rules.ShotInsideBandYMaxBasisPoints, rules.ShotWideBandYMaxBasisPoints),
        };
    }
}

/// <summary>The three lanes across the pitch the pass focus speaks of, on a side's own scale (`engine-v9`).</summary>
internal enum PassLane
{
    /// <summary>The left lane, the low end of the side's own scale.</summary>
    Left = 0,

    /// <summary>The centre lane.</summary>
    Centre = 1,

    /// <summary>The right lane.</summary>
    Right = 2,
}
