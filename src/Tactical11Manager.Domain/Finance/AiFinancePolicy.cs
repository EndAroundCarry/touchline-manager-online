using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Finance;

/// <summary>The versioned identity of the AI finance policy (`FIC-8`).</summary>
public static class AiFinancePolicyVersions
{
    /// <summary>The current policy label.</summary>
    public const string Version = "ai-finance-v1";
}

/// <summary>An order for new places in a ground.</summary>
/// <param name="Stand">The kind of place.</param>
/// <param name="Places">How many places to build.</param>
public sealed record AiStandOrder(StadiumStand Stand, int Places);

/// <summary>An order to raise one of the two buildings by a level.</summary>
/// <param name="Shop">Whether the shop (true) or the snack bar (false).</param>
public sealed record AiUpgradeOrder(bool Shop);

/// <summary>What the building policy reads about a club.</summary>
/// <param name="Tier">The tier the club plays in.</param>
/// <param name="Seats">The ground's places.</param>
/// <param name="Fans">The club's fans.</param>
/// <param name="MoodBp">The fans' mood, 0 to 10,000.</param>
/// <param name="RecentSoldBp">What its last home matches sold, each as a share of the ground in basis points, newest last. Up to five.</param>
/// <param name="AvailableMinor">Cash the club can spend now.</param>
/// <param name="WageBillSeasonMinor">The season wage bill, which sets the reserve.</param>
/// <param name="InTheRed">Whether the club is in the red, which bars every order.</param>
/// <param name="SnackLevel">The snack bar's level.</param>
/// <param name="ShopLevel">The shop's level.</param>
public sealed record AiBuildingInput(
    int Tier,
    StadiumSeats Seats,
    int Fans,
    int MoodBp,
    IReadOnlyList<int> RecentSoldBp,
    long AvailableMinor,
    long WageBillSeasonMinor,
    bool InTheRed,
    int SnackLevel,
    int ShopLevel);

/// <summary>
/// What a computer club decides about its sponsor, its objective, its ground and its buildings (`ai-finance-v1`, plan toasty-scott section 7).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the club's shape and its money, so the same club decides the same way. Every choice is one a manager could make, and
/// goes through the writer a manager's choice goes through: the AI has no bypass, only a different author (`INS-12`, `FIN-15`).
/// </para>
/// <para>
/// The sponsor follows the club's temperament and the board's realistic objective. The ground grows when the last five home matches sold out,
/// toward the demand, with at most half of the cash above a reserve of half a season of wages. A building is upgraded when a step pays for itself
/// within about half a season. Nothing is built while the club is in the red.
/// </para>
/// </remarks>
public static class AiFinancePolicy
{
    /// <summary>The home matches whose sales the ground policy reads.</summary>
    public const int SalesWindow = 5;

    /// <summary>The share of the ground the recent matches must have sold, on average, before the club builds, in basis points: 95%.</summary>
    public const int SoldOutBp = 9_500;

    /// <summary>The share of the cash above the reserve the club spends on places at one time, in basis points: half.</summary>
    public const int BuildBudgetBp = 5_000;

    /// <summary>The least growth of one order, as a share of the ground in basis points: 5%.</summary>
    public const int MinimumGrowthBp = 500;

    /// <summary>The longest, as a share of a season, a building upgrade may take to pay for itself: half.</summary>
    public const int UpgradePaybackBp = 5_000;

    /// <summary>The offer a club signs: a cautious club the steady one, a balanced club the balanced one, a bold club the performance one.</summary>
    /// <param name="temperament">The club's temperament.</param>
    public static SponsorOfferKind ChooseOffer(AiTemperament temperament) => temperament switch
    {
        AiTemperament.Cautious => SponsorOfferKind.Steady,
        AiTemperament.Bold => SponsorOfferKind.Performance,
        _ => SponsorOfferKind.Balanced,
    };

    /// <summary>The objective a club picks: the board's realistic one.</summary>
    /// <param name="strengthRank">The club's rank by squad strength in its division, 1 for the strongest.</param>
    public static SeasonObjective ChooseObjective(int strengthRank) => EconomyRuleSet.RealisticObjectiveFor(strengthRank);

    /// <summary>The cash a club keeps back before it builds: half a season of wages.</summary>
    /// <param name="wageBillSeasonMinor">The season wage bill.</param>
    public static long ReserveMinorFor(long wageBillSeasonMinor) => wageBillSeasonMinor / 2;

