using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Rules;

/// <summary>
/// The stadium rules: how big a ground may grow, what a place costs to build and to sit in, and how many
/// people turn up (game rules §13.5, `STAD-*`).
/// </summary>
/// <remarks>
/// <para>
/// Kept beside <see cref="WorldRuleSet"/> rather than inside it because the stadium is a self-contained
/// model — a size, four kinds of place, a price and a cost for each, and a demand curve — and the rule set
/// is already the longest file in the domain. It is still part of that rule set: every value here is a
/// balancing value stamped by <see cref="WorldRuleSet.Version"/>, and money scales with the division by the
/// same <see cref="WorldRuleSet.TierScalingFactor"/> as every other baseline, so a pyramid's grounds stay
/// proportionate to its clubs' wallets.
/// </para>
/// <para>
/// Money is an integer count of minor units (`FIN-2`). The tier-1 figures below are therefore hundredths of
/// the currency a manager reads: a standing ticket of 800 is 8.00.
/// </para>
/// </remarks>
public static class StadiumRuleSet
{
    /// <summary>How many seats a stadium level spans (`STAD-1`). A ground's level steps every 5,000 places.</summary>
    public const int SeatsPerLevel = 5_000;

    /// <summary>The highest stadium level (`STAD-1`).</summary>
    public const int MaxLevel = 10;

    /// <summary>The most places a stadium can hold: the top level's ceiling (`STAD-1`).</summary>
    public const int MaxCapacity = SeatsPerLevel * MaxLevel;

    /// <summary>The most places a single build order may add. Bounded so a request is never unbounded.</summary>
    public const int MaxSeatsPerOrder = MaxCapacity;

    /// <summary>The standing places every club's ground opens with (`STAD-2`).</summary>
    public const int OpeningStanding = 3_000;

    /// <summary>The uncovered seats every club's ground opens with (`STAD-2`).</summary>
    public const int OpeningSeating = 1_000;

    /// <summary>The covered seats every club's ground opens with (`STAD-2`).</summary>
    public const int OpeningCoveredSeating = 900;

    /// <summary>The VIP seats every club's ground opens with (`STAD-2`).</summary>
    public const int OpeningVip = 100;

    /// <summary>The ticket price for a place, at a tier-1 club, in minor units (`STAD-3`).</summary>
    /// <param name="stand">The kind of place.</param>
    /// <remarks>
    /// Priced so a covered seat costs about three times a place on the terrace and a VIP seat about eleven,
    /// which is the spread real grounds charge. At tier 1 a full opening ground (3,000 / 1,000 / 900 / 100)
    /// takes 70,500.00 on a matchday.
    /// </remarks>
    public static long TicketPriceMinorTier1(StadiumStand stand) => stand switch
    {
        StadiumStand.Standing => 800,
        StadiumStand.Seating => 1_500,
        StadiumStand.CoveredSeating => 2_500,
        StadiumStand.Vip => 9_000,
        _ => throw new ArgumentOutOfRangeException(nameof(stand), stand, "Unknown stadium stand."),
    };

    /// <summary>What building one place costs, at a tier-1 club, in minor units (`STAD-4`).</summary>
    /// <param name="stand">The kind of place.</param>
    /// <remarks>
    /// Set so that a place which sells out every home match pays for itself in roughly two and a half
    /// seasons. A place nobody turns up to fill never does, which is what stops a manager building without
    /// looking at the crowd (`STAD-6`).
    /// </remarks>
    public static long BuildCostMinorTier1(StadiumStand stand) => stand switch
    {
        StadiumStand.Standing => 30_000,
        StadiumStand.Seating => 60_000,
        StadiumStand.CoveredSeating => 100_000,
        StadiumStand.Vip => 400_000,
        _ => throw new ArgumentOutOfRangeException(nameof(stand), stand, "Unknown stadium stand."),
    };

    /// <summary>The ticket price for a place at a club in the given tier, in minor units (`STAD-3`).</summary>
    /// <param name="stand">The kind of place.</param>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long TicketPriceMinorFor(StadiumStand stand, int tier) =>
        Math.Max(1, TicketPriceMinorTier1(stand) / WorldRuleSet.TierScalingFactor(tier));

    /// <summary>What building one place costs a club in the given tier, in minor units (`STAD-4`).</summary>
    /// <param name="stand">The kind of place.</param>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long BuildCostMinorFor(StadiumStand stand, int tier) =>
        Math.Max(1, BuildCostMinorTier1(stand) / WorldRuleSet.TierScalingFactor(tier));

