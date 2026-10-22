using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Domain.Rules;

// The sponsor and the season objective (plan toasty-scott, section 3).
public static partial class EconomyRuleSet
{
    /// <summary>The bonus a tier-1 sponsor pays for meeting a mid-table objective, in minor units: 1M. A balancing value.</summary>
    public const long SponsorBonusBaseMinorTier1 = 100_000_000;

    /// <summary>How many places better than the objective count as exceeding it.</summary>
    public const int ExceededByPlaces = 3;

    /// <summary>What exceeding the objective pays against meeting it, in basis points: half as much again.</summary>
    public const int ExceededBonusBp = 15_000;

    /// <summary>The most the fans can lift a sponsor's money, as a multiple of the tier's base, in basis points: four times.</summary>
    public const int SponsorFansFactorCapBp = 40_000;

    /// <summary>How much each point of standing above or below the tier's opening standing moves a sponsor's money, in basis points.</summary>
    public const int StandingFactorBpPerPoint = 200;

    /// <summary>The lowest a sponsor's standing factor goes, in basis points.</summary>
    public const int StandingFactorFloorBp = 5_000;

    /// <summary>The highest a sponsor's standing factor goes, in basis points.</summary>
    public const int StandingFactorCeilingBp = 20_000;

    /// <summary>The place the top-half objective asks for: half the table.</summary>
    public const int TopHalfPlace = ClubsPerDivision / 2;

    /// <summary>The place the eleventh-place objective asks for: five places above the bottom, so the last of the middle of the table.</summary>
    public const int EleventhPlace = ClubsPerDivision - 5;

    /// <summary>The place the stay-up objective asks for: the last place that is not relegated.</summary>
    public const int StayUpPlace = ClubsPerDivision - WorldRuleSet.RelegatedPerTier;

    /// <summary>The place a season objective asks for.</summary>
    /// <param name="objective">The objective.</param>
    public static int TargetPlaceFor(SeasonObjective objective) => objective switch
    {
        SeasonObjective.WinLeague => 1,
        SeasonObjective.TopThree => 3,
        SeasonObjective.TopSix => 6,
        SeasonObjective.TopHalf => TopHalfPlace,
        SeasonObjective.EleventhPlace => EleventhPlace,
        SeasonObjective.StayUp => StayUpPlace,
        _ => throw new ArgumentOutOfRangeException(nameof(objective), objective, "Unknown season objective."),
    };

    /// <summary>How much more a bonus pays for a harder objective, in basis points: 3.0 for the title down to 0.35 for staying up.</summary>
    /// <param name="objective">The objective.</param>
    public static int ObjectiveAmbitionBp(SeasonObjective objective) => objective switch
    {
        SeasonObjective.WinLeague => 30_000,
        SeasonObjective.TopThree => 22_000,
        SeasonObjective.TopSix => 16_000,
        SeasonObjective.TopHalf => 10_000,
        SeasonObjective.EleventhPlace => 6_000,
        SeasonObjective.StayUp => 3_500,
        _ => throw new ArgumentOutOfRangeException(nameof(objective), objective, "Unknown season objective."),
    };

    /// <summary>The share of the steady fixed part an offer pays every matchday, in basis points: all of it, 85% or 65%.</summary>
    /// <param name="kind">The offer.</param>
    public static int SponsorFixedShareBp(SponsorOfferKind kind) => kind switch
    {
        SponsorOfferKind.Steady => 10_000,
        SponsorOfferKind.Balanced => 8_500,
        SponsorOfferKind.Performance => 6_500,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sponsor offer."),
    };

    /// <summary>How much an offer's bonus is multiplied by, in basis points: half, once or twice.</summary>
    /// <param name="kind">The offer.</param>
    public static int SponsorBonusMultiplierBp(SponsorOfferKind kind) => kind switch
    {
        SponsorOfferKind.Steady => 5_000,
        SponsorOfferKind.Balanced => 10_000,
        SponsorOfferKind.Performance => 20_000,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sponsor offer."),
    };

    /// <summary>The objective that fits a club's strength rank in its division: the one the board offers as realistic.</summary>
    /// <param name="strengthRank">The club's rank by squad strength, 1 for the strongest.</param>
    public static SeasonObjective RealisticObjectiveFor(int strengthRank)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(strengthRank, 1);

