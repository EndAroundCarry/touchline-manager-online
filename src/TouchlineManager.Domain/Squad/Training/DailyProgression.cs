using System.Globalization;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Squad.Training;

/// <summary>
/// Everything one player's day of training needs, so the progression has no ambient state (`TRN-9`).
/// </summary>
/// <remarks>
/// The hidden potential and aptitude are passed in rather than read from the player, because they are what
/// cap and scale growth and the calculator is deliberately pure: it takes them as values so a test can vary
/// them.
/// </remarks>
/// <param name="PlayerId">The player's identity, which seeds the day's draw (`TRN-9`).</param>
/// <param name="Age">The player's age in game years, which is the development curve.</param>
/// <param name="Attributes">The player's current attributes.</param>
/// <param name="Potential">The hidden development ceiling, on the displayed scale (`TRN-9`).</param>
/// <param name="AptitudePermille">The hidden training aptitude, from <see cref="TrainingAptitude.For"/> (`TRN-15`).</param>
/// <param name="Programme">The training programme in force for this player (`TRN-1`).</param>
/// <param name="Intensity">How hard the club trains.</param>
/// <param name="ConditionBp">Condition in basis points (`TRN-5`).</param>
/// <param name="FatigueBp">Fatigue in basis points (`TRN-6`).</param>
/// <param name="MoraleBp">Morale in basis points (`TRN-7`).</param>
/// <param name="MatchSharpnessBp">Match sharpness in basis points.</param>
/// <param name="AttributeProgress">
/// Each attribute's progress towards its next point, in millionths, carried over from earlier days (`TRN-10`).
/// </param>
/// <param name="Day">The day being progressed, which is part of the draw's identity.</param>
public sealed record DailyProgressionInput(
    Guid PlayerId,
    int Age,
    PlayerAttributeSet Attributes,
    int Potential,
    int AptitudePermille,
    TrainingProgramme Programme,
    TrainingIntensity Intensity,
    int ConditionBp,
    int FatigueBp,
    int MoraleBp,
    int MatchSharpnessBp,
    IReadOnlyList<int> AttributeProgress,
    DateOnly Day);

/// <summary>One attribute's net movement across a training day.</summary>
/// <param name="Attribute">The attribute that moved.</param>
/// <param name="Delta">The net change in displayed points; never zero.</param>
public sealed record AttributeChange(AttributeName Attribute, int Delta);

/// <summary>The state a player leaves a training day in (`TRN-9`, `TRN-10`).</summary>
/// <param name="Attributes">The attributes after development and decline, never outside the displayed scale (`TRN-4`).</param>
/// <param name="ConditionBp">Condition in basis points.</param>
/// <param name="FatigueBp">Fatigue in basis points.</param>
/// <param name="MoraleBp">Morale in basis points.</param>
/// <param name="MatchSharpnessBp">Match sharpness in basis points.</param>
/// <param name="AttributeProgress">Each attribute's progress towards its next point, carried into the next day.</param>
/// <param name="DevelopmentMilli">The development the day earned, in thousandths of a point.</param>
/// <param name="DeclineMilli">The decline the day incurred, in thousandths of a point.</param>
/// <param name="AttributeChanges">Every attribute that moved by a whole point, in canonical order.</param>
/// <param name="ProgressChanges">
/// How much of the day's development each attribute earned and how much decline it incurred, net, in canonical
/// order. This is the split of the day's budget; it is what moves <paramref name="AttributeProgress"/>.
/// </param>
public sealed record DailyProgressionOutcome(
    PlayerAttributeSet Attributes,
    int ConditionBp,
    int FatigueBp,
    int MoraleBp,
    int MatchSharpnessBp,
    IReadOnlyList<int> AttributeProgress,
    int DevelopmentMilli,
    int DeclineMilli,
    IReadOnlyList<AttributeChange> AttributeChanges,
    IReadOnlyList<AttributeProgressChange> ProgressChanges);

