using System.Globalization;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Configuration;

/// <summary>
/// Every tunable constant of the match engine, versioned as rules set 2.
/// </summary>
/// <remarks>
/// <para>
/// All formula constants live here rather than as magic numbers in the simulation, so a balance change
/// is one diff in one file that carries a rules-version bump (RULE-1, RULE-3, ADR-0004). The unit-rating
/// weight tables are the one thing that does not fit a scalar and live in
/// <c>Ratings/UnitRatingWeights.cs</c>, versioned alongside this type.
/// </para>
/// <para>
/// Two scales run through the whole engine and mixing them is the easiest way to write a subtly wrong
/// formula:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The <b>rating scale</b> is <c>0…100</c>, and one attribute point is exactly five units. An attribute
/// of 1 is 5, an attribute of 20 is 100. Ratings are only ever compared against each other.
/// </description></item>
/// <item><description>
/// The <b>basis-point scale</b> is <c>0…10_000</c>, where 10_000 is certainty and 5_000 is even. Every
/// probability and every modifier is expressed in it, and two probabilities combine exactly as
/// <c>a * b / 10_000</c> with integer arithmetic.
/// </description></item>
/// </list>
/// <para>
/// Nothing here is a floating-point number. Two integer probabilities compose to an integer, so an
/// outcome at the end of a possession chain depends only on the values that produced it and not on the
/// rounding behaviour of any intermediate representation.
/// </para>
/// </remarks>
public sealed record EngineRulesV2
{
    /// <summary>The version label recorded alongside a result produced under these rules.</summary>
    public const string Version = EngineVersions.RuleSetLabel;

    /// <summary>The rating scale's maximum: one attribute point is five rating units.</summary>
    public const int RatingScale = 100;

    /// <summary>The basis-point scale: 10_000 is certainty.</summary>
    public const int Certain = 10_000;

    /// <summary>The scale slot coordinates are normalized to (`TAC-9`).</summary>
    public const int SlotCoordinateScale = 10_000;

    /// <summary>The smallest a basis-point multiplier may be.</summary>
    public const int MinMultiplier = 5_000;

    /// <summary>The largest a basis-point multiplier may be.</summary>
    public const int MaxMultiplier = 25_000;

    /// <summary>The engine's rules set 1.</summary>
    public static EngineRulesV2 Default { get; } = new();

    // ---- Clock -----------------------------------------------------------------------------------

    /// <summary>Regulation time in minutes (MAT-3).</summary>
    public int RegulationMinutes { get; init; } = 90;

    /// <summary>The minute a half ends, where half-time is taken.</summary>
    public int HalfTimeMinute { get; init; } = 45;

    /// <summary>Seconds in a minute. Named because it appears in every clock formula.</summary>
    public int SecondsPerMinute { get; init; } = 60;

    /// <summary>The least stoppage time a half can be given, in minutes.</summary>
    public int MinStoppageMinutes { get; init; } = 1;

    /// <summary>The most stoppage time a half can be given, in minutes.</summary>
    public int MaxStoppageMinutes { get; init; } = 10;

    /// <summary>Stoppage added purely because the clock has to be topped up.</summary>
    public int StoppageBaseSeconds { get; init; } = 150;

    /// <summary>The most a half's stoppage is jittered above the base, so no two halves end alike.</summary>
    public int StoppageJitterSeconds { get; init; } = 60;

    /// <summary>Stoppage added per goal.</summary>
    public int StoppageSecondsPerGoal { get; init; } = 20;

    /// <summary>Stoppage added per card.</summary>
    public int StoppageSecondsPerCard { get; init; } = 25;

    /// <summary>Stoppage added per substitution.</summary>
    public int StoppageSecondsPerSubstitution { get; init; } = 15;

    /// <summary>Stoppage added per injury.</summary>
    public int StoppageSecondsPerInjury { get; init; } = 60;

    /// <summary>The least time one possession can consume, in seconds.</summary>
    /// <remarks>
    /// Retuned from 16 in `engine-v5`. The half-time clock fix gave the second half its true length, about four
    /// per cent more football than `engine-v4` played, and every per-possession probability was calibrated
    /// against the shorter match. Lengthening a possession by the same four to five per cent keeps the number of
    /// possessions in a match where the calibration put it, so goals, shots, fouls, and cards come out as they
    /// did without touching a single probability.
    /// </remarks>
    public int PossessionSecondsMin { get; init; } = 17;

    /// <summary>The most time one possession can consume, in seconds (44 before `engine-v5`).</summary>
    public int PossessionSecondsMax { get; init; } = 46;

    /// <summary>What a high tempo multiplies the time a possession takes by, so play is more end-to-end.</summary>
    public int HighTempoPossessionSecondsMultiplierBasisPoints { get; init; } = 8_000;

    /// <summary>What a low tempo multiplies the time a possession takes by, so play is more patient.</summary>
    public int LowTempoPossessionSecondsMultiplierBasisPoints { get; init; } = 12_000;

    /// <summary>The shortest a possession can be after the tempo multiplier, so the clock always advances.</summary>
    public int MinEffectivePossessionSeconds { get; init; } = 6;

    // ---- Possession ------------------------------------------------------------------------------

    /// <summary>The share of possession an even contest gives the home side.</summary>
    public int BasePossessionBasisPoints { get; init; } = 5_000;

    /// <summary>How far a maximal control differential can swing possession away from an even split.</summary>
    /// <remarks>
    /// Retuned from 2,400 in `engine-v5`. A dead ball now belongs to somebody (`MAT-12`), so the possession draw
    /// only decides the possessions that begin from play, and the stronger side's edge in it was diluted: after
    /// its shot was saved the ball went to the defender by rule. Widening the swing gives that edge back — the
    /// ability curve (an underdog three points down wins 16.4% of the time) and home advantage (+4.2 points)
    /// land where `engine-v4` had them. The possession share itself stays inside <see cref="MinPossessionBasisPoints"/>
    /// and <see cref="MaxPossessionBasisPoints"/>, so a maximal mismatch is clamped well before the swing is spent.
    /// </remarks>
    public int PossessionControlSwingBasisPoints { get; init; } = 4_000;

    /// <summary>Possession's small, fixed home bonus, separate from home advantage on the ratings.</summary>
    public int PossessionHomeBonusBasisPoints { get; init; } = 120;

    /// <summary>The floor on a side's possession share, so neither side is ever shut out.</summary>
    public int MinPossessionBasisPoints { get; init; } = 2_000;

    /// <summary>The ceiling on a side's possession share.</summary>
    public int MaxPossessionBasisPoints { get; init; } = 8_000;

    // ---- Progression and creation ----------------------------------------------------------------

    /// <summary>The baseline chance that a possession is progressed out of build-up.</summary>
    public int BaseProgressBasisPoints { get; init; } = 6_200;

    /// <summary>The floor on the progression chance.</summary>
    public int MinProgressBasisPoints { get; init; } = 3_400;

    /// <summary>The ceiling on the progression chance.</summary>
    public int MaxProgressBasisPoints { get; init; } = 9_000;

    /// <summary>How much a maximal control differential moves the progression chance.</summary>
    public int ProgressControlSwingBasisPoints { get; init; } = 2_400;

    /// <summary>The baseline chance that a progressed possession becomes a chance on goal.</summary>
    public int BaseCreationBasisPoints { get; init; } = 2_400;

    /// <summary>The floor on the creation chance.</summary>
    public int MinCreationBasisPoints { get; init; } = 1_100;

    /// <summary>The ceiling on the creation chance.</summary>
    public int MaxCreationBasisPoints { get; init; } = 6_200;

    /// <summary>How much a maximal creation-versus-shape differential moves the creation chance.</summary>
    public int CreationSwingBasisPoints { get; init; } = 2_800;

    /// <summary>The share of failed progressions that are offsides rather than turnovers.</summary>
    public int OffsideShareOfTurnoverBasisPoints { get; init; } = 800;

    /// <summary>The share of failed creations that win a corner rather than turning over.</summary>
    public int CornerShareOfFailedCreationBasisPoints { get; init; } = 1_200;

    /// <summary>The chance a corner produces a headed chance on goal.</summary>
    public int CornerChanceBasisPoints { get; init; } = 3_400;

    /// <summary>The chance a foul is committed inside the box and becomes a penalty.</summary>
    public int PenaltyFromFoulBasisPoints { get; init; } = 40;

    /// <summary>
    /// The margin at which a settled scoreline starts to change how a side plays.
    /// </summary>
    /// <remarks>
    /// The games-state effect below is what stops the score distribution being a pure Poisson. Real football
    /// is under-dispersed: a side three goals up stops chasing a fourth and a side three down faces a defence
    /// with nothing to lose by sitting deep, so seven-goal matches happen about half as often as independent
    /// scoring would predict. Without this, the mean is right and the tail is nonsense.
    /// </remarks>
    public int GameStateMarginThresholdGoals { get; init; } = 2;

    /// <summary>How much each goal of margin above the threshold reduces the leading side's creation.</summary>
    public int LeadingCreationStepBasisPoints { get; init; } = 900;

    /// <summary>How much each goal of margin above the threshold raises the trailing side's creation.</summary>
    public int TrailingCreationStepBasisPoints { get; init; } = 600;

    /// <summary>The most the scoreline may move either side's creation, so it stays a nudge and not a rewrite.</summary>
    public int MaxGameStateModifierBasisPoints { get; init; } = 3_000;

    // ---- Shot resolution -------------------------------------------------------------------------

    /// <summary>The floor on a shot's goal probability, so no chance is impossible.</summary>
    public int MinShotGoalBasisPoints { get; init; } = 220;

    /// <summary>The ceiling on a shot's goal probability, so no chance is a formality.</summary>
    public int MaxShotGoalBasisPoints { get; init; } = 5_600;

    /// <summary>
    /// The baseline goal probability of an average chance from an inside channel.
    /// </summary>
    /// <remarks>
    /// Calibrated with the laboratory to about 2.7–2.8 goals a match under the spatial play model
    /// (Stage 7). The mean is what sets the shape of the score tail — a higher mean thickens it faster than
    /// any game-state effect thins it — so this constant and <see cref="GameStateMarginThresholdGoals"/> were
    /// tuned together against a measured distribution rather than guessed at.
    /// </remarks>
    public int BaseShotGoalBasisPoints { get; init; } = 865;

    /// <summary>How much a maximal finishing-versus-goalkeeping differential moves the goal probability.</summary>
    public int ShotQualitySwingBasisPoints { get; init; } = 1_900;

    /// <summary>What a shot from the central zone multiplies its goal chance by, relative to an inside channel.</summary>
    public int CentralZoneMultiplierBasisPoints { get; init; } = 15_000;

    /// <summary>What a shot from an inside channel multiplies its goal chance by, the reference case.</summary>
    public int InsideZoneMultiplierBasisPoints { get; init; } = 10_000;

    /// <summary>What a shot from a wide zone multiplies its goal chance by.</summary>
    public int WideZoneMultiplierBasisPoints { get; init; } = 8_000;

    /// <summary>The share of saved-or-blocked shots that hit the woodwork.</summary>
    public int WoodworkShareBasisPoints { get; init; } = 700;

    /// <summary>The share of non-goal shots that are blocked rather than saved or off target.</summary>
    public int BlockedShareBasisPoints { get; init; } = 2_600;

    /// <summary>The baseline chance that an on-target shot that is not a goal is saved.</summary>
    public int BaseSaveBasisPoints { get; init; } = 5_000;

    /// <summary>The floor on the save chance, so a great finisher still gets stopped sometimes.</summary>
    public int MinSaveBasisPoints { get; init; } = 2_500;

    /// <summary>The ceiling on the save chance.</summary>
    public int MaxSaveBasisPoints { get; init; } = 7_500;

    /// <summary>The goal probability of a penalty.</summary>
    public int PenaltyGoalBasisPoints { get; init; } = 7_600;

    /// <summary>
    /// The attribute-scale gap at which a shot, a save, a penalty, or a free kick contest's swing is applied in
    /// full (`engine-v6`).
    /// </summary>
    /// <remarks>
    /// These contests compare a shooter's attribute (1–20) with the goalkeeper's rating divided back down to
    /// the same scale. Through `engine-v5` they converted that gap with <see cref="RatingDifferentialReference"/>,
    /// which is meant for gaps in the hundreds, so the whole skill range moved a shot by about a third of a
    /// percentage point. This constant is the shot's own reference, so finishing and goalkeeping can be tuned
    /// without touching the duel curve.
    /// </remarks>
    public int ShotContestReference { get; init; } = 150;

    /// <summary>How far a maximal taker-versus-keeper gap moves a penalty's goal chance.</summary>
    public int PenaltyQualitySwingBasisPoints { get; init; } = 3_000;

    /// <summary>The floor on a penalty's goal chance, so a penalty can never be hopeless.</summary>
    public int PenaltyMinGoalBasisPoints { get; init; } = 5_500;

    /// <summary>The ceiling on a penalty's goal chance, so a penalty can never be a formality.</summary>
    public int PenaltyMaxGoalBasisPoints { get; init; } = 9_400;

    /// <summary>The attribute a corner taker's delivery is measured against, so an average taker adds nothing.</summary>
    public int CornerDeliveryBaseline { get; init; } = 13;

    /// <summary>How much of a corner taker's delivery edge is added to the header contest's attacking score.</summary>
    public int CornerDeliveryAerialWeight { get; init; } = 4;

    /// <summary>How far each point of a corner taker's delivery edge moves the chance a corner is headed at goal.</summary>
    public int CornerDeliveryChanceStepBasisPoints { get; init; } = 60;

    /// <summary>The floor on the chance a corner produces a headed chance, after the taker's delivery.</summary>
    public int CornerChanceMinBasisPoints { get; init; } = 1_500;

    /// <summary>The ceiling on the chance a corner produces a headed chance, after the taker's delivery.</summary>
    public int CornerChanceMaxBasisPoints { get; init; } = 6_000;

    /// <summary>The share of open-play shots taken from the central zone, in percent.</summary>
    public int ShotZoneCentralPercent { get; init; } = 40;

