using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Finance;

/// <summary>How a signing or a renewal sits with the club's money (`WAGE-*`, plan toasty-scott section 5).</summary>
public enum WageImpactLevel
{
    /// <summary>The wage bill stays within the budget and the season ends with cash to spare.</summary>
    Fits = 0,

    /// <summary>The wage bill goes over the budget, but the season still ends with cash at or above zero.</summary>
    Stretching = 1,

    /// <summary>The club would be in the red before the season ends.</summary>
    Danger = 2,

    /// <summary>The club would end the season deeper in the red than its overdraft limit.</summary>
    AdministrationRisk = 3,
}

/// <summary>What the wage impact reads about the club, all in minor units.</summary>
/// <param name="CashMinor">The club's cash now.</param>
/// <param name="FeeMinor">The fee the deal costs now, 0 for a renewal.</param>
/// <param name="AddedSeasonWageMinor">What the deal adds to the season's wages: the whole wage of a signing, the rise of a renewal.</param>
/// <param name="WageBillSeasonMinor">The season wage bill before the deal.</param>
/// <param name="ForecastSeasonIncomeMinor">The season income forecast (<see cref="EconomyRuleSet.SeasonIncomeForecastMinor"/>).</param>
/// <param name="FixedCostSeasonMinor">The season's costs that are not wages (<see cref="EconomyRuleSet.FixedCostSeasonMinor"/>).</param>
/// <param name="MatchdaysPlayed">The matchdays the club has played this season, 0 to 34.</param>
public sealed record WageImpactInput(
    long CashMinor,
    long FeeMinor,
    long AddedSeasonWageMinor,
    long WageBillSeasonMinor,
    long ForecastSeasonIncomeMinor,
    long FixedCostSeasonMinor,
    int MatchdaysPlayed);

/// <summary>The wage impact a bid form or a renewal quote shows, computed by the server for the viewer's club.</summary>
/// <param name="Level">How the deal sits with the club's money.</param>
/// <param name="WageBillAfterMinor">The season wage bill with the deal.</param>
/// <param name="WageBudgetMinor">The budget the bill is held against: 70% of the forecast.</param>
/// <param name="WageShareBp">The bill with the deal as a share of the forecast, in basis points.</param>
/// <param name="SeasonEndCashMinor">The cash the club would hold at the season's end after the fee, at today's rates.</param>
/// <param name="RedFromMatchday">The matchday (1 to 34) the club would first be in the red, or null when it would not be in the red within the season.</param>
/// <param name="NeedsConfirmation">Whether the form asks for a confirm tick: for Danger and Administration risk. The deal is never refused for it.</param>
public sealed record WageImpactResult(
    WageImpactLevel Level,
    long WageBillAfterMinor,
    long WageBudgetMinor,
    int WageShareBp,
    long SeasonEndCashMinor,
    int? RedFromMatchday,
    bool NeedsConfirmation);

/// <summary>
/// The warning a bid or a renewal gets about its wage (`WAGE-*`). Never a block: the only thing the rules stop is a bid the cash cannot cover
/// (`FIN-10`) and any bid while the club is in the red.
/// </summary>
public static class WageImpact
{
    /// <summary>Reads a deal against the club's money.</summary>
    /// <param name="input">The club's money and the deal.</param>
    /// <param name="overdraftLimitMinor">The club's overdraft limit.</param>
    public static WageImpactResult Evaluate(WageImpactInput input, long overdraftLimitMinor)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(input.MatchdaysPlayed);

        var played = Math.Min(input.MatchdaysPlayed, EconomyRuleSet.MatchdaysPerSeason);
        var remaining = EconomyRuleSet.MatchdaysPerSeason - played;
        var billAfter = input.WageBillSeasonMinor + input.AddedSeasonWageMinor;
        var budget = EconomyRuleSet.WageBudgetMinorFor(input.ForecastSeasonIncomeMinor);
        var share = input.ForecastSeasonIncomeMinor <= 0
            ? EconomyRuleSet.OneHundredPercentBp * 10
            : (int)Math.Min(
                EconomyRuleSet.OneHundredPercentBp * 10L,
                billAfter * EconomyRuleSet.OneHundredPercentBp / input.ForecastSeasonIncomeMinor);

        var netSeason = input.ForecastSeasonIncomeMinor - input.FixedCostSeasonMinor - billAfter;
        var cashAfterFee = input.CashMinor - input.FeeMinor;
        var seasonEnd = cashAfterFee + (netSeason * remaining / EconomyRuleSet.MatchdaysPerSeason);

        var level = seasonEnd < -overdraftLimitMinor
            ? WageImpactLevel.AdministrationRisk
            : seasonEnd < 0
                ? WageImpactLevel.Danger
                : billAfter > budget
                    ? WageImpactLevel.Stretching
                    : WageImpactLevel.Fits;

        return new WageImpactResult(
            level,
            billAfter,
            budget,
            share,
            seasonEnd,
            RedFromMatchdayOf(cashAfterFee, netSeason, played, remaining),
            level >= WageImpactLevel.Danger);
    }

    private static int? RedFromMatchdayOf(long cashAfterFee, long netSeason, int played, int remaining)
    {
        if (cashAfterFee < 0)
        {
            return played + 1;
        }

        var netPerMatchday = netSeason / EconomyRuleSet.MatchdaysPerSeason;

        if (netPerMatchday >= 0)
        {
            return null;
        }

        // The first matchday whose settlement leaves the cash under zero.
        var burn = -netPerMatchday;
        var matchdays = Math.Min(remaining + 1L, (cashAfterFee / burn) + 1);

        return matchdays > remaining ? null : played + (int)matchdays;
    }
}
