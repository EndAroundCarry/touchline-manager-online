namespace Tactical11Manager.Domain.Rules;

// League money, the sponsor's base, fixed costs and the prize table (plan toasty-scott, sections 2 and 2a).
public static partial class EconomyRuleSet
{
    /// <summary>The league's television money for a tier-1 club in a season, in minor units: 4M, equal for the division. A balancing value.</summary>
    public const long LeagueTvSeasonMinorTier1 = 400_000_000;

    /// <summary>The steady sponsor offer's fixed part for a tier-1 club in a season, in minor units: 3.5M. A balancing value.</summary>
    public const long SponsorBaseSeasonMinorTier1 = 350_000_000;

    /// <summary>The fixed operating cost of a tier-1 club in a season, in minor units: 1.2M. A balancing value.</summary>
    public const long OperatingCostSeasonMinorTier1 = 120_000_000;

    /// <summary>The share of the contracts a relegated club keeps, in basis points: the relegation clause takes 40% off at rollover.</summary>
    public const int RelegationKeepBp = 6_000;

    /// <summary>The share of the television money difference a relegated club is paid once as a parachute, in basis points: half.</summary>
    public const int ParachuteShareBp = 5_000;

    /// <summary>
    /// One prize share for a tier-1 division, in minor units: 340k. The last place gets one share and each place up to the third one more, so a
    /// division of 16 pays 140.42 shares and the average club the 2.98M a division of 18 paid at 300k a share.
    /// </summary>
    public const long PrizeShareMinorTier1 = 34_000_000;

    /// <summary>How much more the second place gets than the third, in basis points: 15% more, on top of the straight line.</summary>
    public const int PrizeSecondOverThirdBp = 11_500;

    /// <summary>How much more the champion gets than the second place, in basis points: 20% more.</summary>
    public const int PrizeFirstOverSecondBp = 12_000;

    /// <summary>The league's television money for a club in a season, in minor units.</summary>
    /// <param name="tier">The tier the club plays in.</param>
    public static long LeagueTvSeasonMinorFor(int tier) => ByTier(LeagueTvSeasonMinorTier1, tier);

    /// <summary>The steady sponsor offer's fixed part for a club in a season, at neutral fans and standing, in minor units.</summary>
    /// <param name="tier">The tier the club plays in.</param>
    public static long SponsorBaseSeasonMinorFor(int tier) => ByTier(SponsorBaseSeasonMinorTier1, tier);

    /// <summary>The fixed operating cost of a club in a season, in minor units.</summary>
    /// <param name="tier">The tier the club plays in.</param>
    public static long OperatingCostSeasonMinorFor(int tier) => ByTier(OperatingCostSeasonMinorTier1, tier);

    /// <summary>The prize a club is paid at the end of the season for its final place, in minor units, at the tier it finished in.</summary>
    /// <param name="tier">The tier the club finished in.</param>
    /// <param name="place">The final place, 1 to <see cref="ClubsPerDivision"/>.</param>
    /// <remarks>
    /// A straight line from the last place to the third, like a league's merit payment, then bigger steps for the top three:
    /// the second place gets 15% more than the third and the champion 20% more than the second. The line is not a table in the code: any one
    /// place can be bent by changing this method.
    /// </remarks>
    public static long PrizeMinorFor(int tier, int place)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(place, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(place, ClubsPerDivision);

        var share = ByTier(PrizeShareMinorTier1, tier);
        var third = (ClubsPerDivision + 1 - 3) * share;

        if (place >= 3)
        {
            return (ClubsPerDivision + 1 - place) * share;
        }

        var second = third * PrizeSecondOverThirdBp / OneHundredPercentBp;

        return place == 2 ? second : second * PrizeFirstOverSecondBp / OneHundredPercentBp;
    }

    /// <summary>The prize of every place of a tier, first place first.</summary>
    /// <param name="tier">The tier number.</param>
    public static IReadOnlyList<long> PrizeTable(int tier) =>
        [.. Enumerable.Range(1, ClubsPerDivision).Select(place => PrizeMinorFor(tier, place))];

    /// <summary>The one-off payment to a club relegated from a tier, in minor units: half the annual television money it loses.</summary>
    /// <param name="fromTier">The tier the club is relegated from.</param>
    public static long ParachuteMinorFor(int fromTier)
    {
        var lost = LeagueTvSeasonMinorFor(fromTier) - LeagueTvSeasonMinorFor(fromTier + 1);

        return Math.Max(0, lost * ParachuteShareBp / OneHundredPercentBp);
    }

    /// <summary>A relegated club's season wage after the relegation clause, in minor units. A wage already at the floor stays there.</summary>
    /// <param name="seasonWageMinor">The wage before the clause.</param>
    public static long AfterRelegationClause(long seasonWageMinor) =>
        Math.Max(Math.Min(seasonWageMinor, SeasonWageFloorMinor), seasonWageMinor * RelegationKeepBp / OneHundredPercentBp);
}
