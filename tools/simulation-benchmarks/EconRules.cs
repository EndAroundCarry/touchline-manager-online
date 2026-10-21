using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.SimulationBenchmarks;

// The economy sandbox (plan toasty-scott, Milestone 0). Every money formula the sandbox needs goes through this one seam, so Milestone 1 can add
// the v2 rules beside today's and run the same world on both. All amounts are minor units; "per week" means one of the season's 34 settlements
// (today the weekly finance run, in v2 the matchday), so a season is 34 of them either way.

/// <summary>The terms a free agent signs on.</summary>
internal sealed record EconFreeAgentQuote(long FeeMinor, long WagePerWeekMinor, int Seasons);

internal interface IEconRules
{
    string Name { get; }

    /// <summary>Cash a fresh club of the tier opens with.</summary>
    long OpeningCash(int tier);

    /// <summary>The wage the generator gives a squad member of this ability.</summary>
    long GeneratedWagePerWeek(int ability, int tier);

    /// <summary>The wage a renewal, a listing or a transfer quotes.</summary>
    long RenewalWagePerWeek(ContractRenewalInput input, int seasons);

    /// <summary>What a player is worth to an AI club: its asking price and the basis of its bid ceiling.</summary>
    long Value(int ability, int potential, int age, int tier);

    EconFreeAgentQuote FreeAgent(int ability, int potential, int age, int referenceTier, bool released);

    /// <summary>The fixed income a club gets each week from its league and its sponsor.</summary>
    long FixedIncomePerWeek(int tier);

    long OperatingPerWeek(int tier);

    long Gate(StadiumSeats seats, int tier, int fans, int moodBp);

    int Attendance(StadiumSeats seats, int fans, int moodBp);

    long BuildCostPerPlace(StadiumStand stand, int tier);

    long SnackTakings(int level, int spectators, int bonusBp);

    long MerchTakings(int level, int fans, FanMoodWord mood);

    long Upkeep(int level, int tier);

    long UpgradeCost(int targetLevel, int tier);

    /// <summary>The prize money for a final place in a division of the tier.</summary>
    long Prize(int tier, int rank);

    /// <summary>
    /// How far below zero a club's cash may go before it is bailed out. Today nothing may go below zero, so the limit is zero and a shortfall is an
    /// emergency grant (FIN-16).
    /// </summary>
    long OverdraftLimit(long seasonIncomeForecastMinor);
}

/// <summary>The rules as they are in the game today (<c>world-rules-v15</c>, <c>player-valuation-v1</c>).</summary>
internal sealed class TodayEconRules : IEconRules
{
    public string Name => "today (world-rules-v15)";

    public long OpeningCash(int tier) => WorldRuleSet.OpeningCashMinorForTier(tier);

    public long GeneratedWagePerWeek(int ability, int tier) => WorldRuleSet.GeneratedWeeklyWageMinorFor(ability, tier);

    public long RenewalWagePerWeek(ContractRenewalInput input, int seasons) => ContractRenewalQuote.Calculate(input, seasons).WeeklyWageMinor;

    public long Value(int ability, int potential, int age, int tier) =>
        PlayerValuation.Estimate(new PlayerValuationInput(ability, potential, age, tier));

    public EconFreeAgentQuote FreeAgent(int ability, int potential, int age, int referenceTier, bool released)
    {
        var offer = released
            ? FreeAgentTerms.CalculateReleased(ability, potential, age, referenceTier)
            : FreeAgentTerms.Calculate(ability, age, referenceTier);

        return new EconFreeAgentQuote(offer.MinimumFeeMinor, offer.WeeklyWageMinor, offer.ContractSeasons);
    }

    public long FixedIncomePerWeek(int tier) => WorldRuleSet.WeeklySponsorshipMinorForTier(tier);

    public long OperatingPerWeek(int tier) => WorldRuleSet.WeeklyOperatingCostMinorForTier(tier);

    public long Gate(StadiumSeats seats, int tier, int fans, int moodBp) => StadiumRuleSet.GateRevenueMinorFor(seats, tier, fans, moodBp);

    public int Attendance(StadiumSeats seats, int fans, int moodBp) => StadiumRuleSet.AttendanceFor(seats, fans, moodBp);

    public long BuildCostPerPlace(StadiumStand stand, int tier) => StadiumRuleSet.BuildCostMinorFor(stand, tier);

    public long SnackTakings(int level, int spectators, int bonusBp) => StructureRuleSet.SnackTakingsMinorFor(level, spectators, bonusBp);

    public long MerchTakings(int level, int fans, FanMoodWord mood) => StructureRuleSet.MerchTakingsMinorFor(level, fans, mood);

    public long Upkeep(int level, int tier) => StructureRuleSet.UpkeepMinorFor(level, tier);

    public long UpgradeCost(int targetLevel, int tier) => StructureRuleSet.UpgradeCostMinorFor(targetLevel, tier);

    public long Prize(int tier, int rank) => WorldRuleSet.PositionAwardMinorFor(tier, rank);

    public long OverdraftLimit(long seasonIncomeForecastMinor) => 0;
}
