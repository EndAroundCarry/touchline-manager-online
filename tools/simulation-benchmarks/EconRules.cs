using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.SimulationBenchmarks;

// The economy sandbox (plan toasty-scott, Milestone 1). Every money formula the sandbox needs goes through this one class, which reads the game's own
// rules (EconomyRuleSet, StadiumRuleSet, StructureRuleSet, PlayerValuation, ContractRenewalQuote, FreeAgentTerms). All amounts are minor units. A season
// is 34 settlements, one on each matchday: league money, the sponsor's fixed part and wages are paid in 34 equal parts, and the weekly run's items
// (the shop, building upkeep) are paid a third of a weekly run on each matchday.
//
// The first version of the sandbox (Milestone 0) ran the rules of world-rules-v16 behind an interface; its baseline report is kept outside the repo. Those
// rules were replaced by the v17 rules, so only the v17 rules run here.

/// <summary>The terms a free agent signs on.</summary>
internal sealed record EconFreeAgentQuote(long FeeMinor, long SeasonWageMinor, int Seasons);

internal static class EconRules
{
    public static string Name => $"economy v2 ({WorldRuleSet.Version}, {PlayerValuation.Version})";

    /// <summary>Cash a fresh club of the tier opens with.</summary>
    public static long OpeningCash(int tier) => EconomyRuleSet.OpeningCashMinorFor(tier);

    /// <summary>The season wage the generator gives a squad member of this ability.</summary>
    public static long GeneratedWage(int ability) => EconomyRuleSet.GeneratedSeasonWageMinorFor(ability);

    /// <summary>The season wage a renewal, a listing or a transfer quotes.</summary>
    public static long RenewalWage(ContractRenewalInput input, int seasons) => ContractRenewalQuote.Calculate(input, seasons).SeasonWageMinor;

    /// <summary>What a player is worth to the market: its asking price and the basis of a bid ceiling.</summary>
    public static long Value(int abilityTenths, int age) => PlayerValuation.Estimate(new PlayerValuationInput(abilityTenths, age));

    public static EconFreeAgentQuote FreeAgent(int ability, int age, bool released)
    {
        var offer = released ? FreeAgentTerms.CalculateReleased(ability, age) : FreeAgentTerms.Calculate(ability, age);

        return new EconFreeAgentQuote(offer.MinimumFeeMinor, offer.SeasonWageMinor, offer.ContractSeasons);
    }

    public static long Gate(StadiumSeats seats, int tier, int fans, int moodBp) => StadiumRuleSet.GateRevenueMinorFor(seats, tier, fans, moodBp);

    public static int Attendance(StadiumSeats seats, int fans, int moodBp) => StadiumRuleSet.AttendanceFor(seats, fans, moodBp);

    public static long BuildCostPerPlace(StadiumStand stand, int tier) => StadiumRuleSet.BuildCostMinorFor(stand, tier);

    public static long SnackTakings(int level, int spectators, int bonusBp, int tier) => StructureRuleSet.SnackTakingsMinorFor(level, spectators, bonusBp, tier);

    /// <summary>The shop's takings for one matchday: a third of a weekly run's.</summary>
    public static long MerchTakings(int level, int fans, Tactical11Manager.Domain.Fans.FanMoodWord mood, int tier) =>
        StructureRuleSet.MerchTakingsMinorFor(level, fans, mood, tier) / EconomyRuleSet.MatchdaysPerWeeklyRun;

    /// <summary>A building's upkeep for one matchday.</summary>
    public static long Upkeep(int level, int tier) => EconomyRuleSet.PerMatchday(StructureRuleSet.UpkeepSeasonMinorFor(level, tier));

    public static long UpgradeCost(int targetLevel, int tier) => StructureRuleSet.UpgradeCostMinorFor(targetLevel, tier);

    public static long TvPerMatchday(int tier) => EconomyRuleSet.PerMatchday(EconomyRuleSet.LeagueTvSeasonMinorFor(tier));

    public static long OperatingPerMatchday(int tier) => EconomyRuleSet.PerMatchday(EconomyRuleSet.OperatingCostSeasonMinorFor(tier));

    public static long GroundUpkeepPerMatchday(StadiumSeats seats, int tier) => EconomyRuleSet.PerMatchday(StadiumRuleSet.GroundUpkeepSeasonMinorFor(seats, tier));

    public static long Prize(int tier, int rank) => EconomyRuleSet.PrizeMinorFor(tier, rank);
}
