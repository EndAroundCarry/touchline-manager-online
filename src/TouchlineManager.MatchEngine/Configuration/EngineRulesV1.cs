using System.Globalization;

namespace TouchlineManager.MatchEngine.Configuration;

/// <summary>
/// Every tunable constant of the match engine, versioned as rules set 1.
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
public sealed record EngineRulesV1
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
    public static EngineRulesV1 Default { get; } = new();

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
    public int PossessionSecondsMin { get; init; } = 16;

    /// <summary>The most time one possession can consume, in seconds.</summary>
    public int PossessionSecondsMax { get; init; } = 44;

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
    public int PossessionControlSwingBasisPoints { get; init; } = 2_400;

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
    public int PenaltyFromFoulBasisPoints { get; init; } = 120;

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
    /// Calibrated with the laboratory to about 2.8–2.9 goals a match. The mean is what sets the shape of the
    /// score tail — a higher mean thickens it faster than any game-state effect thins it — so this constant and
    /// <see cref="GameStateMarginThresholdGoals"/> were tuned together against a measured distribution rather
    /// than guessed at.
    /// </remarks>
    public int BaseShotGoalBasisPoints { get; init; } = 760;

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

    // ---- Discipline ------------------------------------------------------------------------------

    /// <summary>The baseline chance a possession contains a foul by the defending side.</summary>
    public int BaseFoulBasisPoints { get; init; } = 1_100;

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

    /// <summary>What a player at zero condition multiplies their ratings by.</summary>
    public int ConditionFactorFloorBasisPoints { get; init; } = 8_500;

    /// <summary>What a player at full condition multiplies their ratings by.</summary>
    public int ConditionFactorCeilingBasisPoints { get; init; } = 10_500;

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

    /// <summary>
    /// The rating difference at which a swing is applied in full, so the probability formulas can express
    /// "how much a difference of this size moves the chance" rather than a raw per-point coefficient.
    /// Roughly the gap between a mid-table side and a good one.
    /// </summary>
    public int RatingDifferentialReference { get; init; } = 1_000;

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

    /// <summary>What playing a player at a role outside their family costs in cohesion.</summary>
    public int OutOfPositionCohesionPenaltyBasisPoints { get; init; } = 1_400;

    /// <summary>
    /// What each player below eleven multiplies the whole side's ratings by, so being a man down costs
    /// more than simply averaging over ten players (master plan §8.5).
    /// </summary>
    public int ShortHandedPenaltyBasisPoints { get; init; } = 8_600;

    // ---- Set-piece and home advantage ------------------------------------------------------------

    /// <summary>Home advantage, applied to the home side's ratings (master plan §8.5). Small and configurable.</summary>
    public int HomeAdvantageBasisPoints { get; init; } = 10_300;

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
    /// <returns>The canonical, name-ordered description of every constant.</returns>
    public IReadOnlyList<string> ToCanonicalParts()
    {
        var parts = new List<string> { Version };

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

        // A subtractive penalty is not a multiplier: it is taken off a cohesion figure, so it is bounded by
        // certainty rather than by the multiplier band. Filing it with the multipliers is exactly the mistake
        // this check exists to catch.
        if (OutOfPositionCohesionPenaltyBasisPoints is < 0 or > Certain)
        {
            problems.Add(
                "OutOfPositionCohesionPenaltyBasisPoints must be subtractable from a cohesion figure in "
                + $"0..{Certain}, was {OutOfPositionCohesionPenaltyBasisPoints}.");
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

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Engine rules '{Version}' are invalid: {string.Join(" ", problems)}");
        }
    }

    private IEnumerable<(string Name, int Value)> ProbabilityConstants()
    {
        yield return (nameof(BasePossessionBasisPoints), BasePossessionBasisPoints);
        yield return (nameof(PossessionControlSwingBasisPoints), PossessionControlSwingBasisPoints);
        yield return (nameof(PossessionHomeBonusBasisPoints), PossessionHomeBonusBasisPoints);
        yield return (nameof(BaseProgressBasisPoints), BaseProgressBasisPoints);
        yield return (nameof(ProgressControlSwingBasisPoints), ProgressControlSwingBasisPoints);
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
    }

    private IEnumerable<(string Name, int Min, int Max)> OrderedTriples()
    {
        yield return (nameof(MinProgressBasisPoints), MinProgressBasisPoints, MaxProgressBasisPoints);
        yield return (nameof(MinCreationBasisPoints), MinCreationBasisPoints, MaxCreationBasisPoints);
        yield return (nameof(MinShotGoalBasisPoints), MinShotGoalBasisPoints, MaxShotGoalBasisPoints);
        yield return (nameof(MinSaveBasisPoints), MinSaveBasisPoints, MaxSaveBasisPoints);
    }

    private IEnumerable<(string Name, int Floor, int Ceiling)> FactorPairs()
    {
        yield return (nameof(ConditionFactorFloorBasisPoints), ConditionFactorFloorBasisPoints, ConditionFactorCeilingBasisPoints);
        yield return (nameof(FatigueFactorFloorBasisPoints), FatigueFactorFloorBasisPoints, FatigueFactorCeilingBasisPoints);
        yield return (nameof(MoraleFactorFloorBasisPoints), MoraleFactorFloorBasisPoints, MoraleFactorCeilingBasisPoints);
        yield return (nameof(SharpnessFactorFloorBasisPoints), SharpnessFactorFloorBasisPoints, SharpnessFactorCeilingBasisPoints);
    }
}