/// <summary>
/// One day of a player's training: recovery, bounded deterministic development, and ageing (`TRN-1`,
/// `TRN-4`, `TRN-9`, `TRN-10`, `TRN-14`…`TRN-16`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the player's identity, the day, the training in force, and the hidden potential and
/// aptitude. It reads no clock, no database, and no global random source, so the same inputs always produce
/// the same day and the whole world's progression can be replayed from the seed (`TRN-9`).
/// </para>
/// <para>
/// <b>Development.</b> The day's budget, in millionths of an attribute point, is a base rate scaled by the
/// player's age curve, hidden aptitude, the club's intensity, and a fatigue penalty. It does not depend on how
/// many attributes the programme covers, so a narrow programme concentrates the same gain on fewer skills. The
/// budget is split across the programme's attributes by their weights (3 core, 2 important, 1 supporting), and
/// each attribute keeps its own progress towards its next point, so a core skill climbs faster than a
/// supporting one and a manager can see how close each is. An attribute at the player's potential takes no
/// share, and its share moves to the others (`TRN-4`, `TRN-10`).
/// </para>
/// <para>
/// <b>Decline.</b> Each attribute ages from its own start age (see <see cref="TrainingAgeCurve"/>). The
/// programme's attributes decline at half the rate, because training slows ageing. The decline comes off the
/// same per-attribute progress, so a skill that is both trained and ageing nets the two, and it falls a point
/// when its progress reaches minus one, never below the scale's floor.
/// </para>
/// <para>
/// <b>Trade-off.</b> Intensity buys development and costs condition and fatigue, and fatigue in turn costs
/// development; the recovery programme buys freshness and costs development. Every coefficient is bounded,
/// and none multiplies a rating without a counter-cost (`TRN-1`, master plan §3.8).
/// </para>
/// <para>
/// <b>Version.</b> <see cref="Version"/> is bumped whenever a coefficient, a table, or the draw order
/// changes, because a change to any of them rewrites what a replay would produce (`FIC-8`, `TRN-9`).
/// </para>
/// <para>
/// Training injuries are deliberately absent: <see cref="PlayerUnavailability"/>'s own documentation gives
/// the injury band-to-fixture mapping to Stage 8, and progression cannot create an absence before the rule
/// that measures it exists (`TRN-12`).
/// </para>
/// </remarks>
public static class DailyProgression
{
    /// <summary>The progression version, bumped whenever a coefficient or the draw order changes.</summary>
    public const string Version = "training-v3";

    /// <summary>How many thousandths of an attribute point make one displayed point (`TRN-10`).</summary>
    public const int DevelopmentBasis = 1_000;

    /// <summary>The development budget of a peak-age, neutral-aptitude player on normal intensity, per day.</summary>
    public const int BaseDailyMilli = 140;

    /// <summary>The fatigue below which development is not penalised, in basis points.</summary>
    public const int FatiguePenaltyStartBp = 6_000;

    /// <summary>The smallest fatigue factor, reached at full fatigue, in permille.</summary>
    public const int FatigueFloorPermille = 600;

    /// <summary>How much of an attribute's decline survives while the programme trains it, in permille.</summary>
    public const int ShieldedDeclinePermille = 500;

    private const int Permille = 1_000;
    private const long BudgetDenominator = 1_000_000_000_000_000L;
    private const long MicroDenominator = BudgetDenominator / Permille;
    private const int JitterLowPermille = 900;
    private const int JitterSpanPermille = 201;

    private const int FatigueRecoveryBp = 400;
    private const int RecoveryProgrammeFatigueRecoveryBp = 800;
    private const int ConditionRecoveryBp = 600;
    private const int SharpnessGainBp = 40;
    private const int MoraleDriftBp = 20;

    /// <summary>Advances one player by one day of training.</summary>
    /// <param name="input">The player's day.</param>
    /// <returns>The state the player leaves the day in.</returns>
    public static DailyProgressionOutcome Advance(DailyProgressionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        var rng = new Pcg32(DeterministicDigest.SeedOf(
            input.PlayerId.ToString("D", CultureInfo.InvariantCulture),
            input.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Version));

        var condition = Clamp(input.ConditionBp + ConditionRecoveryBp - ConditionCost(input.Intensity));
        var fatigue = Clamp(input.FatigueBp - FatigueRecovery(input.Programme) + FatigueLoad(input.Intensity));
        var sharpness = Clamp(input.MatchSharpnessBp + SharpnessGainBp);
        var morale = Drift(input.MoraleBp, rng);

        // The day's jitter is drawn whether or not anything is trained, so the draw does not depend on the branch.
        var jitter = JitterLowPermille + rng.NextInt(JitterSpanPermille);

        var definition = TrainingProgrammes.Of(input.Programme);
        var values = input.Attributes.Values.ToArray();
        var movement = new int[AttributeNames.Count];

        var developmentMicro = Develop(input, definition, jitter, values, movement);
        var declineMicro = Age(input, definition, values, movement);

        var progress = input.AttributeProgress.ToArray();
        var deltas = Settle(input, values, progress, movement);

        var changes = new List<AttributeChange>();
        var progressChanges = new List<AttributeProgressChange>();

        foreach (var name in AttributeNames.All)
        {
            if (deltas[(int)name] != 0)
            {
                changes.Add(new AttributeChange(name, deltas[(int)name]));
            }

            if (movement[(int)name] != 0)
            {
                progressChanges.Add(new AttributeProgressChange(name, movement[(int)name]));
            }
        }

        var attributes = changes.Count == 0 ? input.Attributes : PlayerAttributeSet.FromValues(values);

        return new DailyProgressionOutcome(
            attributes,
            condition,
            fatigue,
            morale,
            sharpness,
            progress,
            ToMilli(developmentMicro),
            ToMilli(declineMicro),
            changes,
            progressChanges);
    }

