using System.Globalization;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Squad.Training;

/// <summary>
/// Everything one player's day of training needs, so the progression has no ambient state (`TRN-9`).
/// </summary>
/// <remarks>
/// The hidden potential is passed in rather than read from the player, because it is what caps growth and
/// the calculator is deliberately pure: it takes the ceiling as a value so a test can vary it.
/// </remarks>
/// <param name="PlayerId">The player's identity, which seeds the day's draw order (`TRN-9`).</param>
/// <param name="Age">The player's age in game years, which is the development curve.</param>
/// <param name="Attributes">The player's current attributes.</param>
/// <param name="Potential">The hidden development ceiling, on the displayed scale (`TRN-9`).</param>
/// <param name="TeamFocus">The club's team focus (`TRN-1`).</param>
/// <param name="Intensity">How hard the club trains.</param>
/// <param name="IndividualFocus">The player's optional individual focus, when one is set (`TRN-2`).</param>
/// <param name="ConditionBp">Condition in basis points (`TRN-5`).</param>
/// <param name="FatigueBp">Fatigue in basis points (`TRN-6`).</param>
/// <param name="MoraleBp">Morale in basis points (`TRN-7`).</param>
/// <param name="MatchSharpnessBp">Match sharpness in basis points.</param>
/// <param name="DevelopmentRemainder">The partial development carried over from earlier days (`TRN-10`).</param>
/// <param name="Day">The day being progressed, which is part of the draw's identity.</param>
public sealed record DailyProgressionInput(
    Guid PlayerId,
    int Age,
    PlayerAttributeSet Attributes,
    int Potential,
    TrainingFocus TeamFocus,
    TrainingIntensity Intensity,
    AttributeFamily? IndividualFocus,
    int ConditionBp,
    int FatigueBp,
    int MoraleBp,
    int MatchSharpnessBp,
    int DevelopmentRemainder,
    DateOnly Day);

/// <summary>The state a player leaves a training day in (`TRN-9`, `TRN-10`).</summary>
/// <param name="Attributes">The attributes after development, never outside the displayed scale (`TRN-4`).</param>
/// <param name="ConditionBp">Condition in basis points.</param>
/// <param name="FatigueBp">Fatigue in basis points.</param>
/// <param name="MoraleBp">Morale in basis points.</param>
/// <param name="MatchSharpnessBp">Match sharpness in basis points.</param>
/// <param name="DevelopmentRemainder">The partial development carried into the next day (`TRN-10`).</param>
public sealed record DailyProgressionOutcome(
    PlayerAttributeSet Attributes,
    int ConditionBp,
    int FatigueBp,
    int MoraleBp,
    int MatchSharpnessBp,
    int DevelopmentRemainder);