    /// <summary>The share of open-play shots taken from each inside channel, in percent.</summary>
    public int ShotZoneInsidePercent { get; init; } = 20;

    /// <summary>The share of open-play shots taken from each wide zone, in percent.</summary>
    public int ShotZoneWidePercent { get; init; } = 10;

    // ---- Pass focus (engine-v8) ------------------------------------------------------------------
    // The shares below are shares of a uniform lateral draw, which the planner remaps into the lanes. The ball's
    // measured shares sit nearer the middle, because every possession starts there; the values are calibrated so
    // that the measured shares are about 20/60/20 (centre), 39/22/39 (wings), and 37/44/20 (centre and left).

    /// <summary>Where the left lane ends, across the pitch on a side's own scale, 0…7,000.</summary>
    public int PassLeftLaneMaxYBasisPoints { get; init; } = 2_333;

    /// <summary>Where the right lane begins, across the pitch on a side's own scale, 0…7,000.</summary>
    public int PassRightLaneMinYBasisPoints { get; init; } = 4_667;

    /// <summary>The share of a uniform lateral draw that lands in the centre lane when the focus is the centre alone, in percent.</summary>
    public int PassFocusCentreCentrePercent { get; init; } = 40;

    /// <summary>The share in each flank when the focus is the centre alone, in percent.</summary>
    public int PassFocusCentreFlankPercent { get; init; } = 30;

    /// <summary>The share in the centre lane when the focus is the centre and a flank, in percent.</summary>
    public int PassFocusPairCentrePercent { get; init; } = 27;

    /// <summary>The share in the favoured flank when the focus is the centre and a flank, in percent.</summary>
    public int PassFocusPairFlankPercent { get; init; } = 42;

    /// <summary>The share in the other flank when the focus is the centre and a flank, in percent.</summary>
    public int PassFocusPairOtherFlankPercent { get; init; } = 31;

    /// <summary>The share in each flank when the focus is both wings, in percent.</summary>
    public int PassFocusWingsFlankPercent { get; init; } = 46;

    /// <summary>The share in the centre lane when the focus is both wings, in percent.</summary>
    public int PassFocusWingsCentrePercent { get; init; } = 8;

    // ---- Pass focus: where the shots are taken, and how many (engine-v9) --------------------------
    // A side's focus also moves its shots. The zone shares are the percent of open-play shots taken from each zone
    // (one central, an inside channel and a wide zone on each side); a side with no preference keeps the
    // ShotZone* shares above. The chance volume scales how often a progressed possession becomes a shot, because
    // the zones differ in how often they score: the centre scores best, so a side that shoots from it takes fewer
    // shots, and a side that shoots from wide takes more.

    /// <summary>The share of shots from the central zone when the focus is the centre alone, in percent.</summary>
    public int ShotFocusCentreCentralPercent { get; init; } = 54;

    /// <summary>The share of shots from each inside channel when the focus is the centre alone, in percent.</summary>
    public int ShotFocusCentreInsidePercent { get; init; } = 15;

    /// <summary>The share of shots from each wide zone when the focus is the centre alone, in percent.</summary>
    public int ShotFocusCentreWidePercent { get; init; } = 8;

    /// <summary>The share of shots from the central zone when the focus is the centre and a flank, in percent.</summary>
    public int ShotFocusPairCentralPercent { get; init; } = 34;

    /// <summary>The share of shots from the favoured flank's inside channel when the focus is the centre and a flank, in percent.</summary>
    public int ShotFocusPairInsidePercent { get; init; } = 28;

    /// <summary>The share of shots from the favoured flank's wide zone when the focus is the centre and a flank, in percent.</summary>
    public int ShotFocusPairWidePercent { get; init; } = 15;

    /// <summary>The share of shots from the other flank's inside channel when the focus is the centre and a flank, in percent.</summary>
    public int ShotFocusPairOtherInsidePercent { get; init; } = 15;

    /// <summary>The share of shots from the other flank's wide zone when the focus is the centre and a flank, in percent.</summary>
    public int ShotFocusPairOtherWidePercent { get; init; } = 8;

    /// <summary>The share of shots from the central zone when the focus is both wings, in percent.</summary>
    public int ShotFocusWingsCentralPercent { get; init; } = 12;

    /// <summary>The share of shots from each inside channel when the focus is both wings, in percent.</summary>
    public int ShotFocusWingsInsidePercent { get; init; } = 29;

    /// <summary>The share of shots from each wide zone when the focus is both wings, in percent.</summary>
    public int ShotFocusWingsWidePercent { get; init; } = 15;

    /// <summary>The multiplier on the chance a progressed possession becomes a shot, for the centre alone.</summary>
    public int ChanceVolumeCentreBasisPoints { get; init; } = 9_350;

    /// <summary>The multiplier on the chance a progressed possession becomes a shot, for the centre and a flank.</summary>
    public int ChanceVolumePairBasisPoints { get; init; } = 10_300;

    /// <summary>The multiplier on the chance a progressed possession becomes a shot, for both wings.</summary>
    public int ChanceVolumeWingsBasisPoints { get; init; } = 11_600;

    // ---- Spatial play (engine-v3) ----------------------------------------------------------------

    /// <summary>
    /// The share of a progressed possession the ball-carrier keeps through a 1v1 dribble rather than losing
    /// to the covering defender, before attributes are weighed.
    /// </summary>
    public int BaseGroundDuelBasisPoints { get; init; } = 5_000;

    /// <summary>How far a maximal dribble-versus-tackling attribute differential moves the ground duel.</summary>
    public int GroundDuelSwingBasisPoints { get; init; } = 3_500;

    /// <summary>The share of an aerial contest the attacker wins before attributes are weighed.</summary>
    public int BaseAerialDuelBasisPoints { get; init; } = 5_000;

    /// <summary>How far a maximal aerial attribute differential moves the aerial duel.</summary>
    public int AerialDuelSwingBasisPoints { get; init; } = 3_200;

    /// <summary>The share of possessions that open with a contested loose-ball scramble at all.</summary>
    /// <remarks>
    /// Most possessions begin with the side in possession already holding the ball cleanly; only a share
    /// start with a genuine 50/50. Keeping the scramble occasional is what keeps the model from taxing
    /// every attack with a coin flip.
    /// </remarks>
    public int ScrambleOpeningBasisPoints { get; init; } = 1_500;

    /// <summary>
    /// The attribute-scale differential at which a duel's swing is applied in full (`engine-v3`).
    /// </summary>
    /// <remarks>
    /// Duels read attributes on the 1–20 scale, whose maximal weighted differential is a tenth or so of the
    /// unit ratings', so they carry their own reference rather than borrowing the ratings'. At 150, a
    /// maximal mismatch applies the full swing and wins about the plan's 85% share; an even contest stays
    /// at even.
    /// </remarks>
    public int DuelDifferentialReference { get; init; } = 150;

    /// <summary>The share of a loose-ball scramble the side in possession wins before attributes are weighed.</summary>
    public int BaseScrambleBasisPoints { get; init; } = 5_000;

    /// <summary>How far a maximal scramble attribute differential moves the scramble.</summary>
    public int ScrambleSwingBasisPoints { get; init; } = 3_000;

    /// <summary>
    /// The share of a ground duel won that converts into extra creation appetite, which is how a dribble
    /// past a man becomes the chance that follows.
    /// </summary>
    public int DribbleCreationBonusBasisPoints { get; init; } = 1_200;

    /// <summary>The home crowd's duel bonus (master plan Stage 2), applied on top of the rating advantage.</summary>
    public int DuelHomeBonusBasisPoints { get; init; } = 400;

    /// <summary>
    /// The stochastic variance around every duel, scramble, and set-piece roll (master plan Stage 2).
    /// </summary>
    /// <remarks>
    /// The underdog factor: a bounded band of noise each contest is rolled inside, so a worse side wins the
    /// contests it would lose nine times in ten rather than never, while attributes still govern the
    /// overwhelming share of outcomes over a season.
    /// </remarks>
    public int UnderdogVarianceBasisPoints { get; init; } = 1_200;

    /// <summary>The floor on any duel's win chance, so no duel is certain.</summary>
    public int DuelMinWinBasisPoints { get; init; } = 1_500;

    /// <summary>The ceiling on any duel's win chance.</summary>
    public int DuelMaxWinBasisPoints { get; init; } = 8_500;

    /// <summary>What aggressive tackling adds to the defender's duel score, in attribute points.</summary>
    public int AggressiveTacklingDuelScoreBonus { get; init; } = 6;

    /// <summary>What staying on your feet takes from the defender's duel score, in attribute points.</summary>
    public int StayOnFeetDuelScorePenalty { get; init; } = 4;

    /// <summary>Weight of Dribbling in the carrier's ground duel score.</summary>
    public int GroundDuelDribblingWeight { get; init; } = 4;

    /// <summary>Weight of Agility in the carrier's ground duel score.</summary>
    public int GroundDuelAgilityWeight { get; init; } = 3;

    /// <summary>Weight of Pace in the carrier's ground duel score.</summary>
    public int GroundDuelPaceWeight { get; init; } = 3;

    /// <summary>Weight of Tackling in the defender's ground duel score.</summary>
    public int GroundDuelTacklingWeight { get; init; } = 4;

    /// <summary>Weight of Positioning in the defender's ground duel score.</summary>
    public int GroundDuelPositioningWeight { get; init; } = 3;

    /// <summary>Weight of Strength in the defender's ground duel score.</summary>
    public int GroundDuelStrengthWeight { get; init; } = 3;

    /// <summary>Weight of Jumping reach in both sides' aerial duel score.</summary>
    public int AerialDuelJumpingReachWeight { get; init; } = 5;

    /// <summary>Weight of Heading in both sides' aerial duel score.</summary>
    public int AerialDuelHeadingWeight { get; init; } = 3;

    /// <summary>Weight of Strength in both sides' aerial duel score.</summary>
    public int AerialDuelStrengthWeight { get; init; } = 2;

    /// <summary>Weight of Positioning in both sides' aerial duel score: getting to where the ball will drop (`engine-v10`).</summary>
    public int AerialDuelPositioningWeight { get; init; } = 2;

    /// <summary>
    /// What the lowest effective Positioning multiplies a player's weight for a shot or a header by, in basis
    /// points (`engine-v10`).
    /// </summary>
    public int PositioningFloorBasisPoints { get; init; } = 7_000;

    /// <summary>
    /// What the highest effective Positioning multiplies a player's weight for a shot or a header by, in basis
    /// points; the edge rises linearly from the floor to this (`engine-v10`).
    /// </summary>
    public int PositioningCeilingBasisPoints { get; init; } = 13_000;

    /// <summary>
    /// How far from the nearest defender, in pitch units, a receiver is wholly free: at this distance and more
    /// his openness is certain, and it falls in a straight line to nothing at no distance (`engine-v10`).
    /// </summary>
    public int OffBallOpennessFullDistance { get; init; } = 1_200;

    /// <summary>
    /// How far a receiver of neutral Positioning can get to a pass, in pitch units; a better-placed player
    /// covers more and a worse one less (`engine-v10`).
    /// </summary>
    public int OffBallReachDistance { get; init; } = 4_600;

    /// <summary>
    /// How far up the pitch a pass must take the ball, in pitch units, to be worth the whole progress score
    /// (`engine-v10`).
    /// </summary>
    public int OffBallProgressFullGain { get; init; } = 2_000;

    /// <summary>
    /// How close the nearest defender is, in pitch units, once his Marking and Positioning are counted, for the
    /// player on the ball to be under pressure (`engine-v10`).
    /// </summary>
    public int OffBallPressureDistance { get; init; } = 600;

    /// <summary>
    /// How far behind the player on the ball a teammate may receive it, in pitch units, whoever he is: a short
    /// ball back or across (`engine-v10`).
    /// </summary>
    public int BackPassFreeDepth { get; init; } = 500;

    /// <summary>
    /// How far behind the player on the ball a midfielder or attacker may receive it, in pitch units, when the
    /// holder is under pressure; nobody is given the ball further back than this (`engine-v10`).
    /// </summary>
    public int BackPassMaxDepth { get; init; } = 2_500;

    /// <summary>
    /// How far up the pitch, on the side's own scale, the player on the ball can be for a defender to be given
    /// it: past this the attack is in the other half, and a defender is no longer a receiver (`engine-v10`).
    /// </summary>
    public int DefenderReceiveMaxHolderX { get; init; } = 4_500;

    /// <summary>
    /// How far up the pitch, on the side's own scale, a defender may take a ball: past this he is not a receiver
    /// however far back the holder was, so a long ball from the back is not played to a centre half beyond the halfway
    /// line (`engine-v10`).
    /// </summary>
    public int DefenderReceiveMaxPointX { get; init; } = 5_000;

    /// <summary>
    /// The chance, in basis points, that a holder of the lowest effective Vision sees a teammate standing next to
    /// him as a way to play the ball (`engine-v10`).
    /// </summary>
    public int ReceiverSeeLowestBasisPoints { get; init; } = 4_500;

    /// <summary>
    /// The chance, in basis points, that a holder of the highest effective Vision sees a teammate standing next to
    /// him; it rises linearly from the lowest (`engine-v10`).
    /// </summary>
    public int ReceiverSeeHighestBasisPoints { get; init; } = 9_900;

    /// <summary>
    /// How far off a teammate is, in pitch units, when the chance of seeing him has fallen by the whole of
    /// <see cref="ReceiverSeeDistancePenaltyBasisPoints"/>; it falls in a straight line from nothing at no
    /// distance (`engine-v10`).
    /// </summary>
    public int ReceiverSeeFullDistance { get; init; } = 6_000;

    /// <summary>
    /// The share of the chance of seeing a teammate that is lost by the time he is
    /// <see cref="ReceiverSeeFullDistance"/> away, in basis points (`engine-v10`).
    /// </summary>
    public int ReceiverSeeDistancePenaltyBasisPoints { get; init; } = 3_500;

    /// <summary>
    /// How far a planned touch moves towards the spot of the player who is given the ball, in basis points of
    /// the distance between them; the last touch of an approach never moves (`engine-v10`).
    /// </summary>
    public int ReceiverPullBasisPoints { get; init; } = 2_500;

