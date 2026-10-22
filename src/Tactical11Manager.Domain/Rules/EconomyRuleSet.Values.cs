namespace Tactical11Manager.Domain.Rules;

// Player values and wages: the player's price, not the club's tier (plan toasty-scott, section 1).
public static partial class EconomyRuleSet
{
    /// <summary>The highest skill, in tenths of a point (the mean of a player's attributes is 20 at most).</summary>
    public const int MaxSkillTenths = 200;

    /// <summary>The lowest skill the price curve reads, in tenths of a point.</summary>
    public const int MinSkillTenths = 10;

    /// <summary>What a player of skill 10 is worth at his peak age, in minor units: 0.75M. A balancing value; the curve is anchored here.</summary>
    public const long ValueAtSkillTenMinor = 75_000_000;

    /// <summary>The least a player is worth, in minor units, so a fee is always a real price.</summary>
    public const long MinimumPlayerValueMinor = 1_000_000;

    /// <summary>The share of his value a player earns in a season, in basis points: 16%. A balancing value.</summary>
    public const int WageShareOfValueBp = 900;

    /// <summary>The least a player earns in a season, in minor units: 20k. A balancing value.</summary>
    public const long SeasonWageFloorMinor = 2_000_000;

    /// <summary>The years under 24 that each add to what the market assumes a player will become.</summary>
    public const int PeakAge = 24;

    /// <summary>What the market assumes a player gains for each year he is under <see cref="PeakAge"/>, in tenths of a skill point (0.6).</summary>
    public const int PerceivedGrowthTenthsPerYear = 6;

    // The skill-to-value curve at whole skill points, then geometric between them so a value moves smoothly as a player trains.
    private static readonly long[] ValueByTenth = BuildValueTable();

    /// <summary>The ratio of a player's value at the next whole skill point to his value at this one, in basis points: ×1.6, steeper above 17.</summary>
    /// <param name="skill">The whole skill point, 1 to 19.</param>
    public static int ValueGrowthBp(int skill) => skill switch
    {
        < 17 => 16_000,
        17 => 17_000,
        18 => 17_000,
        _ => 19_000,
    };

    /// <summary>What a player of a skill is worth at his peak age, in minor units, before the market's view of his age.</summary>
    /// <param name="skillTenths">The mean of his attributes in tenths of a point, so 115 is 11.5.</param>
    public static long ValueAtPeakMinor(int skillTenths) =>
        ValueByTenth[Math.Clamp(skillTenths, MinSkillTenths, MaxSkillTenths) - MinSkillTenths];

    /// <summary>How a player's age shapes what the market pays for him, in basis points.</summary>
    /// <param name="age">The player's age in game years.</param>
    public static int ValueAgeFactorBp(int age)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);

        return age switch
        {
            <= 21 => 13_000,
            <= 23 => 12_000,
            <= 28 => 10_000,
            <= 30 => 7_500,
            <= 32 => 5_000,
            _ => 3_000,
        };
    }

    /// <summary>
    /// What the market takes a player to be, in tenths of a point: his skill plus 0.6 for each year he is under 24, never above 20. It reads
    /// nothing hidden, so a gem is priced like any other young player and a flop like one who is going to arrive.
    /// </summary>
    /// <param name="skillTenths">The mean of his attributes in tenths of a point.</param>
    /// <param name="age">The player's age in game years.</param>
    public static int PerceivedSkillTenths(int skillTenths, int age)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);

        var years = Math.Max(0, PeakAge - age);

        return Math.Clamp(skillTenths + (PerceivedGrowthTenthsPerYear * years), MinSkillTenths, MaxSkillTenths);
    }

    /// <summary>What a player is worth to the market, in minor units: the price of the skill it takes him to be, moved by his age.</summary>
    /// <param name="skillTenths">The mean of his attributes in tenths of a point.</param>
    /// <param name="age">The player's age in game years.</param>
    public static long PlayerValueMinor(int skillTenths, int age)
    {
        var perceived = PerceivedSkillTenths(skillTenths, age);

        return Math.Max(MinimumPlayerValueMinor, ValueAtPeakMinor(perceived) * ValueAgeFactorBp(age) / OneHundredPercentBp);
    }

    /// <summary>The skill a wage is priced on, in tenths: two parts what he is and one part what the market takes him to be.</summary>
    /// <param name="skillTenths">The mean of his attributes in tenths of a point.</param>
    /// <param name="age">The player's age in game years.</param>
    public static int WageSkillTenths(int skillTenths, int age) =>
        ((2 * Math.Clamp(skillTenths, MinSkillTenths, MaxSkillTenths)) + PerceivedSkillTenths(skillTenths, age)) / 3;

    /// <summary>What a player earns in a season, in minor units, before a renewal's factors: 16% of his value at his peak age, never under the floor.</summary>
    /// <param name="skillTenths">The skill the wage is priced on, in tenths of a point.</param>
    public static long SeasonWageMinorForSkill(int skillTenths) =>
        Math.Max(SeasonWageFloorMinor, ValueAtPeakMinor(skillTenths) * WageShareOfValueBp / OneHundredPercentBp);

    /// <summary>What a generated player earns in a season, in minor units, from his whole-point ability.</summary>
    /// <param name="ability">The mean of his attributes, rounded, on the 1–20 scale.</param>
    public static long GeneratedSeasonWageMinorFor(int ability)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ability, WorldRuleSet.AttributeMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ability, WorldRuleSet.AttributeMax);

        return SeasonWageMinorForSkill(ability * 10);
    }

    private static long[] BuildValueTable()
    {
        // anchors[s] is the value at whole skill s; the curve is anchored at skill 10 and grows by ValueGrowthBp for each point up, shrinks down.
        var anchors = new double[WorldRuleSet.AttributeMax + 2];

        anchors[10] = ValueAtSkillTenMinor;

        for (var skill = 9; skill >= 1; skill--)
        {
            anchors[skill] = anchors[skill + 1] * OneHundredPercentBp / ValueGrowthBp(skill);
        }

        for (var skill = 10; skill <= WorldRuleSet.AttributeMax; skill++)
        {
            anchors[skill + 1] = anchors[skill] * ValueGrowthBp(skill) / OneHundredPercentBp;
        }

        var table = new long[MaxSkillTenths - MinSkillTenths + 1];

        for (var tenths = MinSkillTenths; tenths <= MaxSkillTenths; tenths++)
        {
            var whole = tenths / 10;
            var part = (tenths % 10) / 10.0;
            var value = whole >= WorldRuleSet.AttributeMax
                ? anchors[WorldRuleSet.AttributeMax]
                : anchors[whole] * Math.Pow(anchors[whole + 1] / anchors[whole], part);

            // Rounded to whole hundreds of the currency a manager reads, so the last bit of a floating-point result never reaches a price.
            table[tenths - MinSkillTenths] = (long)Math.Round(value / 10_000.0) * 10_000;
        }

        return table;
    }
}
