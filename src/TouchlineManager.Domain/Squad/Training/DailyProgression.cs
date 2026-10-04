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
/// <param name="PlayerId">The player's identity, which seeds the day's draw order (`TRN-9`).</param>
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
/// <param name="DevelopmentRemainder">The partial development carried over from earlier days (`TRN-10`).</param>
/// <param name="DeclineRemainder">The partial decline carried over from earlier days (`TRN-16`).</param>
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
    int DevelopmentRemainder,
    int DeclineRemainder,
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
/// <param name="DevelopmentRemainder">The partial development carried into the next day (`TRN-10`).</param>
/// <param name="DeclineRemainder">The partial decline carried into the next day (`TRN-16`).</param>
/// <param name="DevelopmentMilli">The development the day earned, in thousandths of a point.</param>
/// <param name="DeclineMilli">The decline the day incurred, in thousandths of a point.</param>
/// <param name="AttributeChanges">Every attribute that moved by a whole point, in canonical order.</param>
public sealed record DailyProgressionOutcome(
    PlayerAttributeSet Attributes,
    int ConditionBp,
    int FatigueBp,
    int MoraleBp,
    int MatchSharpnessBp,
    int DevelopmentRemainder,
    int DeclineRemainder,
    int DevelopmentMilli,
    int DeclineMilli,
    IReadOnlyList<AttributeChange> AttributeChanges);

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
/// <b>Development.</b> The day's budget, in thousandths of an attribute point, is a base rate scaled by the
/// player's age curve, hidden aptitude, the club's intensity, and a fatigue penalty. It does not depend on how
/// many attributes the programme covers, so a narrow programme concentrates the same gain on fewer skills. The
/// fraction is added to <see cref="PlayerState.DevelopmentRemainder"/> and every whole point is spent on one
/// of the programme's attributes by a weighted draw, only where the value is below the player's potential
/// (`TRN-4`, `TRN-10`).
/// </para>
/// <para>
/// <b>Decline.</b> Each attribute ages from its own start age (see <see cref="TrainingAgeCurve"/>). The
/// programme's attributes decline at half the rate, because training slows ageing. The fraction is carried in
/// <see cref="PlayerState.DeclineRemainder"/> and each whole point is removed from one declining attribute by
/// a draw weighted by its rate, never below the scale's floor.
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
    public const string Version = "training-v2";

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

        var definition = TrainingProgrammes.Of(input.Programme);
        var values = input.Attributes.Values.ToArray();
        var deltas = new int[AttributeNames.Count];

        var (developmentMilli, developmentRemainder) = Develop(input, definition, rng, values, deltas);
        var (declineMilli, declineRemainder) = Age(input, definition, rng, values, deltas);

        var changes = new List<AttributeChange>();

        foreach (var name in AttributeNames.All)
        {
            if (deltas[(int)name] != 0)
            {
                changes.Add(new AttributeChange(name, deltas[(int)name]));
            }
        }

        var attributes = changes.Count == 0 ? input.Attributes : PlayerAttributeSet.FromValues(values);

        return new DailyProgressionOutcome(
            attributes,
            condition,
            fatigue,
            morale,
            sharpness,
            developmentRemainder,
            declineRemainder,
            developmentMilli,
            declineMilli,
            changes);
    }

    /// <summary>Gets the development budget a day earns before jitter, in thousandths of a point.</summary>
    /// <param name="age">The player's age in whole game years.</param>
    /// <param name="aptitudePermille">The hidden aptitude, in permille.</param>
    /// <param name="intensity">The club's intensity.</param>
    /// <param name="fatigueBp">The player's fatigue in basis points.</param>
    public static int BudgetMilli(int age, int aptitudePermille, TrainingIntensity intensity, int fatigueBp) =>
        Budget(age, aptitudePermille, intensity, fatigueBp, Permille);

    private static int Budget(int age, int aptitudePermille, TrainingIntensity intensity, int fatigueBp, int jitter)
    {
        var numerator = (long)BaseDailyMilli
            * TrainingAgeCurve.GrowthPermille(age)
            * aptitudePermille
            * IntensityPermille(intensity)
            * FatigueFactorPermille(fatigueBp)
            * jitter;

        return (int)((numerator + (BudgetDenominator / 2)) / BudgetDenominator);
    }

    /// <summary>Spends the day's development and carries the fraction forward (`TRN-4`, `TRN-10`).</summary>
    private static (int Milli, int Remainder) Develop(
        DailyProgressionInput input,
        TrainingProgrammeDefinition definition,
        Pcg32 rng,
        int[] values,
        int[] deltas)
    {
        // Drawn on every day, trained or not, so the rest of the day's draws do not depend on the branch.
        var jitter = JitterLowPermille + rng.NextInt(JitterSpanPermille);

        if (definition.Attributes.Count == 0)
        {
            return (0, input.DevelopmentRemainder);
        }

        var capacity = Math.Min(WorldRuleSet.AttributeMax, input.Potential);
        var eligible = definition.Attributes
            .Where(entry => values[(int)entry.Attribute] < capacity)
            .ToList();

        if (eligible.Count == 0)
        {
            return (0, input.DevelopmentRemainder);
        }

        var budget = Budget(input.Age, input.AptitudePermille, input.Intensity, input.FatigueBp, jitter);
        var accumulated = input.DevelopmentRemainder + budget;
        var points = accumulated / DevelopmentBasis;
        var remainder = accumulated % DevelopmentBasis;

        for (var point = 0; point < points && eligible.Count > 0; point++)
        {
            var pick = rng.NextInt(eligible.Sum(entry => entry.Weight));
            var slot = 0;

            while (pick >= eligible[slot].Weight)
            {
                pick -= eligible[slot].Weight;
                slot++;
            }

            var index = (int)eligible[slot].Attribute;

            values[index]++;
            deltas[index]++;

            if (values[index] >= capacity)
            {
                eligible.RemoveAt(slot);
            }
        }

        return (budget, remainder);
    }

    /// <summary>Applies the day's ageing and carries the fraction forward (`TRN-16`).</summary>
    private static (int Milli, int Remainder) Age(
        DailyProgressionInput input,
        TrainingProgrammeDefinition definition,
        Pcg32 rng,
        int[] values,
        int[] deltas)
    {
        // Micro-points per day, so a slow decline is not rounded away before it is summed.
        var rates = new long[AttributeNames.Count];
        var total = 0L;

        foreach (var name in AttributeNames.All)
        {
            var annualMilli = TrainingAgeCurve.AnnualDeclineMilli(name, input.Age);

            if (annualMilli == 0)
            {
                continue;
            }

            var shield = definition.WeightOf(name) > 0 ? ShieldedDeclinePermille : Permille;
            var rate = (long)annualMilli * shield / TrainingAgeCurve.TrainingDaysPerSeason;

            rates[(int)name] = rate;
            total += rate;
        }

        var milli = (int)((total + (Permille / 2)) / Permille);

        if (milli == 0)
        {
            return (0, input.DeclineRemainder);
        }

        var accumulated = input.DeclineRemainder + milli;
        var points = accumulated / DevelopmentBasis;
        var remainder = accumulated % DevelopmentBasis;

        for (var point = 0; point < points; point++)
        {
            var candidateTotal = 0L;

            for (var index = 0; index < rates.Length; index++)
            {
                if (values[index] > WorldRuleSet.AttributeMin)
                {
                    candidateTotal += rates[index];
                }
            }

            if (candidateTotal == 0)
            {
                break;
            }

            var pick = (long)(rng.NextUInt64() % (ulong)candidateTotal);

            for (var index = 0; index < rates.Length; index++)
            {
                if (values[index] <= WorldRuleSet.AttributeMin || rates[index] == 0)
                {
                    continue;
                }

                if (pick < rates[index])
                {
                    values[index]--;
                    deltas[index]--;

                    break;
                }

                pick -= rates[index];
            }
        }

        return (milli, remainder);
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

        if (input.DevelopmentRemainder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.DevelopmentRemainder,
                "A development remainder is never negative (TRN-10).");
        }

        if (input.DeclineRemainder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.DeclineRemainder,
                "A decline remainder is never negative (TRN-16).");
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