    /// <summary>Gets the development budget a day earns before jitter, in thousandths of a point.</summary>
    /// <param name="age">The player's age in whole game years.</param>
    /// <param name="aptitudePermille">The hidden aptitude, in permille.</param>
    /// <param name="intensity">The club's intensity.</param>
    /// <param name="fatigueBp">The player's fatigue in basis points.</param>
    public static int BudgetMilli(int age, int aptitudePermille, TrainingIntensity intensity, int fatigueBp) =>
        (int)((BudgetNumerator(age, aptitudePermille, intensity, fatigueBp, Permille) + (BudgetDenominator / 2))
            / BudgetDenominator);

    private static long BudgetNumerator(
        int age,
        int aptitudePermille,
        TrainingIntensity intensity,
        int fatigueBp,
        int jitter) =>
        (long)BaseDailyMilli
            * TrainingAgeCurve.GrowthPermille(age)
            * aptitudePermille
            * IntensityPermille(intensity)
            * FatigueFactorPermille(fatigueBp)
            * jitter;

    /// <summary>The day's budget in millionths of a point, so a share is not lost to rounding.</summary>
    private static int BudgetMicro(int age, int aptitudePermille, TrainingIntensity intensity, int fatigueBp, int jitter) =>
        (int)((BudgetNumerator(age, aptitudePermille, intensity, fatigueBp, jitter) + (MicroDenominator / 2))
            / MicroDenominator);

    private static int ToMilli(int micro) => (micro + (Permille / 2)) / Permille;

    /// <summary>
    /// Splits the day's development across the programme's attributes by weight (`TRN-4`, `TRN-10`), adding
    /// each share to <paramref name="movement"/>, and returns what was spent in millionths of a point.
    /// </summary>
    private static int Develop(
        DailyProgressionInput input,
        TrainingProgrammeDefinition definition,
        int jitter,
        int[] values,
        int[] movement)
    {
        if (definition.Attributes.Count == 0)
        {
            return 0;
        }

        var capacity = Math.Min(WorldRuleSet.AttributeMax, input.Potential);
        var eligible = definition.Attributes
            .Where(entry => values[(int)entry.Attribute] < capacity)
            .ToList();

        if (eligible.Count == 0)
        {
            return 0;
        }

        var budget = BudgetMicro(input.Age, input.AptitudePermille, input.Intensity, input.FatigueBp, jitter);
        var totalWeight = eligible.Sum(entry => entry.Weight);
        var spent = 0;

        foreach (var entry in eligible)
        {
            var share = (int)((long)budget * entry.Weight / totalWeight);

            movement[(int)entry.Attribute] += share;
            spent += share;
        }

        // The integer split leaves less than one micro-point per attribute. It goes to the heaviest attributes
        // first, so the whole budget is spent and the split is the same on every replay.
        for (var slot = 0; spent < budget; slot++, spent++)
        {
            movement[(int)eligible[slot % eligible.Count].Attribute]++;
        }

        return budget;
    }

    /// <summary>
    /// Takes each attribute's ageing off its progress (`TRN-16`), adding it to <paramref name="movement"/>, and
    /// returns the total taken in millionths of a point.
    /// </summary>
    private static int Age(
        DailyProgressionInput input,
        TrainingProgrammeDefinition definition,
        int[] values,
        int[] movement)
    {
        var total = 0;

        foreach (var name in AttributeNames.All)
        {
            var annualMilli = TrainingAgeCurve.AnnualDeclineMilli(name, input.Age);

            if (annualMilli == 0 || values[(int)name] <= WorldRuleSet.AttributeMin)
            {
                continue;
            }

            var shield = definition.WeightOf(name) > 0 ? ShieldedDeclinePermille : Permille;

            // Thousandths of a point a year, over the season's training days, in millionths of a point.
            var rate = annualMilli * shield / TrainingAgeCurve.TrainingDaysPerSeason;

            movement[(int)name] -= rate;
            total += rate;
        }

        return total;
    }