    /// <summary>Weight of how open the receiver is, in the score the holder gives each teammate he sees (`engine-v10`).</summary>
    public int ReceiverOpennessWeight { get; init; } = 4;

    /// <summary>Weight of how far the pass takes the attack up the pitch, in a receiver's score (`engine-v10`).</summary>
    public int ReceiverProgressWeight { get; init; } = 3;

    /// <summary>Weight of how easily the receiver gets to the ball, in a receiver's score (`engine-v10`).</summary>
    public int ReceiverReachWeight { get; init; } = 2;

    /// <summary>
    /// Weight of how well the receiver's lane fits the side's pass focus, in a receiver's score; it is how a
    /// manager's lanes still steer the ball once a player is chosen (`engine-v10`).
    /// </summary>
    public int ReceiverLaneWeight { get; init; } = 2;

    /// <summary>
    /// How sharply a holder of the lowest effective Decisions favours the best-placed teammate: a gain on the
    /// squared score, so a low gain is nearly a flat draw (`engine-v10`).
    /// </summary>
    public int ReceiverChoiceGainLowest { get; init; } = 2;

    /// <summary>
    /// How sharply a holder of the highest effective Decisions favours the best-placed teammate; the gain rises
    /// linearly from the lowest (`engine-v10`).
    /// </summary>
    public int ReceiverChoiceGainHighest { get; init; } = 24;

    /// <summary>
    /// How far, in basis points, the weakest pass of an approach moves the chance the attack progresses, when its
    /// receiver was as open as could be or as marked as could be (`engine-v10`).
    /// </summary>
    public int ChainProgressSwingBasisPoints { get; init; } = 700;

    /// <summary>
    /// The openness of the weakest pass of an approach, 0…10,000, at which the chain moves neither the chance the
    /// attack progresses nor the chance it creates one: the mean of the passes the engine plays (`engine-v10`).
    /// </summary>
    public int ChainWeakestOpennessReference { get; init; } = 5_200;

    /// <summary>
    /// How far, in basis points, the openness of the player the approach ends with moves the chance it creates a
    /// shot (`engine-v10`).
    /// </summary>
    public int ChainCreationOpennessSwingBasisPoints { get; init; } = 600;

    /// <summary>
    /// The openness of the player the approach ends with, 0…10,000, at which he moves nothing: the mean of the
    /// approaches the engine plays (`engine-v10`).
    /// </summary>
    public int ChainFinalOpennessReference { get; init; } = 6_900;

    /// <summary>
    /// How far, in basis points, the quality of the holders' choices along the approach moves the chance it creates
    /// a shot (`engine-v10`).
    /// </summary>
    public int ChainCreationChoiceSwingBasisPoints { get; init; } = 500;

    /// <summary>
    /// The mean score of the receivers the holders chose along an approach, 0…10,000, at which the choices move
    /// nothing: the mean of the approaches the engine plays (`engine-v10`).
    /// </summary>
    public int ChainChoiceReference { get; init; } = 6_200;

    /// <summary>
    /// What the player the approach ends with multiplies his weight for the shot by, in basis points on top of his
    /// skill and his Positioning edge: he has the ball, so he is the likeliest to shoot (`engine-v10`).
    /// </summary>
    public int ShooterChainBonusBasisPoints { get; init; } = 25_000;

    /// <summary>
    /// The attribute point a crosser's Crossing is measured from, to see how far his delivery is above or below
    /// the common standard (`engine-v10`).
    /// </summary>
    public int CrossHeaderDeliveryBaseline { get; init; } = 13;

    /// <summary>
    /// How much a crosser's delivery, above or below <see cref="CrossHeaderDeliveryBaseline"/>, adds to or takes
    /// from the aerial score of the attacker who goes for the ball (`engine-v10`).
    /// </summary>
    public int CrossHeaderDeliveryAerialWeight { get; init; } = 3;

    /// <summary>
    /// What an attacker going for an open-play cross is given in the aerial duel, in hundredths of an attribute
    /// point times the duel's weights: a cross is played to where the attackers are, and a defender clearing it has
    /// less time than one who has watched a corner be set up (`engine-v10`).
    /// </summary>
    public int CrossHeaderAttackerBonus { get; init; } = 13_000;

    /// <summary>
    /// The share of a player's weight for being the one at the ball that he keeps when the formation puts him out of
    /// reach of it, in basis points; at the ball he keeps all of it. It picks the player who goes up for an open-play
    /// cross and the one who has the ball when a possession begins (`engine-v10`).
    /// </summary>
    public int ReachWeightFloorBasisPoints { get; init; } = 500;

    /// <summary>
    /// How far, in basis points, a shooter's Finishing moves the chance his shot scores, at
    /// <see cref="ShotContestReference"/> attribute points above or below <see cref="FinishingGoalReference"/>; it is
    /// added to the contest of the shot's own skill against the goalkeeper, for headers too (`engine-v10`).
    /// </summary>
    public int FinishingGoalSwingBasisPoints { get; init; } = 3_500;

    /// <summary>
    /// The Finishing, in attribute points, at which a shooter moves the chance his shot scores neither way
    /// (`engine-v10`).
    /// </summary>
    public int FinishingGoalReference { get; init; } = 11;

    /// <summary>
    /// What a move that ended in a cross multiplies its chance of creating a shot by, in basis points: a cross is
    /// played into the box more readily than a ground ball is, and the header that follows decides how many of them
    /// are chances, so the shots from crosses stay what they were before the header was contested (`engine-v10`).
    /// </summary>
    public int CrossCreationMultiplierBasisPoints { get; init; } = 12_300;

    /// <summary>
    /// The chance, in basis points, that a holder of the lowest effective Decisions takes the option that is worth
    /// most to him (pass, dribble or shoot); otherwise he takes another (`engine-v10`).
    /// </summary>
    public int SoloBestChoiceLowestBasisPoints { get; init; } = 9_700;

    /// <summary>
    /// The chance, in basis points, that a holder of the highest effective Decisions takes the option that is worth
    /// most to him; it rises linearly from the lowest (`engine-v10`).
    /// </summary>
    public int SoloBestChoiceHighestBasisPoints { get; init; } = 9_980;

    /// <summary>
    /// What a dribble is worth against a pass, in basis points of the 0…10,000 score it is read on: below 10,000 the
    /// holder passes unless his teammates are poorly placed (`engine-v10`).
    /// </summary>
    public int SoloDribbleUtilityBasisPoints { get; init; } = 4_200;

    /// <summary>Weight of the holder's Dribbling in what a dribble is worth (`engine-v10`).</summary>
    public int SoloDribbleSkillWeight { get; init; } = 2;

    /// <summary>Weight of the space ahead of the holder in what a dribble is worth (`engine-v10`).</summary>
    public int SoloDribbleSpaceWeight { get; init; } = 3;

    /// <summary>
    /// How far ahead of the holder, in pitch units, the space is read for a dribble: how open the point he would run
    /// into is (`engine-v10`).
    /// </summary>
    public int SoloDribbleStep { get; init; } = 800;

    /// <summary>
    /// How far up the pitch, on the side's own scale, the player on the ball must be for a shot from distance to be an
    /// option when the attack is played into the final third: nearer the goal than this he may shoot (`engine-v10`).
    /// </summary>
    public int LongShotMinX { get; init; } = 7_800;

    /// <summary>
    /// How far up the pitch, on the side's own scale, the player on the ball can be for a shot from distance to be an
    /// option: from the edge of the box in, the shot is the chance the attack creates, not one the holder takes on
    /// himself (`engine-v10`).
    /// </summary>
    public int LongShotMaxX { get; init; } = 8_700;

    /// <summary>
    /// What a shot from distance is worth against a pass, in basis points of the 0…10,000 score it is read on
    /// (`engine-v10`).
    /// </summary>
    public int LongShotUtilityBasisPoints { get; init; } = 5_600;

    /// <summary>Weight of the holder's Finishing and Composure in what a shot from distance is worth (`engine-v10`).</summary>
    public int LongShotSkillWeight { get; init; } = 3;

    /// <summary>Weight of how near the goal he is in what a shot from distance is worth (`engine-v10`).</summary>
    public int LongShotRangeWeight { get; init; } = 2;

    /// <summary>
    /// What a shot from distance multiplies the chance that it scores by, in basis points, after the shooter, the zone
    /// and the goalkeeper have set it: a shot from outside the box is a worse chance than one the attack worked for
    /// (`engine-v10`).
    /// </summary>
    public int LongShotGoalMultiplierBasisPoints { get; init; } = 6_000;

    /// <summary>Weight of Pace in both sides' scramble score.</summary>
    public int ScramblePaceWeight { get; init; } = 3;

    /// <summary>Weight of Acceleration in both sides' scramble score.</summary>
    public int ScrambleAccelerationWeight { get; init; } = 3;

    /// <summary>Weight of Work rate in both sides' scramble score.</summary>
    public int ScrambleWorkRateWeight { get; init; } = 2;

    /// <summary>How likely a defender is to be the one in the ground duel, by band: a defender's weight.</summary>
    public int DuelTacklerDefenceWeight { get; init; } = 4;

    /// <summary>How likely a defender is to be the one in the ground duel, by band: a midfielder's weight.</summary>
    public int DuelTacklerMidfieldWeight { get; init; } = 3;

    /// <summary>How likely a defender is to be the one in the ground duel, by band: an attacker's weight.</summary>
    public int DuelTacklerAttackWeight { get; init; } = 1;

    /// <summary>How likely a carrier is to be the one in the ground duel, by band: a defender's weight.</summary>
    public int DuelCarrierDefenceWeight { get; init; } = 1;

    /// <summary>How likely a carrier is to be the one in the ground duel, by band: a midfielder's weight.</summary>
    public int DuelCarrierMidfieldWeight { get; init; } = 3;

    /// <summary>How likely a carrier is to be the one in the ground duel, by band: an attacker's weight.</summary>
    public int DuelCarrierAttackWeight { get; init; } = 4;

    /// <summary>What each missing player multiplies a player's duel skills by, a milder cost than the ratings'.</summary>
    public int DuelShortHandedPenaltyBasisPoints { get; init; } = 9_800;

    /// <summary>The chance a foul committed in a ground duel, at the edge of the box, is a penalty.</summary>
    public int DuelFoulPenaltyBasisPoints { get; init; } = 300;

    /// <summary>The chance a lost ground duel becomes a foul by the defender.</summary>
    public int DuelFoulBasisPoints { get; init; } = 1_200;

    /// <summary>What aggressive tackling multiplies the duel's foul chance by.</summary>
    public int AggressiveTacklingDuelFoulMultiplierBasisPoints { get; init; } = 16_000;

    /// <summary>What staying on your feet multiplies the duel's foul chance by.</summary>
    public int StayOnFeetDuelFoulMultiplierBasisPoints { get; init; } = 6_000;

    /// <summary>What each man short multiplies the side's condition loss by, as covering teammates tire (Stage 2).</summary>
    public int ShorthandedConditionLossMultiplierBasisPoints { get; init; } = 12_500;

    // ---- Passage progression (engine-v4) ---------------------------------------------------------

    /// <summary>The fewest touches a possession's passage is built from.</summary>
    public int MinPassageTouches { get; init; } = 3;

    /// <summary>The most touches a possession's passage is built from.</summary>
    public int MaxPassageTouches { get; init; } = 8;

    /// <summary>How far the ball is advanced, at least, by one touch, of the distance to the far goal.</summary>
    public int MinTouchAdvanceBasisPoints { get; init; } = 350;

    /// <summary>How far the ball is advanced, at most, by one touch.</summary>
    public int MaxTouchAdvanceBasisPoints { get; init; } = 1_700;

    /// <summary>How far a touch may drift across the pitch, either way, of the pitch's width.</summary>
    public int MaxTouchLateralDriftBasisPoints { get; init; } = 1_600;

    /// <summary>How far up the pitch a defended possession's pressure point is, at least (`engine-v4`).</summary>
    /// <remarks>
    /// The pressure point is where the defending side engages and where a foul, when there is one, is
    /// committed. Its band is what gives fouls a realistic spread across the middle and attacking thirds, so
    /// a direct free kick in range is a genuine possibility rather than unreachable code
    /// (<see cref="FreeKickShootingRangeX"/>).
    /// </remarks>
    public int PressurePointXMinBasisPoints { get; init; } = 4_200;

    /// <summary>How far up the pitch a defended possession's pressure point is, at most.</summary>
    public int PressurePointXMaxBasisPoints { get; init; } = 8_800;

    /// <summary>How far up the pitch an open-play shot is taken from, at least, on the attacker's own scale.</summary>
    public int ShotFinalThirdXMinBasisPoints { get; init; } = 8_300;

    /// <summary>How far up the pitch an open-play shot is taken from, at most.</summary>
    public int ShotFinalThirdXMaxBasisPoints { get; init; } = 9_700;

    /// <summary>The low edge of the central shooting band across the pitch.</summary>
    public int ShotCentralBandYMinBasisPoints { get; init; } = 3_050;

    /// <summary>The high edge of the central shooting band.</summary>
    public int ShotCentralBandYMaxBasisPoints { get; init; } = 3_950;

    /// <summary>The low edge of an inside-channel shooting band.</summary>
    public int ShotInsideBandYMinBasisPoints { get; init; } = 1_700;

    /// <summary>The high edge of an inside-channel shooting band.</summary>
    public int ShotInsideBandYMaxBasisPoints { get; init; } = 5_300;

    /// <summary>The low edge of a wide shooting band.</summary>
    public int ShotWideBandYMinBasisPoints { get; init; } = 500;

    /// <summary>The high edge of a wide shooting band.</summary>
    public int ShotWideBandYMaxBasisPoints { get; init; } = 6_500;

    /// <summary>How far up the pitch an offside is given, on the attacker's own scale.</summary>
    public int OffsideLineXBasisPoints { get; init; } = 7_400;

    /// <summary>How far up the pitch a plain turnover leaves the ball.</summary>
    public int TurnoverMiddleThirdXBasisPoints { get; init; } = 4_800;

