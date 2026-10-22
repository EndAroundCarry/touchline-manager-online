using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Finance;

/// <summary>
/// Where a club's money is heading this season, composed from what the economy rules already compute (plan toasty-scott sections 2a, 5 and 6).
/// </summary>
/// <remarks>
/// A pure function of four reads, so the finance screen's forecast, wage gauge, state banner and prize cannot disagree with the wage impact of a
/// bid or with the settlement that later pays the money (`INT-1`): they all come out of the same <see cref="ClubFinanceBasis"/>.
/// </remarks>
public static class FinanceOutlooks
{
    /// <summary>Composes the outlook.</summary>
    /// <param name="basis">What the club's money is planned from.</param>
    /// <param name="money">What the club holds and has promised, and how far into the season its division is.</param>
    /// <param name="standing">The club's financial state and its place in the table.</param>
    /// <param name="settledMatchdayOrdinal">The matchdays the world has settled so far, counted across seasons.</param>
    public static FinanceOutlookResponse Compose(
        ClubFinanceBasis basis,
        ClubMoneyPosition money,
        ClubFinanceStanding standing,
        int settledMatchdayOrdinal)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(money);
        ArgumentNullException.ThrowIfNull(standing);

        var income = basis.IncomeBreakdown;
        var cost = basis.CostBreakdown;
        var forecast = income.TotalMinor;
        var wages = basis.SeasonWageMinor;
        var budget = EconomyRuleSet.WageBudgetMinorFor(forecast);
        var inAdministration = standing.State == FinancialStateKind.Administration;

        // The dismissal clock counts whole matchdays across seasons, from the matchday the club entered administration.
        int? matchdaysLeft = inAdministration
            ? Math.Max(0, EconomyRuleSet.AdministrationDismissalMatchdays - (settledMatchdayOrdinal - standing.StateSinceMatchday))
            : null;

        FinancePrizeResponse? prize = standing.Place is { } place && standing.Clubs > 0 && place <= EconomyRuleSet.ClubsPerDivision
            ? new FinancePrizeResponse(place, standing.Clubs, EconomyRuleSet.PrizeMinorFor(basis.TierNumber, place))
            : null;

        return new FinanceOutlookResponse(
            standing.State.ToCode(),
            standing.State != FinancialStateKind.Healthy,
            matchdaysLeft,
            money.MatchdaysPlayed,
            EconomyRuleSet.MatchdaysPerSeason,
            wages,
            budget,
            forecast > 0 ? (int)Math.Min(int.MaxValue, wages * EconomyRuleSet.OneHundredPercentBp / forecast) : 0,
            forecast,
            EconomyRuleSet.OverdraftLimitMinorFor(forecast),
            wages > budget,
            [
                new FinanceForecastLineResponse(LedgerCategory.LeagueTv.ToCode(), income.LeagueTvMinor),
                new FinanceForecastLineResponse(LedgerCategory.SponsorMatchday.ToCode(), income.SponsorMinor),
                new FinanceForecastLineResponse(LedgerCategory.GateReceipt.ToCode(), income.GateMinor),
                new FinanceForecastLineResponse(LedgerCategory.SnackBar.ToCode(), income.SnackBarMinor),
                new FinanceForecastLineResponse(LedgerCategory.Merchandise.ToCode(), income.ShopMinor),
                new FinanceForecastLineResponse(LedgerCategory.Wages.ToCode(), -wages),
                new FinanceForecastLineResponse(LedgerCategory.OperatingCost.ToCode(), -cost.OperatingMinor),
                new FinanceForecastLineResponse(LedgerCategory.StructureUpkeep.ToCode(), -cost.BuildingUpkeepMinor),
                new FinanceForecastLineResponse(LedgerCategory.GroundUpkeep.ToCode(), -cost.GroundUpkeepMinor),
            ],
            prize);
    }
}