    /// <summary>The ground's level for a capacity: how many 5,000-place blocks it spans (`STAD-1`).</summary>
    /// <param name="capacity">The total places, 1 and up.</param>
    /// <returns>1 up to 5,000 places, 2 from 5,001, and so on up to <see cref="MaxLevel"/>.</returns>
    public static int LevelFor(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, MaxCapacity);

        return (capacity + SeatsPerLevel - 1) / SeatsPerLevel;
    }

    /// <summary>The most people who would come to a tier-1 club's home match at its mid-table form (`STAD-5`).</summary>
    /// <remarks>
    /// Larger than the opening ground's 5,000, so a tier-1 club has a crowd waiting for a bigger stadium, and
    /// a lower division's own demand falls away below it.
    /// </remarks>
    public const int MatchdayDemandTier1 = 9_000;

    /// <summary>The fraction of one tier's demand that the tier below it draws, in basis points (`STAD-5`).</summary>
    private const int DemandRetainedPerTierBp = 7_500;

    /// <summary>The share of a matchday's demand that wants a given kind of place, in basis points (`STAD-5`).</summary>
    /// <param name="stand">The kind of place.</param>
    /// <remarks>The four shares add up to 10,000: every spectator wants exactly one kind of place.</remarks>
    public static int DemandShareBp(StadiumStand stand) => stand switch
    {
        StadiumStand.Standing => 4_500,
        StadiumStand.Seating => 2_000,
        StadiumStand.CoveredSeating => 2_800,
        StadiumStand.Vip => 700,
        _ => throw new ArgumentOutOfRangeException(nameof(stand), stand, "Unknown stadium stand."),
    };

    /// <summary>The people who would come to a home match, before the ground limits them (`STAD-5`).</summary>
    /// <param name="tier">The tier the club plays in, starting at 1.</param>
    /// <param name="formRank">The club's league position, 1 (top) to 18 (bottom).</param>
    /// <remarks>
    /// Demand falls by a quarter per tier and rises and falls with league position the same way the old
    /// gate did (`FIN-3`), so a club on a winning run draws a bigger crowd for the match it just played.
    /// </remarks>
    public static int MatchdayDemandFor(int tier, int formRank)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        long demand = MatchdayDemandTier1;

        for (var step = 1; step < tier; step++)
        {
            demand = demand * DemandRetainedPerTierBp / 10_000;
        }

        return (int)(demand * WorldRuleSet.GateRevenueFormFactorBpFor(formRank) / 10_000);
    }

    /// <summary>How many places of a kind a home match sells (`STAD-5`).</summary>
    /// <param name="stand">The kind of place.</param>
    /// <param name="seats">How many places of that kind the ground has.</param>
    /// <param name="demand">The matchday's total demand, from <see cref="MatchdayDemandFor"/>.</param>
    /// <returns>The lesser of the ground's places and the crowd that wants them.</returns>
    public static int SoldFor(StadiumStand stand, int seats, int demand)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seats);
        ArgumentOutOfRangeException.ThrowIfNegative(demand);

        var wanted = (int)((long)demand * DemandShareBp(stand) / 10_000);

        return Math.Min(seats, wanted);
    }

    /// <summary>The gate a home fixture yields, in minor units (`FIN-3`, `STAD-5`).</summary>
    /// <param name="seats">The ground's places.</param>
    /// <param name="tier">The tier the club plays in, starting at 1.</param>
    /// <param name="formRank">The club's league position, 1–18.</param>
    public static long GateRevenueMinorFor(StadiumSeats seats, int tier, int formRank)
    {
        var demand = MatchdayDemandFor(tier, formRank);
        long gate = 0;

        foreach (var stand in StadiumStands.All)
        {
            gate += SoldFor(stand, seats.Of(stand), demand) * TicketPriceMinorFor(stand, tier);
        }

        return gate;
    }

    /// <summary>What a sell-out would take, in minor units: every place sold at its price (`STAD-3`).</summary>
    /// <param name="seats">The ground's places.</param>
    /// <param name="tier">The tier the club plays in, starting at 1.</param>
    public static long FullHouseMinorFor(StadiumSeats seats, int tier)
    {
        long total = 0;

        foreach (var stand in StadiumStands.All)
        {
            total += seats.Of(stand) * TicketPriceMinorFor(stand, tier);
        }

        return total;
    }
}