    /// <summary>How far up the pitch a side restarts from after a goal kick or a keeper claim.</summary>
    public int GoalAreaXBasisPoints { get; init; } = 1_200;

    /// <summary>
    /// The share of a possession's final approach that is crossed rather than passed, when the ball arrives in a
    /// flank lane (`engine-v9`).
    /// </summary>
    public int CrossShareFlankLaneBasisPoints { get; init; } = 3_850;

    /// <summary>
    /// The share of a possession's final approach that is crossed rather than passed, when the ball arrives in
    /// the centre lane (`engine-v9`).
    /// </summary>
    public int CrossShareCentreLaneBasisPoints { get; init; } = 700;

    // A side with no preference, or both wings, crosses by the two shares above. A side that favours the centre
    // crosses from the middle as well, because that is the lane it plays in: the shares below are set so the
    // crosses measure about 24/52/24 (centre) and 53/30/15 (centre and left), left, centre, right.

    /// <summary>The share of approaches crossed from a flank lane when the focus is the centre alone (`engine-v9`).</summary>
    public int CrossFocusCentreFlankBasisPoints { get; init; } = 2_080;

    /// <summary>The share of approaches crossed from the centre lane when the focus is the centre alone (`engine-v9`).</summary>
    public int CrossFocusCentreCentreBasisPoints { get; init; } = 3_380;

    /// <summary>The share crossed from the favoured flank when the focus is the centre and a flank (`engine-v9`).</summary>
    public int CrossFocusPairFlankBasisPoints { get; init; } = 3_710;

    /// <summary>The share crossed from the centre lane when the focus is the centre and a flank (`engine-v9`).</summary>
    public int CrossFocusPairCentreBasisPoints { get; init; } = 3_275;

    /// <summary>The share crossed from the other flank when the focus is the centre and a flank (`engine-v9`).</summary>
    public int CrossFocusPairOtherFlankBasisPoints { get; init; } = 1_410;

    /// <summary>The ball's altitude at a cross, 0…100.</summary>
    public int CrossAltitude { get; init; } = 70;

    /// <summary>
    /// The ball's altitude at a shot, 0…100, which is also the height of the crossbar (`engine-v5`).
    /// </summary>
    /// <remarks>
    /// A strike that arrives at the goal line below this altitude and between the posts is on target; one that
    /// arrives above it is over the bar.
    /// </remarks>
    public int ShotAltitude { get; init; } = 30;

    /// <summary>The ball's altitude at a header, 0…100.</summary>
    public int HeaderAltitude { get; init; } = 80;

    /// <summary>The ball's altitude at a clearance, 0…100.</summary>
    public int ClearanceAltitude { get; init; } = 55;

    /// <summary>The chance a foul in the attacking half becomes a direct free kick rather than a quick restart.</summary>
    public int FreeKickAwardBasisPoints { get; init; } = 4_500;

    /// <summary>The chance a free kick within shooting range is struck directly at goal.</summary>
    public int FreeKickAttemptBasisPoints { get; init; } = 3_000;

    /// <summary>The baseline goal probability of a direct free kick.</summary>
    public int FreeKickGoalBasisPoints { get; init; } = 900;

    /// <summary>How far a maximal set-piece-versus-goalkeeping differential moves the free kick's goal chance.</summary>
    public int FreeKickQualitySwingBasisPoints { get; init; } = 1_400;

    /// <summary>The share of non-goal free kicks that are saved.</summary>
    public int FreeKickSavedShareBasisPoints { get; init; } = 4_500;

    /// <summary>The share of non-goal free kicks that are blocked by the wall.</summary>
    public int FreeKickBlockedShareBasisPoints { get; init; } = 2_500;

    /// <summary>The share of non-goal free kicks that hit the woodwork.</summary>
    public int FreeKickWoodworkShareBasisPoints { get; init; } = 800;

    /// <summary>The distance from the halfway line beyond which a free kick is in shooting range.</summary>
    /// <remarks>
    /// A spatial threshold rather than a probability: it is the X coordinate, on the attacking half's own
    /// scale, past which the wall and the angle make a direct strike worth attempting.
    /// </remarks>
    public int FreeKickShootingRangeX { get; init; } = 6_500;

    // ---- Restarts and strikes (engine-v5) --------------------------------------------------------

    /// <summary>
    /// How far along its approach, at least, a possession that loses its opening scramble is cut, as a share
    /// of the approach's length (`engine-v5`).
    /// </summary>
    /// <remarks>
    /// The geometry constants below shape where the ball is when something ends, so they are rules: a
    /// possession that loses the ball early leaves it somewhere different from one that loses it late, and
    /// where the ball is decides how deep the next possession starts.
    /// </remarks>
    public int ScrambleCutMinBasisPoints { get; init; } = 500;

    /// <summary>How far along its approach, at most, a lost scramble is cut.</summary>
    public int ScrambleCutMaxBasisPoints { get; init; } = 2_500;

    /// <summary>How far along its approach, at least, a failed progression is cut.</summary>
    public int ProgressionCutMinBasisPoints { get; init; } = 4_000;

    /// <summary>How far along its approach, at most, a failed progression is cut.</summary>
    public int ProgressionCutMaxBasisPoints { get; init; } = 8_000;

    /// <summary>
    /// How far from the pressure point to the shot point, at least, the ball enters the final third
    /// (`engine-v5`).
    /// </summary>
    public int EntryFractionMinBasisPoints { get; init; } = 3_000;

    /// <summary>How far from the pressure point to the shot point, at most, the ball enters the final third.</summary>
    public int EntryFractionMaxBasisPoints { get; init; } = 7_000;

    /// <summary>The least far up the pitch an open-play entry into the final third is, on the attacker's scale.</summary>
    public int FinalThirdEntryXBasisPoints { get; init; } = 6_700;

    /// <summary>
    /// The shortest clearance worth recording (`engine-v5`): a ball that would be cleared less far than this
    /// is left where it was won, and the film shows no kick.
    /// </summary>
    public int MinClearanceDistanceBasisPoints { get; init; } = 600;

    /// <summary>How far up the pitch the penalty area band begins, on the attacker's scale.</summary>
    public int BoxXMinBasisPoints { get; init; } = 8_700;

    /// <summary>How far up the pitch the penalty area band ends, on the attacker's scale.</summary>
    public int BoxXMaxBasisPoints { get; init; } = 9_400;

    /// <summary>The low edge of the penalty area band across the pitch.</summary>
    public int BoxYMinBasisPoints { get; init; } = 2_400;

    /// <summary>The high edge of the penalty area band across the pitch.</summary>
    public int BoxYMaxBasisPoints { get; init; } = 4_600;

    /// <summary>How far from the corner flag along the goal line, at least, a ball goes out for a corner.</summary>
    public int CornerOutOfPlayMinBasisPoints { get; init; } = 300;

    /// <summary>How far from the corner flag along the goal line, at most, a ball goes out for a corner.</summary>
    public int CornerOutOfPlayMaxBasisPoints { get; init; } = 1_500;

    /// <summary>How far in front of the goal line, at least, the goalkeeper is when he gets to a shot.</summary>
    public int SaveDepthMinBasisPoints { get; init; } = 120;

    /// <summary>How far in front of the goal line, at most, the goalkeeper is when he gets to a shot.</summary>
    public int SaveDepthMaxBasisPoints { get; init; } = 450;

    /// <summary>How far beyond a post, at least, a wide miss crosses the goal line.</summary>
    public int MissWideMinBasisPoints { get; init; } = 150;

    /// <summary>How far beyond a post, at most, a wide miss crosses the goal line.</summary>
    public int MissWideMaxBasisPoints { get; init; } = 1_250;

    /// <summary>The share of off-target strikes that go over the bar rather than wide of a post.</summary>
    public int MissOverShareBasisPoints { get; init; } = 3_500;

    /// <summary>The share of woodwork hits that strike a post rather than the crossbar.</summary>
    public int PostShareOfWoodworkBasisPoints { get; init; } = 7_000;

    /// <summary>
    /// The share of missed penalties the goalkeeper is shown stopping, rather than the taker putting wide.
    /// </summary>
    /// <remarks>
    /// Presentation only: the event is <c>PenaltyMissed</c> either way and the defending side restarts from
    /// its goal area either way, so the choice cannot move a result.
    /// </remarks>
    public int PenaltySavedShareBasisPoints { get; init; } = 6_000;

    /// <summary>How far in front of the shooter, at least, a blocked shot is stopped (about two metres).</summary>
    public int BlockDistanceMinBasisPoints { get; init; } = 190;

    /// <summary>How far in front of the shooter, at most, a blocked shot is stopped (about six metres).</summary>
    public int BlockDistanceMaxBasisPoints { get; init; } = 570;

    /// <summary>How far out from the goal line, at least, a ball rebounds from the woodwork.</summary>
    public int ReboundDistanceMinBasisPoints { get; init; } = 600;

    /// <summary>How far out from the goal line, at most, a ball rebounds from the woodwork.</summary>
    public int ReboundDistanceMaxBasisPoints { get; init; } = 1_400;

    /// <summary>The highest altitude a strike that is under the bar arrives at, 0…100.</summary>
    public int StrikeLowAltitudeMax { get; init; } = 18;

    /// <summary>The highest altitude a strike that goes over the bar arrives at, 0…100.</summary>
    public int OverBarAltitudeMax { get; init; } = 75;

    // ---- Discipline ------------------------------------------------------------------------------

    /// <summary>The baseline chance a possession contains a foul by the defending side.</summary>
    public int BaseFoulBasisPoints { get; init; } = 780;

    /// <summary>What aggressive tackling multiplies the foul chance by.</summary>
    public int AggressiveTacklingFoulMultiplierBasisPoints { get; init; } = 13_500;

    /// <summary>What staying on your feet multiplies the foul chance by.</summary>
    public int StayOnFeetFoulMultiplierBasisPoints { get; init; } = 8_200;

    /// <summary>The chance a foul is booked.</summary>
    public int YellowCardPerFoulBasisPoints { get; init; } = 1_600;

    /// <summary>The chance a foul is a straight red.</summary>
    public int StraightRedPerFoulBasisPoints { get; init; } = 20;

    /// <summary>How much an aggressive side's bookings rise.</summary>
    public int AggressiveTacklingCardMultiplierBasisPoints { get; init; } = 12_500;

    /// <summary>The attribute a side's Aggression and Tackling are measured against when setting its foul rate.</summary>
    public int FoulSkillReference { get; init; } = 13;

    /// <summary>How far each point of Aggression above the reference raises a foul chance.</summary>
    public int AggressionFoulStepBasisPoints { get; init; } = 100;

    /// <summary>How far each point of Tackling above the reference lowers a foul chance.</summary>
    public int TacklingFoulStepBasisPoints { get; init; } = 100;

    /// <summary>The most Aggression and Tackling can move a foul chance, either way.</summary>
    public int MaxFoulSkillAdjustBasisPoints { get; init; } = 1_500;

    /// <summary>What a side that is wasting time multiplies the length of its possessions by.</summary>
    public int TimeWastingPossessionSecondsMultiplierBasisPoints { get; init; } = 12_500;

    // ---- The counter-attack (engine-v11) --------------------------------------------------------

    /// <summary>
    /// How often a ball a side wins back from play becomes a counter-attack when the side has not asked to play on
    /// the counter.
    /// </summary>
    public int CounterStartBasisPoints { get; init; } = 2_000;

    /// <summary>How often a regained ball becomes a counter-attack for a side that plays on the counter.</summary>
    public int CounterStartWithInstructionBasisPoints { get; init; } = 5_000;

    /// <summary>
    /// What a counter-attack adds to the chance it progresses out of build-up against a balanced opponent: a side that
    /// has just won the ball finds the other side out of shape.
    /// </summary>
    public int CounterProgressBasisPoints { get; init; } = 200;

    /// <summary>
    /// How far each step of the opponent's attacking posture moves the progression chance of a counter-attack: an
    /// opponent who has committed forward leaves space to run into, and one who sits deep is well placed to cut out
    /// a long ball, so the counter breaks down more often against it.
    /// </summary>
    public int CounterProgressPerPostureBasisPoints { get; init; } = 1_150;

    /// <summary>What a counter-attack adds to its creation chance against a balanced opponent.</summary>
    public int CounterCreationBasisPoints { get; init; } = 150;

    /// <summary>How far each step of the opponent's attacking posture moves the creation chance of a counter-attack.</summary>
    public int CounterCreationPerPostureBasisPoints { get; init; } = 1_050;

    /// <summary>How much a side that plays on the counter loses in build-up, which is its patience in possession.</summary>
    public int CounterAttackBuildUpCostBasisPoints { get; init; } = 120;

    /// <summary>How much a side that plays on the counter loses in defensive shape, with players left forward.</summary>
    public int CounterAttackShapeCostBasisPoints { get; init; } = 60;

    /// <summary>
    /// The pace and acceleration, as an attribute point, that a side's defenders and midfielders are measured against when
    /// they race back to meet a counter-attack: above it they cut the counter down, below it they are caught out.
    /// </summary>
    public int CounterRecoveryReference { get; init; } = 11;

    /// <summary>How far each point of the defenders' and midfielders' pace and acceleration above the reference lowers a counter's progression chance.</summary>
    public int CounterRecoveryProgressStepBasisPoints { get; init; } = 150;

    /// <summary>How far each point of pace and acceleration above the reference lowers a counter's creation chance.</summary>
    public int CounterRecoveryCreationStepBasisPoints { get; init; } = 120;

    /// <summary>The most the defenders' recovery can move a counter's progression chance, either way.</summary>
    public int CounterRecoveryMaxProgressBasisPoints { get; init; } = 900;

    /// <summary>The most the defenders' recovery can move a counter's creation chance, either way.</summary>
    public int CounterRecoveryMaxCreationBasisPoints { get; init; } = 700;

    /// <summary>
    /// How much likelier a side that sits deep is to counter in its turn, per step of its own caution, when it wins the
    /// ball back from a counter-attack that failed.
    /// </summary>
    public int CounterBackfireStartBasisPoints { get; init; } = 2_000;