/// <summary>
/// One day of a player's training: recovery, and bounded deterministic development (`TRN-1`, `TRN-2`,
/// `TRN-9`, `TRN-10`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the player's identity, the day, the training in force, and the hidden potential.
/// It reads no clock, no database, and no global random source, so the same inputs always produce the same
/// day and the whole world's progression can be replayed from the seed (`TRN-9`).
/// </para>
/// <para>
/// <b>Development.</b> Each day yields fractional development measured in thousandths of an attribute
/// point. The fraction is added to <see cref="PlayerState.DevelopmentRemainder"/> and every whole point is
/// spent on one of the attributes the training emphasises, in a draw order derived from the player and the
/// day. Carrying the fraction forward is what stops a day being lost to rounding (`TRN-10`), and spending
/// a point only where the value is below the player's potential is what keeps growth bounded and monotonic
/// — training never lowers an attribute and never pushes one above forty or below one (`TRN-4`).
/// </para>
/// <para>
/// <b>Trade-off.</b> Intensity buys development and costs condition and fatigue; recovery focus buys
/// freshness and costs development. Every coefficient is bounded, and none multiplies a rating without a
/// counter-cost (`TRN-1`, master plan §3.8).
/// </para>
/// <para>
/// <b>Version.</b> <see cref="Version"/> is bumped whenever a coefficient, the family mapping, or the draw
/// order changes, because a change to any of them rewrites what a replay would produce (`FIC-8`,
/// `TRN-9`).
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
    public const string Version = "training-v1";

    /// <summary>How many thousandths of an attribute point make one displayed point (`TRN-10`).</summary>
    public const int DevelopmentBasis = 1_000;

    /// <summary>The most a single attribute's daily draw varies from its base growth.</summary>
    public const int DevelopmentJitter = 4;

    /// <summary>The age at which a player no longer develops; older players simply maintain (`TRN-9`).</summary>
    public const int DevelopmentAgeLimit = 25;

    private const int FatigueRecoveryBp = 400;
    private const int RecoveryFocusBonusBp = 400;
    private const int ConditionRecoveryBp = 600;
    private const int SharpnessGainBp = 40;
    private const int MoraleDriftBp = 20;

    /// <summary>
    /// Gets the attribute families a player's day emphasises (`TRN-1`, `TRN-2`).
    /// </summary>
    /// <remarks>
    /// An individual focus steers the emphasis to its own family rather than adding to the team's, which is
    /// what makes the optional instruction a decision (`TRN-2`): the team focus still governs load and
    /// recovery, but the player develops where the manager pointed them.
    /// </remarks>
    /// <param name="focus">The club's team focus.</param>
    /// <param name="individualFocus">The player's individual focus, when one is set.</param>
    /// <returns>The emphasised families; empty for a recovery focus, which develops nothing.</returns>
    public static IReadOnlyList<AttributeFamily> FamiliesFor(
        TrainingFocus focus,
        AttributeFamily? individualFocus)
    {
        if (individualFocus is { } family)
        {
            return [family];
        }

        return focus switch
        {
            TrainingFocus.Balanced =>
                [AttributeFamily.Technical, AttributeFamily.Mental, AttributeFamily.Physical, AttributeFamily.Goalkeeping],
            TrainingFocus.Fitness => [AttributeFamily.Physical],
            TrainingFocus.Attacking => [AttributeFamily.Technical],
            TrainingFocus.Defending => [AttributeFamily.Technical],
            TrainingFocus.Technical => [AttributeFamily.Technical],
            TrainingFocus.Tactical => [AttributeFamily.Mental],
            _ => [],
        };
    }

    /// <summary>Advances one player by one day of training.</summary>
    /// <param name="input">The player's day.</param>
    /// <returns>The state the player leaves the day in.</returns>
    public static DailyProgressionOutcome Advance(DailyProgressionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

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

        if (input.DevelopmentRemainder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.DevelopmentRemainder,
                "A development remainder is never negative (TRN-10).");
        }

        var rng = new Pcg32(DeterministicDigest.SeedOf(
            input.PlayerId.ToString("D", CultureInfo.InvariantCulture),
            input.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Version));

        var condition = Clamp(input.ConditionBp + ConditionRecoveryBp - ConditionCost(input.Intensity));
        var fatigue = Clamp(input.FatigueBp - FatigueRecovery(input.TeamFocus) + FatigueLoad(input.Intensity));
        var sharpness = Clamp(input.MatchSharpnessBp + SharpnessGainBp);
        var morale = Drift(input.MoraleBp, rng);

        var (attributes, remainder) = Develop(input, rng);

        return new DailyProgressionOutcome(attributes, condition, fatigue, morale, sharpness, remainder);
    }

    /// <summary>Spends the day's development and carries the fraction forward (`TRN-4`, `TRN-10`).</summary>
    private static (PlayerAttributeSet Attributes, int Remainder) Develop(DailyProgressionInput input, Pcg32 rng)
    {
        var families = FamiliesFor(input.TeamFocus, input.IndividualFocus);
        var ageFactor = input.Age < DevelopmentAgeLimit ? DevelopmentAgeLimit - input.Age : 0;

        if (families.Count == 0 || ageFactor == 0)
        {
            return (input.Attributes, input.DevelopmentRemainder);
        }

        var capacity = Math.Min(WorldRuleSet.AttributeMax, input.Potential);
        var values = input.Attributes.Values.ToArray();
        var eligible = new List<int>();

        foreach (var name in AttributeNames.All)
        {
            var index = (int)name;

            if (families.Contains(AttributeNames.FamilyOf(name)) && values[index] < capacity)
            {
                eligible.Add(index);
            }
        }

        if (eligible.Count == 0)
        {
            return (input.Attributes, input.DevelopmentRemainder);
        }

        var gain = 0L;

        foreach (var _ in eligible)
        {
            gain += (ageFactor * IntensityFactor(input.Intensity)) + rng.NextInt(DevelopmentJitter + 1);
        }

        var accumulated = input.DevelopmentRemainder + gain;
        var points = (int)(accumulated / DevelopmentBasis);
        var remainder = (int)(accumulated % DevelopmentBasis);

        if (points == 0)
        {
            return (input.Attributes, remainder);
        }

        // A draw order derived from the player and the day, so the day's whole points spread across the
        // emphasised attributes rather than always landing on the first one.
        var order = new List<int>(eligible);

        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = rng.NextInt(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var unplaced = points;
        var cursor = 0;
        var guard = points + order.Count;

        while (unplaced > 0 && guard > 0)
        {
            var index = order[cursor % order.Count];

            if (values[index] < capacity)
            {
                values[index]++;
                unplaced--;
            }

            cursor++;
            guard--;
        }

        return (PlayerAttributeSet.FromValues(values), remainder);
    }

    private static int FatigueRecovery(TrainingFocus focus) =>
        focus == TrainingFocus.Recovery ? FatigueRecoveryBp + RecoveryFocusBonusBp : FatigueRecoveryBp;

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

    private static int IntensityFactor(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Light => 1,
        TrainingIntensity.Intense => 3,
        _ => 2,
    };

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
