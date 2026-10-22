using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Rules;

// The clean start a new manager can choose on a takeover (plan toasty-scott, section 8).
public static partial class EconomyRuleSet
{
    /// <summary>The share of a fresh club's season income forecast its clean-start squad's wages should come to, in basis points: 60%.</summary>
    public const int CleanStartWageShareBp = 6_000;

    /// <summary>The lowest share of a fresh club's season income forecast a clean-start squad's wages may come to, in basis points: 55%.</summary>
    public const int CleanStartWageBandLowBp = 5_500;

    /// <summary>The highest share of a fresh club's season income forecast a clean-start squad's wages may come to, in basis points: 65%.</summary>
    public const int CleanStartWageBandHighBp = 6_500;

    /// <summary>The lowest share of its forecast a seeded club's squad wages may come to, in basis points: 70%.</summary>
    public const int SeededWageBandLowBp = 7_000;

    /// <summary>The highest share of its forecast a seeded club's squad wages may come to, in basis points: 92%.</summary>
    public const int SeededWageBandHighBp = 9_200;

    /// <summary>The most squads the generator draws to find one inside a band before it takes the closest.</summary>
    public const int SquadSizingDraws = 40;

    /// <summary>The ground a club opens with, and returns to on a clean start.</summary>
    public static StadiumSeats OpeningSeats => new(
        StadiumRuleSet.OpeningStanding,
        StadiumRuleSet.OpeningSeating,
        StadiumRuleSet.OpeningCoveredSeating,
        StadiumRuleSet.OpeningVip);

    /// <summary>The season income forecast of a club that has just been founded or has just made a clean start, in minor units.</summary>
    /// <param name="tier">The tier the club plays in.</param>
    /// <remarks>The steady sponsor offer, the tier's opening fans at calm and standing, the opening ground and both buildings at level 1.</remarks>
    public static long FreshClubForecastMinor(int tier) => SeasonIncomeForecastMinor(
        tier,
        SponsorFixedSeasonMinorFor(SponsorOfferKind.Steady, tier, FanRuleSet.StartFansFor(tier), OpeningStandingFor(tier)),
        OpeningSeats,
        FanRuleSet.StartFansFor(tier),
        FanRuleSet.CalmMoodBp,
        StructureRuleSet.OpeningLevel,
        StructureRuleSet.OpeningLevel);

    /// <summary>
    /// How far a squad's wage bill is from a band of a fresh club's forecast, in minor units: 0 inside the band. The generator draws squads
    /// (<see cref="SquadSizingDraws"/> at most) and keeps the first at 0, or else the nearest, so a club never starts with wages it cannot carry.
    /// </summary>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="wageBillMinor">The squad's season wage bill.</param>
    /// <param name="lowBp">The band's low end as a share of the forecast, in basis points.</param>
    /// <param name="highBp">The band's high end as a share of the forecast, in basis points.</param>
    public static long WageBandDistanceMinor(int tier, long wageBillMinor, int lowBp, int highBp)
    {
        var forecast = FreshClubForecastMinor(tier);
        var low = forecast * lowBp / OneHundredPercentBp;
        var high = forecast * highBp / OneHundredPercentBp;

        return wageBillMinor < low ? low - wageBillMinor : wageBillMinor > high ? wageBillMinor - high : 0;
    }

    /// <summary>The wage bill a clean-start squad should come to, in minor units.</summary>
    /// <param name="tier">The tier the club plays in.</param>
    public static long CleanStartWageBillMinorFor(int tier) => FreshClubForecastMinor(tier) * CleanStartWageShareBp / OneHundredPercentBp;
}