    /// <summary>
    /// What the chance that follows a failed counter-attack gains, per step of caution of the side that won the ball
    /// back: it has the other side stretched.
    /// </summary>
    public int CounterBackfireCreationBasisPoints { get; init; } = 1_500;

    // ---- Fitness ---------------------------------------------------------------------------------

    /// <summary>
    /// Condition lost per possession at a normal tempo and a normal block, in basis points.
    /// </summary>
    /// <remarks>
    /// Calibrated so a player who stays on ends the match around 55–60% condition and the substitution
    /// planner has somebody to replace. Half-time recovery gives back about half of the first half's load, so
    /// this value and <see cref="HalfTimeConditionRecoveryBasisPoints"/> have to be tuned together — a
    /// recovery that undoes a whole half leaves a side that never tires and a bench that is never used.
    /// </remarks>
    public int ConditionLossPerPossessionBasisPoints { get; init; } = 18;

    /// <summary>What a high tempo multiplies condition loss by.</summary>
    public int HighTempoConditionLossMultiplierBasisPoints { get; init; } = 12_000;

    /// <summary>What a low tempo multiplies condition loss by.</summary>
    public int LowTempoConditionLossMultiplierBasisPoints { get; init; } = 8_500;

    /// <summary>What a high press multiplies condition loss by.</summary>
    public int HighPressConditionLossMultiplierBasisPoints { get; init; } = 11_500;

    /// <summary>What a low block multiplies condition loss by.</summary>
    public int LowBlockConditionLossMultiplierBasisPoints { get; init; } = 8_000;

    /// <summary>
    /// The most a player's physical skills fall at zero condition, in basis points (`engine-v6`).
    /// </summary>
    /// <remarks>
    /// A tired player plays below his sheet, and the drop grows linearly with the condition he has lost: none
    /// when fresh, this much at empty. Physical skills fall furthest, then technical, then mental. Goalkeepers
    /// are exempt.
    /// </remarks>
    public int TiredPhysicalDropBasisPoints { get; init; } = 4_000;

    /// <summary>The most a player's technical skills fall at zero condition, in basis points.</summary>
    public int TiredTechnicalDropBasisPoints { get; init; } = 2_000;

    /// <summary>The most a player's mental skills fall at zero condition, in basis points.</summary>
    public int TiredMentalDropBasisPoints { get; init; } = 1_000;

    /// <summary>The Stamina at which a player tires at the normal rate.</summary>
    public int StaminaReference { get; init; } = 13;

    /// <summary>How much each point of Stamina above the reference slows a player's condition loss.</summary>
    public int StaminaConditionLossStepBasisPoints { get; init; } = 350;

    /// <summary>The least a player's condition loss can be multiplied by, at the highest Stamina.</summary>
    public int MinStaminaConditionLossMultiplierBasisPoints { get; init; } = 6_000;

    /// <summary>The most a player's condition loss can be multiplied by, at the lowest Stamina.</summary>
    public int MaxStaminaConditionLossMultiplierBasisPoints { get; init; } = 15_000;

    /// <summary>Fatigue gained per possession at a normal tempo and a normal block, in basis points.</summary>
    public int FatigueGainPerPossessionBasisPoints { get; init; } = 7;

    /// <summary>Condition recovered over half-time, in basis points.</summary>
    public int HalfTimeConditionRecoveryBasisPoints { get; init; } = 900;

    /// <summary>Fatigue shed over half-time, in basis points.</summary>
    public int HalfTimeFatigueRecoveryBasisPoints { get; init; } = 1_200;

    /// <summary>Sharpness gained per possession by a player who is on the pitch, in basis points.</summary>
    public int SharpnessGainPerPossessionBasisPoints { get; init; } = 2;

    /// <summary>How far match morale can drift from its starting value, in basis points.</summary>
    public int MaxMoraleDriftBasisPoints { get; init; } = 600;

    /// <summary>Morale gained per goal by the scoring side, in basis points.</summary>
    public int MoraleGainPerGoalBasisPoints { get; init; } = 90;

    /// <summary>Morale lost per goal by the conceding side, in basis points.</summary>
    public int MoraleLossPerConcededGoalBasisPoints { get; init; } = 70;

    /// <summary>The Leadership of the best leader on the pitch at which morale moves at its normal rate.</summary>
    public int LeadershipReference { get; init; } = 13;

    /// <summary>
    /// How much each point of the best leader's Leadership above the reference softens a morale loss and
    /// sharpens a morale gain.
    /// </summary>
    public int LeadershipMoraleStepBasisPoints { get; init; } = 400;

    /// <summary>The least a morale shift can be scaled by.</summary>
    public int MinLeadershipMoraleMultiplierBasisPoints { get; init; } = 6_000;

    /// <summary>The most a morale shift can be scaled by.</summary>
    public int MaxLeadershipMoraleMultiplierBasisPoints { get; init; } = 14_000;

    // ---- Injuries --------------------------------------------------------------------------------

    /// <summary>The baseline chance per possession that a player is injured, in basis points.</summary>
    public int BaseInjuryPerPossessionBasisPoints { get; init; } = 12;

    /// <summary>What maximal fatigue multiplies the injury chance by at most.</summary>
    public int FatigueInjuryMultiplierBasisPoints { get; init; } = 21_000;

    /// <summary>The most a single injury can add to the injury chance, in basis points.</summary>
    public int MaxInjuryProbabilityBasisPoints { get; init; } = 90;

    /// <summary>The shortest absence an injury causes, in fixtures (DIS-1).</summary>
    public int MinInjuryAbsenceFixtures { get; init; } = 1;

    /// <summary>The longest absence an injury causes, in fixtures (DIS-1).</summary>
    public int MaxInjuryAbsenceFixtures { get; init; } = 6;

    // ---- Substitutions ---------------------------------------------------------------------------

    /// <summary>The most substitutions a side may make (SQ-5).</summary>
    public int MaxSubstitutions { get; init; } = 5;

    /// <summary>The minutes at which the planner considers a change.</summary>
    public IReadOnlyList<int> SubstitutionWindows { get; init; } = [46, 58, 68, 78, 84];

    /// <summary>The condition below which a tiring player becomes a candidate for replacement.</summary>
    public int ConditionSubstitutionThresholdBasisPoints { get; init; } = 6_800;

    /// <summary>
    /// The condition a replacement must exceed the player they would replace by, so a substitution is
    /// only made when it is worth making.
    /// </summary>
    public int MinimumConditionAdvantageBasisPoints { get; init; } = 1_200;

    // ---- Ratings and player state ------------------------------------------------------------------

    /// <summary>
    /// What one attribute point is worth on the rating scale. An attribute of 1 is 50 and one of 20 is
    /// 1000, so a rating is a weighted mean of these values.
    /// </summary>
    public int AttributeRatingFactor { get; init; } = 50;

    /// <summary>What a player at maximum fatigue multiplies their ratings by.</summary>
    public int FatigueFactorFloorBasisPoints { get; init; } = 8_750;

    /// <summary>What a player with no fatigue multiplies their ratings by.</summary>
    public int FatigueFactorCeilingBasisPoints { get; init; } = 10_000;

    /// <summary>What a player at the lowest morale multiplies their ratings by.</summary>
    public int MoraleFactorFloorBasisPoints { get; init; } = 9_500;

    /// <summary>What a player at the highest morale multiplies their ratings by.</summary>
    public int MoraleFactorCeilingBasisPoints { get; init; } = 10_000;

    /// <summary>What a player at zero sharpness multiplies their ratings by.</summary>
    public int SharpnessFactorFloorBasisPoints { get; init; } = 9_600;

    /// <summary>What a player at full sharpness multiplies their ratings by.</summary>
    public int SharpnessFactorCeilingBasisPoints { get; init; } = 10_000;

    /// <summary>The largest a whole unit rating can reach after its modifiers are applied.</summary>
    public int MaxUnitRating { get; init; } = 1_150;

    // ---- Player match rating ---------------------------------------------------------------------

    /// <summary>
    /// What a player's match rating starts at, in basis points, before anything they did is counted.
    /// </summary>
    /// <remarks>
    /// The rating is the engine's own summary of a player's match, produced with the result so a season's
    /// statistics need no second definition of "how well did they play". It is a display value on the
    /// 0–10,000 basis-point scale the API converts to a 0–10.0 figure (`TRN-8`), not a hidden player value,
    /// and every term is derived from a fact the match already records.
    /// </remarks>
    public int RatingBaseBasisPoints { get; init; } = 6_000;

    /// <summary>What a win adds to every player who appeared, weighted by how much they played.</summary>
    public int RatingWinBonusBasisPoints { get; init; } = 600;

    /// <summary>What a draw adds to every player who appeared, weighted by how much they played.</summary>
    public int RatingDrawBonusBasisPoints { get; init; } = 120;

    /// <summary>What a defeat takes from every player who appeared, weighted by how much they played.</summary>
    public int RatingLossPenaltyBasisPoints { get; init; } = 350;

    /// <summary>What each goal a player scored adds.</summary>
    public int RatingGoalBonusBasisPoints { get; init; } = 1_000;

    /// <summary>What each goal a player set up adds.</summary>
    public int RatingAssistBonusBasisPoints { get; init; } = 450;

    /// <summary>What each save a goalkeeper made adds.</summary>
    public int RatingSaveBonusBasisPoints { get; init; } = 60;

    /// <summary>The most the saves a player made can add, so a busy afternoon stays good rather than perfect.</summary>
    public int RatingMaxSaveBonusBasisPoints { get; init; } = 400;

    /// <summary>What each booking takes from a player.</summary>
    public int RatingYellowPenaltyBasisPoints { get; init; } = 350;

    /// <summary>What a sending-off takes from a player.</summary>
    public int RatingRedPenaltyBasisPoints { get; init; } = 1_400;

    /// <summary>The lowest match rating a player who appeared can be given.</summary>
    public int RatingMinBasisPoints { get; init; } = 1_000;

    /// <summary>The highest match rating a player who appeared can be given.</summary>
    public int RatingMaxBasisPoints { get; init; } = 10_000;

    /// <summary>What a player's live, fluctuating match rating starts at, in basis points (`engine-v3`).</summary>
    public int LiveRatingBaseBasisPoints { get; init; } = 6_000;

    /// <summary>The lowest a live rating can reach during a match, as a pinned constant of the scale.</summary>
    public const int MinLiveRatingBasisPoints = 3_000;

    /// <summary>The highest a live rating can reach during a match, as a pinned constant of the scale.</summary>
    public const int MaxLiveRatingBasisPoints = 10_000;

    /// <summary>What a tackle won adds, and what a tackle lost takes.</summary>
    public int LiveRatingTackleBonusBasisPoints { get; init; } = 120;

    /// <summary>What a tackle lost takes from the tackler's live rating.</summary>
    public int LiveRatingTackleLostPenaltyBasisPoints { get; init; } = 80;

    /// <summary>What an aerial duel won adds.</summary>
    public int LiveRatingAerialBonusBasisPoints { get; init; } = 80;

    /// <summary>What an aerial duel lost takes.</summary>
    public int LiveRatingAerialLostPenaltyBasisPoints { get; init; } = 50;

    /// <summary>What a shot on target adds.</summary>
    public int LiveRatingShotBonusBasisPoints { get; init; } = 100;

    /// <summary>What a shot off target takes.</summary>
    public int LiveRatingShotMissPenaltyBasisPoints { get; init; } = 40;

    /// <summary>What a goal adds to the scorer's live rating.</summary>
    public int LiveRatingGoalBonusBasisPoints { get; init; } = 800;

    /// <summary>What an assist adds to the assister's live rating.</summary>
    public int LiveRatingAssistBonusBasisPoints { get; init; } = 450;

    /// <summary>What a save adds to the goalkeeper's live rating.</summary>
    public int LiveRatingSaveBonusBasisPoints { get; init; } = 250;

    /// <summary>What conceding a goal takes from the goalkeeper who faced it.</summary>
    public int LiveRatingGoalConcededPenaltyBasisPoints { get; init; } = 250;

    /// <summary>What a yellow card takes.</summary>
    public int LiveRatingYellowPenaltyBasisPoints { get; init; } = 200;

    /// <summary>What a sending-off takes.</summary>
    public int LiveRatingRedPenaltyBasisPoints { get; init; } = 1_200;

    /// <summary>
    /// The rating difference at which a swing is applied in full, so the probability formulas can express
    /// "how much a difference of this size moves the chance" rather than a raw per-point coefficient.
    /// Roughly the gap between a mid-table side and a good one.
    /// </summary>
    public int RatingDifferentialReference { get; init; } = 950;

    // ---- Tactical bounds -------------------------------------------------------------------------

    /// <summary>
    /// The largest a unit rating may be multiplied by a tactical choice, so attributes stay dominant
    /// (INS-9, master plan §8.4).
    /// </summary>
    public int MaxTacticalModifierBasisPoints { get; init; } = 11_500;

    /// <summary>The smallest a unit rating may be multiplied by a tactical choice.</summary>
    public int MinTacticalModifierBasisPoints { get; init; } = 8_800;

    /// <summary>What an out-of-position player multiplies their ratings by (INS-10).</summary>
    public int OutOfPositionPenaltyBasisPoints { get; init; } = 8_800;

    /// <summary>What a player in a secondary position multiplies their ratings by (INS-10).</summary>
    public int SecondaryPositionPenaltyBasisPoints { get; init; } = 9_600;

    /// <summary>What a player in an unfamiliar role within their own family multiplies their ratings by.</summary>
    public int UnfamiliarRolePenaltyBasisPoints { get; init; } = 9_400;

    /// <summary>
    /// What each player below eleven multiplies the whole side's ratings by, so being a man down costs
    /// more than simply averaging over ten players (master plan §8.5).
    /// </summary>
    /// <remarks>
    /// Deepened by the Stage 7 calibration: at the previous 8_600 a side sent off before the half hour
    /// finished only about two-fifths of a goal worse off, where the plan asks for about one and a quarter.
    /// </remarks>
    public int ShortHandedPenaltyBasisPoints { get; init; } = 6_700;

