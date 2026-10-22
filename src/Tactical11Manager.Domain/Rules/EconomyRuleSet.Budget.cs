using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Rules;

// The season income forecast, the wage budget and the overdraft limit (plan toasty-scott, section 5).
public static partial class EconomyRuleSet
{
    /// <summary>The share of the season income forecast a wage bill should stay within, in basis points: 70%. A balancing value.</summary>
    public const int WageBudgetBp = 7_000;

    /// <summary>The share of the season income forecast a club may be in the red by, in basis points: 25%. A balancing value.</summary>
    public const int OverdraftLimitBp = 2_500;

    /// <summary>The home matches a club plays in a season.</summary>
    public const int HomeMatchesPerSeason = MatchdaysPerSeason / 2;

    /// <summary>
    /// A season's income at the club's present fans and ground, in minor units, before any prize or bonus: the league's money, the sponsor's fixed
    /// part, and the gate and snack bar of every home match and the shop of every weekly run.
    /// </summary>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="sponsorFixedSeasonMinor">The fixed part of the sponsor's deal, 0 while none is signed.</param>
    /// <param name="seats">The ground's places.</param>
    /// <param name="fans">The club's fans.</param>
    /// <param name="moodBp">The fans' mood, 0 to 10,000.</param>
    /// <param name="snackLevel">The snack bar's level.</param>
    /// <param name="shopLevel">The shop's level.</param>
    /// <remarks>Bonuses and prize money are left out: they are upside, not something a wage bill should be planned against.</remarks>
    public static long SeasonIncomeForecastMinor(
        int tier,
        long sponsorFixedSeasonMinor,
        StadiumSeats seats,
        int fans,
        int moodBp,
        int snackLevel,
        int shopLevel)
    {
        var spectators = StadiumRuleSet.AttendanceFor(seats, fans, moodBp);
        var gate = StadiumRuleSet.GateRevenueMinorFor(seats, tier, fans, moodBp) * HomeMatchesPerSeason;
        var snack = StructureRuleSet.SnackTakingsMinorFor(snackLevel, spectators, 0, tier) * HomeMatchesPerSeason;
        var shop = StructureRuleSet.MerchTakingsMinorFor(shopLevel, fans, Fans.FanMoods.WordFor(moodBp), tier) * MatchdaysPerSeason / MatchdaysPerWeeklyRun;

        return LeagueTvSeasonMinorFor(tier) + sponsorFixedSeasonMinor + gate + snack + shop;
    }

    /// <summary>The wage bill a club should stay within, in minor units: 70% of its season income forecast.</summary>
    /// <param name="forecastMinor">The season income forecast.</param>
    public static long WageBudgetMinorFor(long forecastMinor) => Math.Max(0, forecastMinor) * WageBudgetBp / OneHundredPercentBp;

    /// <summary>How far below zero a club's cash may go before it is in administration, in minor units: a quarter of its season income forecast.</summary>
    /// <param name="forecastMinor">The season income forecast.</param>
    public static long OverdraftLimitMinorFor(long forecastMinor) => Math.Max(0, forecastMinor) * OverdraftLimitBp / OneHundredPercentBp;

    /// <summary>The costs of a season that are not wages, in minor units: the operating cost, the buildings' upkeep and the ground's upkeep.</summary>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="seats">The ground's places.</param>
    /// <param name="snackLevel">The snack bar's level.</param>
    /// <param name="shopLevel">The shop's level.</param>
    public static long FixedCostSeasonMinor(int tier, StadiumSeats seats, int snackLevel, int shopLevel) =>
        OperatingCostSeasonMinorFor(tier)
            + ((StructureRuleSet.UpkeepMinorFor(snackLevel, tier) + StructureRuleSet.UpkeepMinorFor(shopLevel, tier)) * MatchdaysPerSeason / MatchdaysPerWeeklyRun)
            + StadiumRuleSet.GroundUpkeepSeasonMinorFor(seats, tier);
}