    /// <summary>Decides whether a club adds places to its ground, and which.</summary>
    /// <param name="input">The club.</param>
    public static IReadOnlyList<AiStandOrder> ChooseStands(AiBuildingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var capacity = input.Seats.Total;

        if (input.InTheRed
            || capacity >= StadiumRuleSet.MaxCapacity
            || input.RecentSoldBp.Count < SalesWindow
            || input.RecentSoldBp.TakeLast(SalesWindow).Average() < SoldOutBp)
        {
            return [];
        }

        var budget = Math.Max(0, input.AvailableMinor - ReserveMinorFor(input.WageBillSeasonMinor)) * BuildBudgetBp / EconomyRuleSet.OneHundredPercentBp;
        var demand = StadiumRuleSet.MatchdayDemandFor(input.Fans, input.MoodBp);
        var wanted = Math.Max(demand - capacity, capacity * MinimumGrowthBp / EconomyRuleSet.OneHundredPercentBp);
        var perPlace = StadiumStands.All.Sum(stand => (long)StadiumRuleSet.DemandShareBp(stand) * StadiumRuleSet.BuildCostMinorFor(stand, input.Tier))
            / EconomyRuleSet.OneHundredPercentBp;
        var places = (int)Math.Min(Math.Min(wanted, StadiumRuleSet.MaxCapacity - capacity), perPlace <= 0 ? wanted : budget / perPlace);

        if (places <= 0)
        {
            return [];
        }

        return
        [
            .. StadiumStands.All
                .Select(stand => new AiStandOrder(stand, places * StadiumRuleSet.DemandShareBp(stand) / EconomyRuleSet.OneHundredPercentBp))
                .Where(order => order.Places > 0),
        ];
    }

    /// <summary>Decides which buildings a club upgrades: those whose next level pays for itself within about half a season.</summary>
    /// <param name="input">The club.</param>
    public static IReadOnlyList<AiUpgradeOrder> ChooseUpgrades(AiBuildingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.InTheRed)
        {
            return [];
        }

        var reserve = ReserveMinorFor(input.WageBillSeasonMinor);
        var available = input.AvailableMinor;
        var spectators = StadiumRuleSet.AttendanceFor(input.Seats, input.Fans, input.MoodBp);
        var word = Fans.FanMoods.WordFor(input.MoodBp);
        var orders = new List<AiUpgradeOrder>();

        if (input.SnackLevel < StructureRuleSet.MaxLevel)
        {
            var next = input.SnackLevel + 1;
            var gain = (StructureRuleSet.SnackTakingsMinorFor(next, spectators, 0, input.Tier) - StructureRuleSet.SnackTakingsMinorFor(input.SnackLevel, spectators, 0, input.Tier))
                * EconomyRuleSet.HomeMatchesPerSeason;

            if (PaysBack(input, next, gain, available, reserve))
            {
                orders.Add(new AiUpgradeOrder(Shop: false));
                available -= StructureRuleSet.UpgradeCostMinorFor(next, input.Tier);
            }
        }

        if (input.ShopLevel < StructureRuleSet.MaxLevel)
        {
            var next = input.ShopLevel + 1;
            var gain = (StructureRuleSet.MerchTakingsMinorFor(next, input.Fans, word, input.Tier) - StructureRuleSet.MerchTakingsMinorFor(input.ShopLevel, input.Fans, word, input.Tier))
                * EconomyRuleSet.MatchdaysPerSeason / EconomyRuleSet.MatchdaysPerWeeklyRun;

            if (PaysBack(input, next, gain, available, reserve))
            {
                orders.Add(new AiUpgradeOrder(Shop: true));
            }
        }

        return orders;
    }

    private static bool PaysBack(AiBuildingInput input, int nextLevel, long seasonGain, long available, long reserve)
    {
        var cost = StructureRuleSet.UpgradeCostMinorFor(nextLevel, input.Tier);
        var upkeep = StructureRuleSet.UpkeepSeasonMinorFor(nextLevel, input.Tier) - StructureRuleSet.UpkeepSeasonMinorFor(nextLevel - 1, input.Tier);
        var net = seasonGain - upkeep;

        return net > 0
            && cost <= net * UpgradePaybackBp / EconomyRuleSet.OneHundredPercentBp
            && available - cost >= reserve;
    }
}