    // ---- Set-piece and home advantage ------------------------------------------------------------

    /// <summary>Home advantage, applied to the home side's ratings (master plan §8.5). Small and configurable.</summary>
    /// <remarks>
    /// Calibrated with the laboratory to about four win-share points over the neutral venue (Stage 7), on
    /// top of the duel and possession bonuses the crowd contributes separately.
    /// </remarks>
    public int HomeAdvantageBasisPoints { get; init; } = 10_420;

    /// <summary>
    /// Describes every constant canonically, for the configuration hash a result records.
    /// </summary>
    /// <remarks>
    /// Derived from the type by reflection over its own properties and ordered by name rather than
    /// written out by hand. A hand-written list is a list somebody forgets to extend, and the failure
    /// mode is silent: a new constant would simply not be covered by the hash, so two results produced
    /// under genuinely different rules would claim the same provenance. Sorting is ordinal and the
    /// formatting is invariant, so the description is stable across platforms and cultures.
    /// </remarks>
    /// <param name="versionOverride">An optional rule version override (e.g. for legacy rule verification).</param>
    /// <returns>The canonical, name-ordered description of every constant.</returns>
    public IReadOnlyList<string> ToCanonicalParts(string? versionOverride = null)
    {
        var parts = new List<string> { versionOverride ?? Version };

        var properties = GetType()
            .GetProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal);

        foreach (var property in properties)
        {
            // Every property on this type is an int or an int list; nothing else is a rule constant.
            var value = property.GetValue(this);

            parts.Add($"{property.Name}={Describe(value)}");
        }

