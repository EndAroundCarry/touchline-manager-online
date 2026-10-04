namespace TouchlineManager.Domain.Squad.Training;

/// <summary>
/// How fast a player of a given age improves, and how fast each attribute ages (`TRN-14`, `TRN-16`).
/// </summary>
/// <remarks>
/// Growth never reaches zero: a 35-year-old can still improve a trained skill, very slowly. Decline starts
/// at an age that depends on the attribute — physical first, technical later, mental and goalkeeping last —
/// and grows by a fixed slope for every year past that age.
/// </remarks>
public static class TrainingAgeCurve
{
    /// <summary>How many progression days make one season, which turns an annual figure into a daily one.</summary>
    public const int TrainingDaysPerSeason = 86;

    /// <summary>The age up to which a player trains at full speed.</summary>
    public const int PeakGrowthAge = 19;

    /// <summary>The last age on the linear part of the curve.</summary>
    public const int LinearGrowthEndAge = 25;

    /// <summary>The smallest growth factor, in permille, so no age stops improving altogether.</summary>
    public const int FloorPermille = 20;

    private const int FullPermille = 1_000;
    private const int LinearStepPermille = 100;
    private const int PostPeakNumerator = 3;
    private const int PostPeakDenominator = 4;

    private static readonly Dictionary<AttributeName, (int StartAge, int SlopePermille)> Decline = BuildDecline();

    /// <summary>Gets the growth factor for an age, in permille of the peak. Non-increasing, never zero.</summary>
    /// <param name="age">The player's age in whole game years.</param>
    public static int GrowthPermille(int age)
    {
        if (age <= PeakGrowthAge)
        {
            return FullPermille;
        }

        if (age <= LinearGrowthEndAge)
        {
            return FullPermille - ((age - PeakGrowthAge) * LinearStepPermille);
        }

        var factor = FullPermille - ((LinearGrowthEndAge - PeakGrowthAge) * LinearStepPermille);

        for (var year = LinearGrowthEndAge; year < age; year++)
        {
            factor = factor * PostPeakNumerator / PostPeakDenominator;
        }

        return Math.Max(FloorPermille, factor);
    }

    /// <summary>
    /// Gets how many thousandths of a point an attribute loses in a year at an age, before any shielding.
    /// </summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="age">The player's age in whole game years.</param>
    /// <returns>Zero when the attribute has not started to decline.</returns>
    public static int AnnualDeclineMilli(AttributeName attribute, int age)
    {
        if (!Decline.TryGetValue(attribute, out var profile))
        {
            return 0;
        }

        var years = Math.Max(0, age - profile.StartAge + 1);

        return profile.SlopePermille * years;
    }

    private static Dictionary<AttributeName, (int StartAge, int SlopePermille)> BuildDecline()
    {
        var table = new Dictionary<AttributeName, (int StartAge, int SlopePermille)>();

        void Set(int startAge, int slopePermille, params AttributeName[] names)
        {
            foreach (var name in names)
            {
                table[name] = (startAge, slopePermille);
            }
        }

        Set(28, 300, AttributeName.Pace, AttributeName.Acceleration);
        Set(29, 250, AttributeName.Agility);
        Set(30, 200, AttributeName.Stamina, AttributeName.JumpingReach);
        Set(31, 150, AttributeName.Strength);

        Set(29, 100, AttributeName.Dribbling, AttributeName.Tackling, AttributeName.Heading);
        Set(
            32,
            80,
            AttributeName.Finishing,
            AttributeName.Passing,
            AttributeName.Crossing,
            AttributeName.FirstTouch,
            AttributeName.Technique,
            AttributeName.SetPieces,
            AttributeName.Marking);

        Set(31, 100, AttributeName.WorkRate);

        Set(32, 200, AttributeName.Reflexes);
        Set(34, 120, AttributeName.Handling, AttributeName.OneOnOnes, AttributeName.AerialAbility);

        return table;
    }
}
