using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Finance;

/// <summary>One sponsor's offer to a club for a season.</summary>
/// <param name="Kind">How much of the money depends on the result.</param>
/// <param name="SponsorName">The sponsor's name: fictional, drawn from the world seed.</param>
/// <param name="FixedSeasonMinor">What it pays over the season whatever happens, in 30 equal parts, one on each matchday.</param>
/// <param name="BonusMultiplierBp">How much the offer multiplies a bonus by: half, once or twice.</param>
public sealed record SponsorOffer(SponsorOfferKind Kind, string SponsorName, long FixedSeasonMinor, int BonusMultiplierBp);

/// <summary>What an objective would pay a club under an offer.</summary>
/// <param name="Objective">The objective.</param>
/// <param name="TargetPlace">The place it asks for.</param>
/// <param name="MetMinor">The bonus for meeting it.</param>
/// <param name="ExceededMinor">The bonus for finishing three or more places better.</param>
public sealed record ObjectiveBonusQuote(SeasonObjective Objective, int TargetPlace, long MetMinor, long ExceededMinor);

/// <summary>
/// The three offers a club's sponsors make when a season begins (`sponsor-v1`, `SPN-*`).
/// </summary>
/// <remarks>
/// A pure function of the world, the season, the club and the club's fans and standing, so the offers a club sees are the offers it always
/// sees, and a retried job writes the same rows. The names are fictional (the content policy): two parts drawn from short lists, a different
/// pair for each of the three offers.
/// </remarks>
public static class SponsorOffers
{
    /// <summary>The version label of the offers.</summary>
    public const string Version = "sponsor-v1";

    private static readonly string[] Places =
    [
        "Northgate", "Ironbridge", "Silverline", "Redfern", "Oakmere", "Bluepeak", "Stonehaven", "Lakeshore",
        "Brightwell", "Crestview", "Eastvale", "Falconer", "Greywater", "Halden", "Kingsmoor", "Marlow",
    ];

    private static readonly string[] Trades =
    [
        "Insurance", "Logistics", "Energy", "Telecom", "Brewing", "Motors", "Foods", "Bank",
        "Airways", "Steel", "Software", "Builders", "Pharma", "Travel", "Paper", "Textiles",
    ];

    /// <summary>The three offers, steady first.</summary>
    /// <param name="worldId">The world.</param>
    /// <param name="seasonNumber">The season the offers are for.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="tier">The tier the club plays the season in.</param>
    /// <param name="fans">The club's fans when the offers are made.</param>
    /// <param name="standing">The club's standing in points when the offers are made.</param>
    public static IReadOnlyList<SponsorOffer> For(Guid worldId, int seasonNumber, Guid clubId, int tier, int fans, int standing)
    {
        var draws = new Pcg32(DeterministicDigest.SeedOf(
            Version,
            worldId.ToString("D"),
            seasonNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            clubId.ToString("D")));

        var first = draws.NextInt(Places.Length);
        var second = draws.NextInt(Trades.Length);

        return
        [
            .. Enum.GetValues<SponsorOfferKind>().Select((kind, index) => new SponsorOffer(
                kind,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{Places[(first + (index * 5)) % Places.Length]} {Trades[(second + (index * 7)) % Trades.Length]}"),
                EconomyRuleSet.SponsorFixedSeasonMinorFor(kind, tier, fans, standing),
                EconomyRuleSet.SponsorBonusMultiplierBp(kind))),
        ];
    }

    /// <summary>What one objective pays under an offer a club was made, worked out from the figures the offer was made from.</summary>
    /// <param name="offer">The offer.</param>
    /// <param name="objective">The objective.</param>
    public static ObjectiveBonusQuote QuoteFor(ClubSponsorOffer offer, SeasonObjective objective)
    {
        ArgumentNullException.ThrowIfNull(offer);

        return new ObjectiveBonusQuote(
            objective,
            EconomyRuleSet.TargetPlaceFor(objective),
            EconomyRuleSet.SponsorBonusMinorFor(offer.Kind, objective, ObjectiveOutcome.Met, offer.Tier, offer.Fans, offer.StandingPoints),
            EconomyRuleSet.SponsorBonusMinorFor(offer.Kind, objective, ObjectiveOutcome.Exceeded, offer.Tier, offer.Fans, offer.StandingPoints));
    }

    /// <summary>What every objective would pay under an offer, hardest first: the figures a manager picks an objective by.</summary>
    /// <param name="kind">The offer.</param>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="fans">The club's fans when the offers were made.</param>
    /// <param name="standing">The club's standing in points when the offers were made.</param>
    public static IReadOnlyList<ObjectiveBonusQuote> Quotes(SponsorOfferKind kind, int tier, int fans, int standing) =>
    [
        .. Enum.GetValues<SeasonObjective>().Select(objective => new ObjectiveBonusQuote(
            objective,
            EconomyRuleSet.TargetPlaceFor(objective),
            EconomyRuleSet.SponsorBonusMinorFor(kind, objective, ObjectiveOutcome.Met, tier, fans, standing),
            EconomyRuleSet.SponsorBonusMinorFor(kind, objective, ObjectiveOutcome.Exceeded, tier, fans, standing))),
    ];
}

/// <summary>
/// What the season's bonus comes to at the end of the season (`OBJ-*`).
/// </summary>
/// <remarks>The outcome is read from the final place against the objective; the amount is the offer's bonus for it, 0 when it was missed.</remarks>
public static class ObjectiveBonus
{
    /// <summary>Settles a season's objective.</summary>
    /// <param name="kind">The offer the club signed.</param>
    /// <param name="objective">The objective the club chose.</param>
    /// <param name="finalPlace">The place it finished in.</param>
    /// <param name="tier">The tier the club played the season in.</param>
    /// <param name="fans">The club's fans when the deal was signed.</param>
    /// <param name="standing">The club's standing in points when the deal was signed.</param>
    public static (ObjectiveOutcome Outcome, long BonusMinor) Settle(
        SponsorOfferKind kind,
        SeasonObjective objective,
        int finalPlace,
        int tier,
        int fans,
        int standing)
    {
        var outcome = EconomyRuleSet.OutcomeOf(objective, finalPlace);

        return (outcome, EconomyRuleSet.SponsorBonusMinorFor(kind, objective, outcome, tier, fans, standing));
    }
}