    /// <summary>
    /// Adds the day's movement to each attribute's progress and turns every whole point into a change of the
    /// displayed value (`TRN-4`, `TRN-10`). Returns the whole-point change of each attribute.
    /// </summary>
    private static int[] Settle(DailyProgressionInput input, int[] values, int[] progress, int[] movement)
    {
        var capacity = Math.Min(WorldRuleSet.AttributeMax, input.Potential);
        var deltas = new int[AttributeNames.Count];

        for (var index = 0; index < progress.Length; index++)
        {
            progress[index] += movement[index];

            while (progress[index] >= AttributeProgress.Basis && values[index] < capacity)
            {
                values[index]++;
                deltas[index]++;
                progress[index] -= AttributeProgress.Basis;
            }

            // An attribute at its ceiling holds no progress towards a point it cannot gain.
            if (progress[index] > 0 && values[index] >= capacity)
            {
                progress[index] = 0;
            }

            while (progress[index] <= -AttributeProgress.Basis && values[index] > WorldRuleSet.AttributeMin)
            {
                values[index]--;
                deltas[index]--;
                progress[index] += AttributeProgress.Basis;
            }

            if (progress[index] < 0 && values[index] <= WorldRuleSet.AttributeMin)
            {
                progress[index] = 0;
            }
        }

        return deltas;
    }

    private static void Validate(DailyProgressionInput input)
    {
        if (input.Age < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), input.Age, "An age is never negative.");
        }

        if (input.Potential is < WorldRuleSet.AttributeMin or > WorldRuleSet.AttributeMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.Potential,
                $"A potential is between {WorldRuleSet.AttributeMin} and {WorldRuleSet.AttributeMax} (TRN-4).");
        }

        if (input.AptitudePermille <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.AptitudePermille,
                "An aptitude is positive (TRN-15).");
        }

        ArgumentNullException.ThrowIfNull(input.AttributeProgress);

        if (!AttributeProgress.IsValid(input.AttributeProgress))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.AttributeProgress,
                "Attribute progress is one value per attribute, each inside one point (TRN-10).");
        }
    }

    private static int FatigueRecovery(TrainingProgramme programme) =>
        programme == TrainingProgramme.Recovery ? RecoveryProgrammeFatigueRecoveryBp : FatigueRecoveryBp;

    private static int FatigueLoad(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Light => 50,
        TrainingIntensity.Intense => 220,
        _ => 120,
    };

    private static int ConditionCost(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Light => 100,
        TrainingIntensity.Intense => 350,
        _ => 200,
    };

    private static int IntensityPermille(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Light => 700,
        TrainingIntensity.Intense => 1_350,
        _ => Permille,
    };

    /// <summary>Falls linearly from full speed at the penalty threshold to the floor at full fatigue.</summary>
    private static int FatigueFactorPermille(int fatigueBp)
    {
        if (fatigueBp <= FatiguePenaltyStartBp)
        {
            return Permille;
        }

        var span = WorldRuleSet.StateBasisPointsMax - FatiguePenaltyStartBp;
        var over = Math.Min(fatigueBp, WorldRuleSet.StateBasisPointsMax) - FatiguePenaltyStartBp;

        return Permille - ((Permille - FatigueFloorPermille) * over / span);
    }

    /// <summary>Pulls morale one bounded step towards neutral, so a day never moves it far (`TRN-13`).</summary>
    private static int Drift(int moraleBp, Pcg32 rng)
    {
        var neutral = PlayerState.NeutralBasisPoints;
        var distance = neutral - moraleBp;
        var step = Math.Min(MoraleDriftBp, Math.Abs(distance));

        if (step == 0)
        {
            return moraleBp;
        }

        // The sign comes from the distance; the size jitters by one so a run of days is not perfectly flat.
        var direction = Math.Sign(distance);

        return Clamp(moraleBp + (direction * (step - rng.NextInt(2))));
    }

    private static int Clamp(int value) =>
        Math.Clamp(value, WorldRuleSet.StateBasisPointsMin, WorldRuleSet.StateBasisPointsMax);
}