        return parts;
    }

    private static string Describe(object? value) => value switch
    {
        null => string.Empty,
        IReadOnlyList<int> values => string.Join(',', values),
        int number => number.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>
    /// Throws when a constant is out of the range its own formula assumes.
    /// </summary>
    /// <remarks>
    /// The configuration is validated rather than trusted (master plan §8.5, ADR-0004). A probability
    /// above certainty, a tempo multiplier below one where the formula assumes an increase, or a
    /// substitution window outside the match would each produce a plausible-looking but wrong
    /// simulation, and a wrong simulation is indistinguishable from a balance choice after the fact.
    /// </remarks>
    /// <exception cref="InvalidOperationException">When a constant is outside its permitted range.</exception>
    public void Validate()
    {
        var problems = new List<string>();

        if (RegulationMinutes is < 1 or > 180)
        {
            problems.Add($"RegulationMinutes must be between 1 and 180, was {RegulationMinutes}.");
        }

        if (HalfTimeMinute <= 0 || HalfTimeMinute >= RegulationMinutes)
        {
            problems.Add($"HalfTimeMinute must fall inside regulation time, was {HalfTimeMinute}.");
        }

        if (SecondsPerMinute is < 1 or > 600)
        {
            problems.Add($"SecondsPerMinute must be between 1 and 600, was {SecondsPerMinute}.");
        }

        if (MinStoppageMinutes < 0 || MaxStoppageMinutes < MinStoppageMinutes)
        {
            problems.Add(
                $"Stoppage bounds are inverted: {MinStoppageMinutes}..{MaxStoppageMinutes}.");
        }

        if (PossessionSecondsMin < 1 || PossessionSecondsMax < PossessionSecondsMin)
        {
            problems.Add(
                $"Possession seconds are inverted: {PossessionSecondsMin}..{PossessionSecondsMax}.");
        }

        // Probabilities and multipliers are checked separately, because they are different kinds of
        // constant: a probability is bounded by certainty, and a multiplier is a factor around one. A
        // multiplier run through the probability check would be rejected for exceeding certainty, and a
        // probability run through the multiplier check would be rejected for being below the band — so
        // conflating the two lists would make the validator useless rather than lenient.
        foreach (var (name, value) in ProbabilityConstants())
        {
            if (value is < 0 or > Certain)
            {
                problems.Add($"{name} must be a basis-point probability in 0..{Certain}, was {value}.");
            }
        }

        foreach (var (name, value) in MultiplierConstants())
        {
            if (value is < MinMultiplier or > MaxMultiplier)
            {
                problems.Add(
                    $"{name} must be a basis-point multiplier in {MinMultiplier}..{MaxMultiplier}, was {value}.");
            }
        }

        foreach (var (name, floor, ceiling) in FactorPairs())
        {
            if (floor < MinMultiplier || ceiling > MaxMultiplier || floor > ceiling)
            {
                problems.Add(
                    $"{name} must be an ordered multiplier pair in {MinMultiplier}..{MaxMultiplier}, "
                    + $"were {floor}..{ceiling}.");
            }
        }

        foreach (var (name, value) in RatingConstants())
        {
            if (value is < 0 or > Certain)
            {
                problems.Add($"{name} must be a basis-point amount in 0..{Certain}, was {value}.");
            }
        }

        if (RatingMinBasisPoints > RatingMaxBasisPoints)
        {
            problems.Add(
                $"The rating bounds are inverted: {RatingMinBasisPoints}..{RatingMaxBasisPoints}.");
        }

        if (RatingBaseBasisPoints < RatingMinBasisPoints || RatingBaseBasisPoints > RatingMaxBasisPoints)
        {
            problems.Add(
                $"RatingBaseBasisPoints ({RatingBaseBasisPoints}) must fall between the rating bounds "
                + $"{RatingMinBasisPoints}..{RatingMaxBasisPoints}.");
        }

        if (AttributeRatingFactor < 1)
        {
            problems.Add($"AttributeRatingFactor must be positive, was {AttributeRatingFactor}.");
        }

        // A rating scale that cannot hold a player who is maximum in every attribute would clamp the best
        // player in the world down to an ordinary one, which is a balance bug that looks like a formula.
        if (MaxUnitRating < AttributeRatingFactor * Model.MatchAttributeNames.Max)
        {
            problems.Add(
                $"MaxUnitRating ({MaxUnitRating}) must admit a rating of "
                + $"{AttributeRatingFactor} x {Model.MatchAttributeNames.Max}.");
        }

        if (MinPossessionBasisPoints < 0
            || MaxPossessionBasisPoints > Certain
            || MinPossessionBasisPoints > MaxPossessionBasisPoints)
        {
            problems.Add("Possession bounds must be an ordered pair inside 0..10000.");
        }

        foreach (var (name, min, max) in OrderedTriples())
        {
            if (min < 0 || max > Certain || min > max)
            {
                problems.Add($"{name} bounds must be an ordered pair inside 0..10000, were {min}..{max}.");
            }
        }

        if (!(MinTacticalModifierBasisPoints <= Certain && Certain <= MaxTacticalModifierBasisPoints))
        {
            problems.Add("The tactical modifier bounds must admit 10000 (no modifier).");
        }

        if (ShotContestReference < 1 || DuelDifferentialReference < 1)
        {
            problems.Add("ShotContestReference and DuelDifferentialReference must be positive.");
        }

        if (ShotZoneCentralPercent + (2 * ShotZoneInsidePercent) + (2 * ShotZoneWidePercent) != 100)
        {
            problems.Add(
                "The shot zone shares must sum to 100: one central, two inside channels, two wide zones, "
                + $"were {ShotZoneCentralPercent} + 2 x {ShotZoneInsidePercent} + 2 x {ShotZoneWidePercent}.");
        }

        if (PassLeftLaneMaxYBasisPoints < 1
            || PassLeftLaneMaxYBasisPoints >= PassRightLaneMinYBasisPoints
            || PassRightLaneMinYBasisPoints >= 7_000)
        {
            problems.Add("The pass lanes must be three non-empty bands across a 7,000-wide pitch: left, centre, right.");
        }

        if (PassFocusCentreCentrePercent + (2 * PassFocusCentreFlankPercent) != 100
            || PassFocusPairCentrePercent + PassFocusPairFlankPercent + PassFocusPairOtherFlankPercent != 100
            || PassFocusWingsCentrePercent + (2 * PassFocusWingsFlankPercent) != 100)
        {
            problems.Add("Each pass focus must share its destinations across the three lanes in percentages summing to 100.");
        }

        if (ShotFocusCentreCentralPercent + (2 * (ShotFocusCentreInsidePercent + ShotFocusCentreWidePercent)) != 100
            || ShotFocusPairCentralPercent + ShotFocusPairInsidePercent + ShotFocusPairWidePercent
                + ShotFocusPairOtherInsidePercent + ShotFocusPairOtherWidePercent != 100
            || ShotFocusWingsCentralPercent + (2 * (ShotFocusWingsInsidePercent + ShotFocusWingsWidePercent)) != 100)
        {
            problems.Add("Each pass focus must share its shots across the five shot zones in percentages summing to 100.");
        }

        if (ChanceVolumeCentreBasisPoints is < 5_000 or > 15_000
            || ChanceVolumePairBasisPoints is < 5_000 or > 15_000
            || ChanceVolumeWingsBasisPoints is < 5_000 or > 15_000)
        {
            problems.Add("The pass focus chance volumes must lie between 5000 and 15000 basis points.");
        }

        if (MinLeadershipMoraleMultiplierBasisPoints > MaxLeadershipMoraleMultiplierBasisPoints
            || MinStaminaConditionLossMultiplierBasisPoints > MaxStaminaConditionLossMultiplierBasisPoints)
        {
            problems.Add("The leadership and stamina multiplier bounds must be ordered pairs.");
        }

        if (MaxSubstitutions < 0 || MaxSubstitutions > 11)
        {
            problems.Add($"MaxSubstitutions must be between 0 and 11, was {MaxSubstitutions}.");
        }

        if (GameStateMarginThresholdGoals is < 0 or > 6)
        {
            problems.Add(
                $"GameStateMarginThresholdGoals must be between 0 and 6, was {GameStateMarginThresholdGoals}.");
        }

        if (SubstitutionWindows.Count == 0)
        {
            problems.Add("At least one substitution window is required.");
        }

        foreach (var window in SubstitutionWindows)
        {
            if (window <= 0 || window > RegulationMinutes)
            {
                problems.Add($"Substitution window {window} falls outside regulation time.");
            }
        }

        if (MinInjuryAbsenceFixtures < 1 || MaxInjuryAbsenceFixtures < MinInjuryAbsenceFixtures)
        {
            problems.Add(
                $"Injury absence bounds are invalid: {MinInjuryAbsenceFixtures}..{MaxInjuryAbsenceFixtures}.");
        }

        // The passage geometry has its own shape: a bounded number of touches between the anchors, a
        // pressure band that can reach the free-kick range, a shot band wholly inside the final third, and
        // altitudes that stay on the ball's 0..100 scale. Each of these would otherwise produce a ball that
        // teleports, a shot from the halfway line, or a waypoint above the sky.
        if (MinPassageTouches < 1 || MaxPassageTouches < MinPassageTouches || MaxPassageTouches > 32)
        {
            problems.Add(
                $"Passage touch bounds are invalid: {MinPassageTouches}..{MaxPassageTouches}.");
        }

        if (MinTouchAdvanceBasisPoints < 1)
        {
            problems.Add(
                $"MinTouchAdvanceBasisPoints must be positive, was {MinTouchAdvanceBasisPoints}.");
        }

        if (MaxTouchLateralDriftBasisPoints is < 0 or > Certain)
        {
            problems.Add(
                $"MaxTouchLateralDriftBasisPoints must be in 0..{Certain}, was {MaxTouchLateralDriftBasisPoints}.");
        }

        foreach (var (name, value) in new[]
                 {
                     (nameof(OffBallOpennessFullDistance), OffBallOpennessFullDistance),
                     (nameof(OffBallReachDistance), OffBallReachDistance),
                     (nameof(OffBallProgressFullGain), OffBallProgressFullGain),
                     (nameof(OffBallPressureDistance), OffBallPressureDistance),
                 })
        {
            if (value < 1)
            {
                problems.Add($"{name} must be a positive pitch distance, was {value}.");
            }
        }

        if (BackPassFreeDepth < 0 || BackPassMaxDepth < BackPassFreeDepth)
        {
            problems.Add(
                $"The back-pass depths are inverted: free {BackPassFreeDepth}, at most {BackPassMaxDepth}.");
        }

        if (DefenderReceiveMaxHolderX is < 0 or > Certain)
        {
            problems.Add(
                $"DefenderReceiveMaxHolderX must be a pitch coordinate in 0..{Certain}, was {DefenderReceiveMaxHolderX}.");
        }

        if (DefenderReceiveMaxPointX is < 0 or > Certain)
        {
            problems.Add(
                $"DefenderReceiveMaxPointX must be a pitch coordinate in 0..{Certain}, was {DefenderReceiveMaxPointX}.");
        }

        if (ReceiverSeeFullDistance < 1)
        {
            problems.Add($"ReceiverSeeFullDistance must be a positive pitch distance, was {ReceiverSeeFullDistance}.");
        }

        if (ReceiverSeeLowestBasisPoints > ReceiverSeeHighestBasisPoints)
        {
            problems.Add(
                $"The receiver sight chances are inverted: lowest {ReceiverSeeLowestBasisPoints}, highest {ReceiverSeeHighestBasisPoints}.");
        }

        if (ReceiverOpennessWeight < 0
            || ReceiverProgressWeight < 0
            || ReceiverReachWeight < 0
            || ReceiverLaneWeight < 0
            || ReceiverOpennessWeight + ReceiverProgressWeight + ReceiverReachWeight + ReceiverLaneWeight < 1)
        {
            problems.Add("The receiver score weights must not be negative, and at least one must be positive.");
        }

        if (ReceiverChoiceGainLowest < 0 || ReceiverChoiceGainHighest < ReceiverChoiceGainLowest)
        {
            problems.Add(
                $"The receiver choice gains are inverted: lowest {ReceiverChoiceGainLowest}, highest {ReceiverChoiceGainHighest}.");
        }

        foreach (var (name, value) in new[]
                 {
                     (nameof(ChainWeakestOpennessReference), ChainWeakestOpennessReference),
                     (nameof(ChainFinalOpennessReference), ChainFinalOpennessReference),
                     (nameof(ChainChoiceReference), ChainChoiceReference),
                 })
        {
            if (value is < 0 or > Certain)
            {
                problems.Add($"{name} must be a score in 0..{Certain}, was {value}.");
            }
        }

        if (ReachWeightFloorBasisPoints is < 0 or > Certain)
        {
            problems.Add($"ReachWeightFloorBasisPoints must be in 0..{Certain}, was {ReachWeightFloorBasisPoints}.");
        }

        if (FinishingGoalSwingBasisPoints < 0 || FinishingGoalReference is < 1 or > 20)
        {
            problems.Add("The Finishing goal swing must not be negative, and its reference must be an attribute value in 1..20.");
        }

        if (CrossCreationMultiplierBasisPoints < Certain)
        {
            problems.Add($"CrossCreationMultiplierBasisPoints must not make a cross less likely to create a chance, was {CrossCreationMultiplierBasisPoints}.");
        }

        if (ShooterChainBonusBasisPoints < Certain)
        {
            problems.Add($"ShooterChainBonusBasisPoints must not take weight away from the player on the ball, was {ShooterChainBonusBasisPoints}.");
        }

        if (CrossHeaderDeliveryBaseline is < 1 or > 20 || CrossHeaderDeliveryAerialWeight < 0 || CrossHeaderAttackerBonus < 0)
        {
            problems.Add("The cross header constants must be a baseline in 1..20 and non-negative weights.");
        }

        if (SoloBestChoiceLowestBasisPoints > SoloBestChoiceHighestBasisPoints)
        {
            problems.Add(
                $"The solo choice accuracies are inverted: lowest {SoloBestChoiceLowestBasisPoints}, highest {SoloBestChoiceHighestBasisPoints}.");
        }

        if (SoloDribbleUtilityBasisPoints is < 0 or > Certain
            || LongShotUtilityBasisPoints is < 0 or > Certain
            || LongShotGoalMultiplierBasisPoints is < 0 or > Certain)
        {
            problems.Add($"The solo utilities and the long shot's goal multiplier must be in 0..{Certain}.");
        }

        if (SoloDribbleSkillWeight < 0
            || SoloDribbleSpaceWeight < 0
            || SoloDribbleSkillWeight + SoloDribbleSpaceWeight < 1
            || LongShotSkillWeight < 0
            || LongShotRangeWeight < 0
            || LongShotSkillWeight + LongShotRangeWeight < 1)
        {
            problems.Add("The solo score weights must not be negative, and at least one of each pair must be positive.");
        }

        if (SoloDribbleStep < 1)
        {
            problems.Add($"SoloDribbleStep must be a positive pitch distance, was {SoloDribbleStep}.");
        }

        if (LongShotMinX < 0 || LongShotMaxX <= LongShotMinX || LongShotMaxX > Certain)
        {
            problems.Add(
                $"The long shot band must be an ordered pair of pitch coordinates inside 0..{Certain}, was {LongShotMinX}..{LongShotMaxX}.");
        }

        if (ShotFinalThirdXMinBasisPoints <= Certain / 2)
        {
            problems.Add(
                $"ShotFinalThirdXMinBasisPoints ({ShotFinalThirdXMinBasisPoints}) must put shots in the final third.");
        }

        if (PressurePointXMaxBasisPoints < FreeKickShootingRangeX / 2)
        {
            problems.Add(
                $"PressurePointXMaxBasisPoints ({PressurePointXMaxBasisPoints}) is too shallow to reach the "
                + $"free-kick range ({FreeKickShootingRangeX}).");
        }

        foreach (var (name, value) in new[]
                 {
                     (nameof(OffsideLineXBasisPoints), OffsideLineXBasisPoints),
                     (nameof(TurnoverMiddleThirdXBasisPoints), TurnoverMiddleThirdXBasisPoints),
                     (nameof(GoalAreaXBasisPoints), GoalAreaXBasisPoints),
                     (nameof(FinalThirdEntryXBasisPoints), FinalThirdEntryXBasisPoints),
                     (nameof(MinClearanceDistanceBasisPoints), MinClearanceDistanceBasisPoints),
                 })
        {
            if (value is < 0 or > Certain)
            {
                problems.Add($"{name} must be a pitch coordinate in 0..{Certain}, was {value}.");
            }
        }

        foreach (var (name, value) in new[]
                 {
                     (nameof(CrossAltitude), CrossAltitude),
                     (nameof(ShotAltitude), ShotAltitude),
                     (nameof(HeaderAltitude), HeaderAltitude),
                     (nameof(ClearanceAltitude), ClearanceAltitude),
                     (nameof(StrikeLowAltitudeMax), StrikeLowAltitudeMax),
                     (nameof(OverBarAltitudeMax), OverBarAltitudeMax),
                 })
        {
            if (value is < 0 or > 100)
            {
                problems.Add($"{name} must be a ball altitude in 0..100, was {value}.");
            }
        }

        // The crossbar is the shot altitude: a strike under the bar must be able to stay under it, and one over
        // the bar must be able to clear it by enough to read as over rather than as a graze.
        if (StrikeLowAltitudeMax >= ShotAltitude || OverBarAltitudeMax <= ShotAltitude + 10)
        {
            problems.Add(
                $"The strike altitudes must straddle the crossbar: {StrikeLowAltitudeMax} < {ShotAltitude} "
                + $"< {OverBarAltitudeMax} - 10.");
        }

        if (BoxYMinBasisPoints < 0 || BoxYMaxBasisPoints > SpatialPitch.PitchWidth || BoxYMinBasisPoints > BoxYMaxBasisPoints)
        {
            problems.Add(
                $"The penalty area band must be an ordered pair inside 0..{SpatialPitch.PitchWidth}, "
                + $"was {BoxYMinBasisPoints}..{BoxYMaxBasisPoints}.");
        }

        // The wide miss has to be able to stay on the pitch beside the goal: the goal mouth sits between the
        // posts, and a strike that lands beyond the touchline is a different kind of miss.
        if (SpatialPitch.GoalYMin - MissWideMaxBasisPoints < 0
            || SpatialPitch.GoalYMax + MissWideMaxBasisPoints > SpatialPitch.PitchWidth)
        {
            problems.Add($"MissWideMaxBasisPoints ({MissWideMaxBasisPoints}) would carry a miss off the pitch.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Engine rules '{Version}' are invalid: {string.Join(" ", problems)}");
        }
    }

    private IEnumerable<(string Name, int Value)> ProbabilityConstants()
    {
        yield return (nameof(ReceiverSeeLowestBasisPoints), ReceiverSeeLowestBasisPoints);
        yield return (nameof(ReceiverSeeHighestBasisPoints), ReceiverSeeHighestBasisPoints);
        yield return (nameof(ReceiverSeeDistancePenaltyBasisPoints), ReceiverSeeDistancePenaltyBasisPoints);
        yield return (nameof(ReceiverPullBasisPoints), ReceiverPullBasisPoints);
        yield return (nameof(SoloBestChoiceLowestBasisPoints), SoloBestChoiceLowestBasisPoints);
        yield return (nameof(SoloBestChoiceHighestBasisPoints), SoloBestChoiceHighestBasisPoints);
        yield return (nameof(FinishingGoalSwingBasisPoints), FinishingGoalSwingBasisPoints);
        yield return (nameof(ChainProgressSwingBasisPoints), ChainProgressSwingBasisPoints);
        yield return (nameof(ChainCreationOpennessSwingBasisPoints), ChainCreationOpennessSwingBasisPoints);
        yield return (nameof(ChainCreationChoiceSwingBasisPoints), ChainCreationChoiceSwingBasisPoints);
        yield return (nameof(BasePossessionBasisPoints), BasePossessionBasisPoints);
        yield return (nameof(PossessionControlSwingBasisPoints), PossessionControlSwingBasisPoints);
        yield return (nameof(PossessionHomeBonusBasisPoints), PossessionHomeBonusBasisPoints);
        yield return (nameof(BaseProgressBasisPoints), BaseProgressBasisPoints);
        yield return (nameof(ProgressControlSwingBasisPoints), ProgressControlSwingBasisPoints);
        yield return (nameof(CounterStartBasisPoints), CounterStartBasisPoints);
        yield return (nameof(CounterStartWithInstructionBasisPoints), CounterStartWithInstructionBasisPoints);
        yield return (nameof(CounterProgressBasisPoints), CounterProgressBasisPoints);
        yield return (nameof(CounterProgressPerPostureBasisPoints), CounterProgressPerPostureBasisPoints);
        yield return (nameof(CounterCreationBasisPoints), CounterCreationBasisPoints);
        yield return (nameof(CounterCreationPerPostureBasisPoints), CounterCreationPerPostureBasisPoints);
        yield return (nameof(CounterAttackBuildUpCostBasisPoints), CounterAttackBuildUpCostBasisPoints);
        yield return (nameof(CounterAttackShapeCostBasisPoints), CounterAttackShapeCostBasisPoints);
        yield return (nameof(CounterRecoveryProgressStepBasisPoints), CounterRecoveryProgressStepBasisPoints);
        yield return (nameof(CounterRecoveryCreationStepBasisPoints), CounterRecoveryCreationStepBasisPoints);
        yield return (nameof(CounterRecoveryMaxProgressBasisPoints), CounterRecoveryMaxProgressBasisPoints);
        yield return (nameof(CounterRecoveryMaxCreationBasisPoints), CounterRecoveryMaxCreationBasisPoints);
        yield return (nameof(CounterBackfireStartBasisPoints), CounterBackfireStartBasisPoints);
        yield return (nameof(CounterBackfireCreationBasisPoints), CounterBackfireCreationBasisPoints);
        yield return (nameof(BaseCreationBasisPoints), BaseCreationBasisPoints);
        yield return (nameof(CreationSwingBasisPoints), CreationSwingBasisPoints);
        yield return (nameof(LeadingCreationStepBasisPoints), LeadingCreationStepBasisPoints);
        yield return (nameof(TrailingCreationStepBasisPoints), TrailingCreationStepBasisPoints);
        yield return (nameof(MaxGameStateModifierBasisPoints), MaxGameStateModifierBasisPoints);
        yield return (nameof(OffsideShareOfTurnoverBasisPoints), OffsideShareOfTurnoverBasisPoints);
        yield return (nameof(CornerShareOfFailedCreationBasisPoints), CornerShareOfFailedCreationBasisPoints);
        yield return (nameof(CornerChanceBasisPoints), CornerChanceBasisPoints);
        yield return (nameof(PenaltyFromFoulBasisPoints), PenaltyFromFoulBasisPoints);
        yield return (nameof(MinShotGoalBasisPoints), MinShotGoalBasisPoints);
        yield return (nameof(MaxShotGoalBasisPoints), MaxShotGoalBasisPoints);
        yield return (nameof(BaseShotGoalBasisPoints), BaseShotGoalBasisPoints);
        yield return (nameof(ShotQualitySwingBasisPoints), ShotQualitySwingBasisPoints);
        yield return (nameof(WoodworkShareBasisPoints), WoodworkShareBasisPoints);
        yield return (nameof(BlockedShareBasisPoints), BlockedShareBasisPoints);
        yield return (nameof(BaseSaveBasisPoints), BaseSaveBasisPoints);
        yield return (nameof(MinSaveBasisPoints), MinSaveBasisPoints);
        yield return (nameof(MaxSaveBasisPoints), MaxSaveBasisPoints);
        yield return (nameof(PenaltyGoalBasisPoints), PenaltyGoalBasisPoints);
        yield return (nameof(GroundDuelSwingBasisPoints), GroundDuelSwingBasisPoints);
        yield return (nameof(AerialDuelSwingBasisPoints), AerialDuelSwingBasisPoints);
        yield return (nameof(ScrambleSwingBasisPoints), ScrambleSwingBasisPoints);
        yield return (nameof(DribbleCreationBonusBasisPoints), DribbleCreationBonusBasisPoints);
        yield return (nameof(FreeKickQualitySwingBasisPoints), FreeKickQualitySwingBasisPoints);
        yield return (nameof(BaseGroundDuelBasisPoints), BaseGroundDuelBasisPoints);
        yield return (nameof(BaseAerialDuelBasisPoints), BaseAerialDuelBasisPoints);
        yield return (nameof(ScrambleOpeningBasisPoints), ScrambleOpeningBasisPoints);
        yield return (nameof(BaseScrambleBasisPoints), BaseScrambleBasisPoints);
        yield return (nameof(DuelDifferentialReference), DuelDifferentialReference);
        yield return (nameof(FreeKickAwardBasisPoints), FreeKickAwardBasisPoints);
        yield return (nameof(FreeKickAttemptBasisPoints), FreeKickAttemptBasisPoints);
        yield return (nameof(FreeKickGoalBasisPoints), FreeKickGoalBasisPoints);
        yield return (nameof(FreeKickSavedShareBasisPoints), FreeKickSavedShareBasisPoints);
        yield return (nameof(FreeKickBlockedShareBasisPoints), FreeKickBlockedShareBasisPoints);
        yield return (nameof(FreeKickWoodworkShareBasisPoints), FreeKickWoodworkShareBasisPoints);
        yield return (nameof(DuelFoulBasisPoints), DuelFoulBasisPoints);

        // A spatial threshold on the normalized pitch, checked on the same 0..10000 scale it lives on.
        yield return (nameof(FreeKickShootingRangeX), FreeKickShootingRangeX);
        yield return (nameof(BaseFoulBasisPoints), BaseFoulBasisPoints);
        yield return (nameof(YellowCardPerFoulBasisPoints), YellowCardPerFoulBasisPoints);
        yield return (nameof(StraightRedPerFoulBasisPoints), StraightRedPerFoulBasisPoints);
        yield return (nameof(ConditionLossPerPossessionBasisPoints), ConditionLossPerPossessionBasisPoints);
        yield return (nameof(FatigueGainPerPossessionBasisPoints), FatigueGainPerPossessionBasisPoints);
        yield return (nameof(HalfTimeConditionRecoveryBasisPoints), HalfTimeConditionRecoveryBasisPoints);
        yield return (nameof(HalfTimeFatigueRecoveryBasisPoints), HalfTimeFatigueRecoveryBasisPoints);
        yield return (nameof(SharpnessGainPerPossessionBasisPoints), SharpnessGainPerPossessionBasisPoints);
        yield return (nameof(MaxMoraleDriftBasisPoints), MaxMoraleDriftBasisPoints);
        yield return (nameof(MoraleGainPerGoalBasisPoints), MoraleGainPerGoalBasisPoints);
        yield return (nameof(MoraleLossPerConcededGoalBasisPoints), MoraleLossPerConcededGoalBasisPoints);
        yield return (nameof(BaseInjuryPerPossessionBasisPoints), BaseInjuryPerPossessionBasisPoints);
        yield return (nameof(MaxInjuryProbabilityBasisPoints), MaxInjuryProbabilityBasisPoints);
        yield return (nameof(ConditionSubstitutionThresholdBasisPoints), ConditionSubstitutionThresholdBasisPoints);
        yield return (nameof(MinimumConditionAdvantageBasisPoints), MinimumConditionAdvantageBasisPoints);
        yield return (nameof(CrossShareFlankLaneBasisPoints), CrossShareFlankLaneBasisPoints);
        yield return (nameof(CrossShareCentreLaneBasisPoints), CrossShareCentreLaneBasisPoints);
        yield return (nameof(CrossFocusCentreFlankBasisPoints), CrossFocusCentreFlankBasisPoints);
        yield return (nameof(CrossFocusCentreCentreBasisPoints), CrossFocusCentreCentreBasisPoints);
        yield return (nameof(CrossFocusPairFlankBasisPoints), CrossFocusPairFlankBasisPoints);
        yield return (nameof(CrossFocusPairCentreBasisPoints), CrossFocusPairCentreBasisPoints);
        yield return (nameof(CrossFocusPairOtherFlankBasisPoints), CrossFocusPairOtherFlankBasisPoints);
        yield return (nameof(MissOverShareBasisPoints), MissOverShareBasisPoints);
        yield return (nameof(PostShareOfWoodworkBasisPoints), PostShareOfWoodworkBasisPoints);
        yield return (nameof(PenaltySavedShareBasisPoints), PenaltySavedShareBasisPoints);
        yield return (nameof(PenaltyQualitySwingBasisPoints), PenaltyQualitySwingBasisPoints);
        yield return (nameof(CornerDeliveryChanceStepBasisPoints), CornerDeliveryChanceStepBasisPoints);
        yield return (nameof(DuelFoulPenaltyBasisPoints), DuelFoulPenaltyBasisPoints);
        yield return (nameof(AggressionFoulStepBasisPoints), AggressionFoulStepBasisPoints);
        yield return (nameof(TacklingFoulStepBasisPoints), TacklingFoulStepBasisPoints);
        yield return (nameof(MaxFoulSkillAdjustBasisPoints), MaxFoulSkillAdjustBasisPoints);
        yield return (nameof(TiredPhysicalDropBasisPoints), TiredPhysicalDropBasisPoints);
        yield return (nameof(TiredTechnicalDropBasisPoints), TiredTechnicalDropBasisPoints);
        yield return (nameof(TiredMentalDropBasisPoints), TiredMentalDropBasisPoints);
        yield return (nameof(StaminaConditionLossStepBasisPoints), StaminaConditionLossStepBasisPoints);
        yield return (nameof(LeadershipMoraleStepBasisPoints), LeadershipMoraleStepBasisPoints);
    }

    private IEnumerable<(string Name, int Value)> MultiplierConstants()
    {
        yield return (nameof(CentralZoneMultiplierBasisPoints), CentralZoneMultiplierBasisPoints);
        yield return (nameof(InsideZoneMultiplierBasisPoints), InsideZoneMultiplierBasisPoints);
        yield return (nameof(WideZoneMultiplierBasisPoints), WideZoneMultiplierBasisPoints);
        yield return (nameof(AggressiveTacklingFoulMultiplierBasisPoints), AggressiveTacklingFoulMultiplierBasisPoints);
        yield return (nameof(StayOnFeetFoulMultiplierBasisPoints), StayOnFeetFoulMultiplierBasisPoints);
        yield return (nameof(AggressiveTacklingCardMultiplierBasisPoints), AggressiveTacklingCardMultiplierBasisPoints);
        yield return (nameof(HighTempoConditionLossMultiplierBasisPoints), HighTempoConditionLossMultiplierBasisPoints);
        yield return (nameof(LowTempoConditionLossMultiplierBasisPoints), LowTempoConditionLossMultiplierBasisPoints);
        yield return (nameof(HighTempoPossessionSecondsMultiplierBasisPoints), HighTempoPossessionSecondsMultiplierBasisPoints);
        yield return (nameof(LowTempoPossessionSecondsMultiplierBasisPoints), LowTempoPossessionSecondsMultiplierBasisPoints);
        yield return (nameof(HighPressConditionLossMultiplierBasisPoints), HighPressConditionLossMultiplierBasisPoints);
        yield return (nameof(LowBlockConditionLossMultiplierBasisPoints), LowBlockConditionLossMultiplierBasisPoints);
        yield return (nameof(FatigueInjuryMultiplierBasisPoints), FatigueInjuryMultiplierBasisPoints);
        yield return (nameof(MaxTacticalModifierBasisPoints), MaxTacticalModifierBasisPoints);
        yield return (nameof(MinTacticalModifierBasisPoints), MinTacticalModifierBasisPoints);
        yield return (nameof(OutOfPositionPenaltyBasisPoints), OutOfPositionPenaltyBasisPoints);
        yield return (nameof(SecondaryPositionPenaltyBasisPoints), SecondaryPositionPenaltyBasisPoints);
        yield return (nameof(UnfamiliarRolePenaltyBasisPoints), UnfamiliarRolePenaltyBasisPoints);
        yield return (nameof(ShortHandedPenaltyBasisPoints), ShortHandedPenaltyBasisPoints);
        yield return (nameof(HomeAdvantageBasisPoints), HomeAdvantageBasisPoints);
        yield return (nameof(AggressiveTacklingDuelFoulMultiplierBasisPoints), AggressiveTacklingDuelFoulMultiplierBasisPoints);
        yield return (nameof(StayOnFeetDuelFoulMultiplierBasisPoints), StayOnFeetDuelFoulMultiplierBasisPoints);
        yield return (nameof(ShorthandedConditionLossMultiplierBasisPoints), ShorthandedConditionLossMultiplierBasisPoints);
        yield return (nameof(DuelShortHandedPenaltyBasisPoints), DuelShortHandedPenaltyBasisPoints);
        yield return (nameof(TimeWastingPossessionSecondsMultiplierBasisPoints), TimeWastingPossessionSecondsMultiplierBasisPoints);
        yield return (nameof(MinStaminaConditionLossMultiplierBasisPoints), MinStaminaConditionLossMultiplierBasisPoints);
        yield return (nameof(MaxStaminaConditionLossMultiplierBasisPoints), MaxStaminaConditionLossMultiplierBasisPoints);
        yield return (nameof(MinLeadershipMoraleMultiplierBasisPoints), MinLeadershipMoraleMultiplierBasisPoints);
        yield return (nameof(MaxLeadershipMoraleMultiplierBasisPoints), MaxLeadershipMoraleMultiplierBasisPoints);
    }

    private IEnumerable<(string Name, int Value)> RatingConstants()
    {
        yield return (nameof(RatingBaseBasisPoints), RatingBaseBasisPoints);
        yield return (nameof(RatingWinBonusBasisPoints), RatingWinBonusBasisPoints);
        yield return (nameof(RatingDrawBonusBasisPoints), RatingDrawBonusBasisPoints);
        yield return (nameof(RatingLossPenaltyBasisPoints), RatingLossPenaltyBasisPoints);
        yield return (nameof(RatingGoalBonusBasisPoints), RatingGoalBonusBasisPoints);
        yield return (nameof(RatingAssistBonusBasisPoints), RatingAssistBonusBasisPoints);
        yield return (nameof(RatingSaveBonusBasisPoints), RatingSaveBonusBasisPoints);
        yield return (nameof(RatingMaxSaveBonusBasisPoints), RatingMaxSaveBonusBasisPoints);
        yield return (nameof(RatingYellowPenaltyBasisPoints), RatingYellowPenaltyBasisPoints);
        yield return (nameof(RatingRedPenaltyBasisPoints), RatingRedPenaltyBasisPoints);
        yield return (nameof(RatingMinBasisPoints), RatingMinBasisPoints);
        yield return (nameof(RatingMaxBasisPoints), RatingMaxBasisPoints);
        yield return (nameof(LiveRatingBaseBasisPoints), LiveRatingBaseBasisPoints);
        yield return (nameof(LiveRatingTackleBonusBasisPoints), LiveRatingTackleBonusBasisPoints);
        yield return (nameof(LiveRatingTackleLostPenaltyBasisPoints), LiveRatingTackleLostPenaltyBasisPoints);
        yield return (nameof(LiveRatingAerialBonusBasisPoints), LiveRatingAerialBonusBasisPoints);
        yield return (nameof(LiveRatingAerialLostPenaltyBasisPoints), LiveRatingAerialLostPenaltyBasisPoints);
        yield return (nameof(LiveRatingShotBonusBasisPoints), LiveRatingShotBonusBasisPoints);
        yield return (nameof(LiveRatingShotMissPenaltyBasisPoints), LiveRatingShotMissPenaltyBasisPoints);
        yield return (nameof(LiveRatingGoalBonusBasisPoints), LiveRatingGoalBonusBasisPoints);
        yield return (nameof(LiveRatingAssistBonusBasisPoints), LiveRatingAssistBonusBasisPoints);
        yield return (nameof(LiveRatingSaveBonusBasisPoints), LiveRatingSaveBonusBasisPoints);
        yield return (nameof(LiveRatingGoalConcededPenaltyBasisPoints), LiveRatingGoalConcededPenaltyBasisPoints);
        yield return (nameof(LiveRatingYellowPenaltyBasisPoints), LiveRatingYellowPenaltyBasisPoints);
        yield return (nameof(LiveRatingRedPenaltyBasisPoints), LiveRatingRedPenaltyBasisPoints);
    }

    private IEnumerable<(string Name, int Min, int Max)> OrderedTriples()
    {
        yield return (nameof(MinProgressBasisPoints), MinProgressBasisPoints, MaxProgressBasisPoints);
        yield return (nameof(MinCreationBasisPoints), MinCreationBasisPoints, MaxCreationBasisPoints);
        yield return (nameof(MinShotGoalBasisPoints), MinShotGoalBasisPoints, MaxShotGoalBasisPoints);
        yield return (nameof(MinSaveBasisPoints), MinSaveBasisPoints, MaxSaveBasisPoints);
        yield return (nameof(PenaltyMinGoalBasisPoints), PenaltyMinGoalBasisPoints, PenaltyMaxGoalBasisPoints);
        yield return (nameof(CornerChanceMinBasisPoints), CornerChanceMinBasisPoints, CornerChanceMaxBasisPoints);
        yield return (nameof(DuelMinWinBasisPoints), DuelMinWinBasisPoints, DuelMaxWinBasisPoints);
        yield return (nameof(MinTouchAdvanceBasisPoints), MinTouchAdvanceBasisPoints, MaxTouchAdvanceBasisPoints);
        yield return (nameof(PressurePointXMinBasisPoints), PressurePointXMinBasisPoints, PressurePointXMaxBasisPoints);
        yield return (nameof(ShotFinalThirdXMinBasisPoints), ShotFinalThirdXMinBasisPoints, ShotFinalThirdXMaxBasisPoints);
        yield return (nameof(ShotCentralBandYMinBasisPoints), ShotCentralBandYMinBasisPoints, ShotCentralBandYMaxBasisPoints);
        yield return (nameof(ShotInsideBandYMinBasisPoints), ShotInsideBandYMinBasisPoints, ShotInsideBandYMaxBasisPoints);
        yield return (nameof(ShotWideBandYMinBasisPoints), ShotWideBandYMinBasisPoints, ShotWideBandYMaxBasisPoints);
        yield return (nameof(ScrambleCutMinBasisPoints), ScrambleCutMinBasisPoints, ScrambleCutMaxBasisPoints);
        yield return (nameof(ProgressionCutMinBasisPoints), ProgressionCutMinBasisPoints, ProgressionCutMaxBasisPoints);
        yield return (nameof(EntryFractionMinBasisPoints), EntryFractionMinBasisPoints, EntryFractionMaxBasisPoints);
        yield return (nameof(BoxXMinBasisPoints), BoxXMinBasisPoints, BoxXMaxBasisPoints);
        yield return (nameof(CornerOutOfPlayMinBasisPoints), CornerOutOfPlayMinBasisPoints, CornerOutOfPlayMaxBasisPoints);
        yield return (nameof(SaveDepthMinBasisPoints), SaveDepthMinBasisPoints, SaveDepthMaxBasisPoints);
        yield return (nameof(MissWideMinBasisPoints), MissWideMinBasisPoints, MissWideMaxBasisPoints);
        yield return (nameof(BlockDistanceMinBasisPoints), BlockDistanceMinBasisPoints, BlockDistanceMaxBasisPoints);
        yield return (nameof(ReboundDistanceMinBasisPoints), ReboundDistanceMinBasisPoints, ReboundDistanceMaxBasisPoints);
    }

    private IEnumerable<(string Name, int Floor, int Ceiling)> FactorPairs()
    {
        yield return (nameof(FatigueFactorFloorBasisPoints), FatigueFactorFloorBasisPoints, FatigueFactorCeilingBasisPoints);
        yield return (nameof(MoraleFactorFloorBasisPoints), MoraleFactorFloorBasisPoints, MoraleFactorCeilingBasisPoints);
        yield return (nameof(SharpnessFactorFloorBasisPoints), SharpnessFactorFloorBasisPoints, SharpnessFactorCeilingBasisPoints);
        yield return (nameof(PositioningFloorBasisPoints), PositioningFloorBasisPoints, PositioningCeilingBasisPoints);
    }
}
