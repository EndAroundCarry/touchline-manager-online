namespace Tactical11Manager.Domain.Rules;

/// <summary>
/// The economy rules of the second finance model (plan toasty-scott, `world-rules-v18`): what a player is worth and paid, what a club earns and
/// spends in a season, how a sponsor and a season objective pay, how a club's standing moves, and when a club is in the red.
/// </summary>
/// <remarks>
/// <para>
/// One place for every value, split into partial files by subject (values and wages, income, sponsor and objective, standing, the budget and
/// the wage impact, debt, takeover). The values are the starting shape the economy sandbox tunes (<c>tools/simulation-benchmarks</c>, mode
/// <c>econ</c>), so a retune changes a number here and nothing else (`RULE-1`).
/// </para>
/// <para>
/// Money is an integer count of minor units (`FIN-2`): the figures a manager reads are these divided by 100, so <c>400_000_000</c> is 4M. A
/// season is <see cref="MatchdaysPerSeason"/> matchdays and every season amount is paid or earned in that many equal parts, so a season figure
/// and a matchday figure never disagree.
/// </para>
/// <para>
/// A club's tier does not change what a player costs: the same player is worth and paid the same everywhere. It scales what a club earns and
/// spends, by <see cref="TierFactorBp"/> for league money and fixed costs, and by <see cref="PriceLevelBp"/> for what a spectator or a fan
/// pays.
/// </para>
/// </remarks>
public static partial class EconomyRuleSet
{
    /// <summary>One hundred per cent, in basis points.</summary>
    public const int OneHundredPercentBp = 10_000;

    /// <summary>The matchdays in a season, which is also the number of equal parts a season amount is paid in (`CAL-1`).</summary>
    public const int MatchdaysPerSeason = WorldRuleSet.MatchdaysPerSeason;

    /// <summary>The matchdays the weekly finance run settles at once: the three kickoff days of a week (`CAL-2`).</summary>
    public const int MatchdaysPerWeeklyRun = 3;

    /// <summary>
    /// The weekly finance runs in a season: one for each week of the season cycle (`CAL-6`), so a season of 30 matchdays is ten match weeks and a
    /// rollover week, and a season amount paid by the week is paid in exactly this many parts.
    /// </summary>
    public const int WeeklyRunsPerSeason = WorldRuleSet.SeasonCycleWeeks;

    /// <summary>The fewest clubs in a division; the table the prize money and the places are read from (`WORLD-4`).</summary>
    public const int ClubsPerDivision = WorldRuleSet.ClubsPerDivision;

    /// <summary>Opening cash for a tier-1 club, in minor units: 5M, about 40% of its first season's income. A balancing value.</summary>
    public const long OpeningCashMinorTier1 = 500_000_000;

    /// <summary>The factor a tier's league money and fixed costs are scaled by, in basis points: 1, 0.4, 0.16, 0.064, and 90% of tier 4 from tier 5 on.</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    /// <remarks>
    /// Tier 5 and every tier below use 90% of tier 4, flat. Wages follow the player and have a floor, and a tier's mean skill bottoms out, so
    /// income that kept shrinking would make every deep club bankrupt by construction. It also replaces a shift by the tier, which C# masks to
    /// six bits, so a tier-65 club would have received tier-1 money.
    /// </remarks>
    public static int TierFactorBp(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        return tier switch
        {
            1 => 10_000,
            2 => 4_000,
            3 => 1_600,
            4 => 640,
            _ => 576,
        };
    }

    /// <summary>
    /// The factor what a spectator or a fan spends is scaled by, in basis points: 1, 0.533, 0.284, 0.152, and 90% of tier 4 from tier 5 on.
    /// </summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    /// <remarks>
    /// A division's crowd is three quarters of the division above's (`FAN-1`), so a price level of 0.4 ÷ 0.75 = 0.533 a tier makes the gate and the
    /// buildings fall by the same 0.4 a tier as the league's money: every tier's income has the same make-up, and a prize is the same share of it.
    /// </remarks>
    public static int PriceLevelBp(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        return tier switch
        {
            1 => 10_000,
            2 => 5_333,
            3 => 2_844,
            4 => 1_517,
            _ => 1_365,
        };
    }

    /// <summary>Scales a tier-1 amount by <see cref="TierFactorBp"/>; never below one minor unit.</summary>
    /// <param name="tier1Minor">The amount at a tier-1 club, in minor units.</param>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long ByTier(long tier1Minor, int tier) => Math.Max(1, tier1Minor * TierFactorBp(tier) / OneHundredPercentBp);

    /// <summary>Scales a tier-1 amount by <see cref="PriceLevelBp"/>; never below one minor unit.</summary>
    /// <param name="tier1Minor">The amount at a tier-1 club, in minor units.</param>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long ByPriceLevel(long tier1Minor, int tier) => Math.Max(1, tier1Minor * PriceLevelBp(tier) / OneHundredPercentBp);

    /// <summary>Opening cash for a club in the given tier, in minor units.</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long OpeningCashMinorFor(int tier) => ByTier(OpeningCashMinorTier1, tier);

    /// <summary>One matchday's part of a season amount, in minor units.</summary>
    /// <param name="seasonMinor">The amount for the whole season.</param>
    public static long PerMatchday(long seasonMinor) => seasonMinor / MatchdaysPerSeason;

    /// <summary>
    /// The part of a season amount the weekly finance run settles: a <see cref="WeeklyRunsPerSeason"/>th (`CON-2`). Used where the contract
    /// still stores a weekly wage; it goes when the contract stores the season wage.
    /// </summary>
    /// <param name="seasonMinor">The amount for the whole season.</param>
    public static long PerWeeklyRun(long seasonMinor) => seasonMinor / WeeklyRunsPerSeason;
}