        return strengthRank switch
        {
            1 => SeasonObjective.WinLeague,
            <= 3 => SeasonObjective.TopThree,
            <= 6 => SeasonObjective.TopSix,
            <= TopHalfPlace => SeasonObjective.TopHalf,
            <= EleventhPlace => SeasonObjective.EleventhPlace,
            _ => SeasonObjective.StayUp,
        };
    }

    /// <summary>How an objective turned out for a final place.</summary>
    /// <param name="objective">The objective the club chose.</param>
    /// <param name="finalPlace">The place it finished in, 1 to <see cref="ClubsPerDivision"/>.</param>
    public static ObjectiveOutcome OutcomeOf(SeasonObjective objective, int finalPlace)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(finalPlace, 1);

        var target = TargetPlaceFor(objective);

        if (finalPlace > target)
        {
            return ObjectiveOutcome.Missed;
        }

        return finalPlace <= target - ExceededByPlaces ? ObjectiveOutcome.Exceeded : ObjectiveOutcome.Met;
    }

    /// <summary>The lift the fans give a sponsor's money, in basis points: the square root of the fans against the tier's opening fans, never above four.</summary>
    /// <param name="fans">The club's fans.</param>
    /// <param name="tier">The tier the club plays in.</param>
    public static int SponsorFansFactorBp(int fans, int tier)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fans);

        var opening = Math.Max(1, FanRuleSet.StartFansFor(tier));
        var factor = Math.Sqrt((double)fans / opening) * OneHundredPercentBp;

        return (int)Math.Min(SponsorFansFactorCapBp, factor);
    }

    /// <summary>The lift a club's standing gives a sponsor's money, in basis points: neutral at the tier's opening standing.</summary>
    /// <param name="standing">The club's standing in points, 1 to 100.</param>
    /// <param name="tier">The tier the club plays in.</param>
    public static int SponsorStandingFactorBp(int standing, int tier) =>
        Math.Clamp(
            OneHundredPercentBp + (StandingFactorBpPerPoint * (standing - OpeningStandingFor(tier))),
            StandingFactorFloorBp,
            StandingFactorCeilingBp);

    /// <summary>What an offer pays in a season whatever happens, in minor units: paid in equal parts every matchday once the deal is signed.</summary>
    /// <param name="kind">The offer.</param>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="fans">The club's fans when the offers were made.</param>
    /// <param name="standing">The club's standing in points when the offers were made.</param>
    public static long SponsorFixedSeasonMinorFor(SponsorOfferKind kind, int tier, int fans, int standing) =>
        SponsorBaseSeasonMinorFor(tier)
            * SponsorFixedShareBp(kind) / OneHundredPercentBp
            * SponsorFansFactorBp(fans, tier) / OneHundredPercentBp
            * SponsorStandingFactorBp(standing, tier) / OneHundredPercentBp;

    /// <summary>What an offer pays at the end of the season for meeting an objective, in minor units. Zero when it was missed.</summary>
    /// <param name="kind">The offer.</param>
    /// <param name="objective">The objective the club chose.</param>
    /// <param name="outcome">How it turned out.</param>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="fans">The club's fans when the offers were made.</param>
    /// <param name="standing">The club's standing in points when the offers were made.</param>
    public static long SponsorBonusMinorFor(SponsorOfferKind kind, SeasonObjective objective, ObjectiveOutcome outcome, int tier, int fans, int standing)
    {
        if (outcome == ObjectiveOutcome.Missed)
        {
            return 0;
        }

        var met = ByTier(SponsorBonusBaseMinorTier1, tier)
            * ObjectiveAmbitionBp(objective) / OneHundredPercentBp
            * SponsorBonusMultiplierBp(kind) / OneHundredPercentBp
            * SponsorFansFactorBp(fans, tier) / OneHundredPercentBp
            * SponsorStandingFactorBp(standing, tier) / OneHundredPercentBp;

        return outcome == ObjectiveOutcome.Exceeded ? met * ExceededBonusBp / OneHundredPercentBp : met;
    }
}
