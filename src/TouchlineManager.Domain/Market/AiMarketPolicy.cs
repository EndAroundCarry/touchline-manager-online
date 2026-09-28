using System.Globalization;
using System.Text;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Market;

/// <summary>The versioned identity of the AI market policy (`TRF-12`, `FIC-8`).</summary>
/// <remarks>
/// A changed draw order, a changed band, or a changed surplus rule is a new policy version, exactly as a
/// training or tactics change is: the version is part of the seed, so a club's market decisions are
/// reproducible from this label and its identity alone.
/// </remarks>
public static class AiMarketPolicyVersions
{
    /// <summary>The current policy label, folded into every draw the policy makes.</summary>
    public const string Version = "ai-market-v1";
}

/// <summary>One registered player as the AI market weighs them (`TRF-12`).</summary>
/// <remarks>
/// Hidden potential rides along because the valuation reads it (`PlayerValuation`); it is an input here and
/// never an output (`MAT-11`). A player who is injured or suspended is still registered and still appears —
/// the market does not care whether they can play this week.
/// </remarks>
/// <param name="PlayerId">The player.</param>
/// <param name="Family">The position family the player belongs to.</param>
/// <param name="Ability">The player's current ability, 1–20.</param>
/// <param name="Potential">The hidden development ceiling, 1–20. Class C2: server-only.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="SquadStatus">The player's standing in the squad (`SQ-1`), which the surplus order reads.</param>
/// <param name="ContractEndSeasonNumber">The last season the active contract covers.</param>
/// <param name="CurrentSeasonNumber">The season being played, so the remaining term can be read.</param>
/// <param name="IsListed">Whether the player already has an open listing (`TRF-14`).</param>
public sealed record AiMarketPlayer(
    Guid PlayerId,
    PositionFamily Family,
    int Ability,
    int Potential,
    int Age,
    SquadStatus SquadStatus,
    int ContractEndSeasonNumber,
    int CurrentSeasonNumber,
    bool IsListed);

/// <summary>One AI-controlled club as the market policy reads it (`TRF-12`).</summary>
/// <param name="ClubId">The club, which is the seed's identity.</param>
/// <param name="Tier">The tier the club plays in, which scales the valuation.</param>
/// <param name="SpendableMinor">Cash available after existing reservations (`FIN-10`).</param>
/// <param name="Players">The club's registered squad.</param>
public sealed record AiMarketClub(
    Guid ClubId,
    int Tier,
    long SpendableMinor,
    IReadOnlyList<AiMarketPlayer> Players);

/// <summary>One open listing as a bidder weighs it (`TRF-4`, `TRF-8`).</summary>
/// <param name="ListingId">The listing.</param>
/// <param name="SellerClubId">The selling club, so a club never bids on its own player.</param>
/// <param name="LeadingClubId">The club currently leading, or null, so a club never raises itself.</param>
/// <param name="PlayerId">The listed player.</param>
/// <param name="Family">The listed player's position family, which decides positional need.</param>
/// <param name="Ability">The listed player's ability, 1–20.</param>
/// <param name="Potential">The listed player's hidden ceiling, 1–20. Class C2: server-only.</param>
/// <param name="Age">The listed player's age in game years.</param>
/// <param name="MinimumFeeMinor">The seller's minimum fee, in minor units.</param>
/// <param name="LeadingAmountMinor">The current leading amount, in minor units, or null.</param>
public sealed record AiMarketListing(
    Guid ListingId,
    Guid SellerClubId,
    Guid? LeadingClubId,
    Guid PlayerId,
    PositionFamily Family,
    int Ability,
    int Potential,
    int Age,
    long MinimumFeeMinor,
    long? LeadingAmountMinor);

/// <summary>An AI club's decision to list a surplus player (`TRF-12`).</summary>
/// <param name="PlayerId">The player to sell.</param>
/// <param name="MinimumFeeMinor">The asking price, in minor units.</param>
/// <param name="Seasons">The buyer contract length the listing will propose, 1–3 game seasons (`CON-1`).</param>
public sealed record AiListPlayerDecision(Guid PlayerId, long MinimumFeeMinor, int Seasons);

/// <summary>An AI club's decision to bid on an open listing (`TRF-12`).</summary>
/// <param name="ListingId">The listing to bid on.</param>
/// <param name="AmountMinor">The amount to bid, in minor units.</param>
public sealed record AiBidDecision(Guid ListingId, long AmountMinor);

/// <summary>Everything an AI club decided in one evaluation (`TRF-12`).</summary>
/// <param name="Listings">The players to list, in decision order.</param>
/// <param name="Bids">The listings to bid on, in decision order.</param>
/// <param name="InputsHash">A digest of the facts the decision was derived from, for the decision record.</param>
public sealed record AiMarketPlan(
    IReadOnlyList<AiListPlayerDecision> Listings,
    IReadOnlyList<AiBidDecision> Bids,
    string InputsHash);

/// <summary>
/// The deterministic policy an AI-controlled club buys and sells by (`TRF-12`).
/// </summary>
/// <remarks>
/// <para>
/// A pure, versioned function of the club's shape and the open market. It reads no clock, repository,
/// culture, or global random source, so the same club always decides the same way and a world can be
/// replayed. It decides no outcome a manager could not: every listing and bid it produces is routed through
/// the same writer a human command uses, which applies the same eligibility, squad-legality, and
/// affordability rules (`INS-12`).
/// </para>
/// <para>
/// A club lists a player only when it is genuinely surplus — its family is above the generator's quota, or
/// the squad is above the target size — and only while the sale leaves a legal squad (the minimum count and
/// two goalkeepers, `SQ-2`). A club bids only where it has a positional need, never on its own player or its
/// own leading bid, and never above the player's valuation or a bounded share of its available cash
/// (`FIN-10`). Both lists are bounded, so one pass cannot flood the market or over-commit a club.
/// </para>
/// <para>
/// Two draws vary a club from its neighbours without making it random: how much of a valuation it will pay,
/// and how optimistically it prices its own player. Both come from a stream seeded from the club identity,
/// so two clubs differ while a given club never does.
/// </para>
/// </remarks>
public static class AiMarketPolicy
{
    /// <summary>The lowest bond a club's bid willingness takes, in basis points of a valuation.</summary>
    private const int BidWillingnessFloorBp = 9_000;

    /// <summary>The highest bond a club's bid willingness takes, in basis points of a valuation.</summary>
    private const int BidWillingnessSpanBp = 1_001;

    /// <summary>The lowest asking-price factor, in basis points of a valuation.</summary>
    private const int AskingFactorFloorBp = 9_500;

    /// <summary>The span the asking-price factor draws from, in basis points.</summary>
    private const int AskingFactorSpanBp = 1_001;

    /// <summary>Decides a club's surplus listings and its bids on the open market.</summary>
    /// <param name="club">The club and its squad.</param>
    /// <param name="openListings">Every open listing in the world, including the club's own.</param>
    public static AiMarketPlan Decide(AiMarketClub club, IReadOnlyList<AiMarketListing> openListings)
    {
        ArgumentNullException.ThrowIfNull(club);
        ArgumentNullException.ThrowIfNull(openListings);

        var draws = new Pcg32(DeterministicDigest.SeedOf(
            AiMarketPolicyVersions.Version,
            club.ClubId.ToString("D")));

        var bidWillingnessBp = BidWillingnessFloorBp + draws.NextInt(BidWillingnessSpanBp);
        var askingFactorBp = AskingFactorFloorBp + draws.NextInt(AskingFactorSpanBp);
        var buyerSeasons = 2 + draws.NextInt(2);

        return new AiMarketPlan(
            ChooseSurplus(club, askingFactorBp, buyerSeasons),
            ChooseBids(club, openListings, bidWillingnessBp),
            InputsHashOf(club, openListings));
    }

    /// <summary>Chooses the surplus players to list, keeping the club legal as each one leaves (`TRF-12`).</summary>
    private static List<AiListPlayerDecision> ChooseSurplus(
        AiMarketClub club,
        int askingFactorBp,
        int buyerSeasons)
    {
        var squad = club.Players;

        // A club with a player already on the market waits for that listing to resolve before listing
        // another: one pass cannot flood the market, and a repeated pass lists nothing new.
        if (squad.Any(player => player.IsListed))
        {
            return [];
        }

        var remaining = squad.Count;
        var familyCounts = CountFamilies(squad);

        // The least valuable players first: a lower squad status, then lower ability, then the shorter
        // remaining term, then identity so the order is total and reproducible.
        var candidates = squad
            .Where(player => !player.IsListed && IsSurplus(player, familyCounts, squad.Count))
            .OrderByDescending(player => (int)player.SquadStatus)
            .ThenBy(player => player.Ability)
            .ThenBy(player => player.ContractEndSeasonNumber)
            .ThenBy(player => player.PlayerId)
            .ToList();

        var decisions = new List<AiListPlayerDecision>();

        foreach (var player in candidates)
        {
            if (decisions.Count >= WorldRuleSet.AiMarketMaxListingsPerClub)
            {
                break;
            }

            // Re-checked against the running squad, so a club trims toward the target and stops rather than
            // selling every player that looked surplus when the pass began.
            if (!IsSurplus(player, familyCounts, remaining) || !SellingKeepsLegal(player, remaining, familyCounts))
            {
                continue;
            }

            var value = PlayerValuation.Estimate(
                new PlayerValuationInput(player.Ability, player.Potential, player.Age, club.Tier));
            var fee = Math.Max(1, value * askingFactorBp / 10_000);

            decisions.Add(new AiListPlayerDecision(player.PlayerId, fee, buyerSeasons));

            remaining--;
            familyCounts[player.Family] = familyCounts[player.Family] - 1;
        }

        return decisions;
    }

    /// <summary>Chooses the listings to bid on within the club's needs, valuation, and budget (`TRF-12`).</summary>
    private static List<AiBidDecision> ChooseBids(
        AiMarketClub club,
        IReadOnlyList<AiMarketListing> openListings,
        int bidWillingnessBp)
    {
        var decisions = new List<AiBidDecision>();
        var familyCounts = CountFamilies(club.Players);
        var squadCount = club.Players.Count;
        var spendable = club.SpendableMinor;

        var ordered = openListings
            .Where(listing => listing.SellerClubId != club.ClubId)
            .OrderBy(listing => listing.ListingId);

        foreach (var listing in ordered)
        {
            if (decisions.Count >= WorldRuleSet.AiMarketMaxBidsPerClub
                || squadCount >= WorldRuleSet.SquadMaximumRegistered)
            {
                break;
            }

            // Never raise the club's own leading bid, and only buy where there is a need or an upgrade.
            if (listing.LeadingClubId == club.ClubId || !WantsListing(club, familyCounts, listing))
            {
                continue;
            }

            var value = PlayerValuation.Estimate(
                new PlayerValuationInput(listing.Ability, listing.Potential, listing.Age, club.Tier));

            var ceiling = Math.Min(
                value * bidWillingnessBp / 10_000,
                spendable * WorldRuleSet.AiMarketBidBudgetFractionBp / 10_000);

            var amount = AuctionRules.MinimumAcceptableBid(listing.MinimumFeeMinor, listing.LeadingAmountMinor);

            if (amount <= 0 || amount > ceiling)
            {
                continue;
            }

            decisions.Add(new AiBidDecision(listing.ListingId, amount));

            spendable -= amount;
            familyCounts[listing.Family] = familyCounts[listing.Family] + 1;
            squadCount++;
        }

        return decisions;
    }

    /// <summary>Reports whether a player is surplus to the squad's shape (`TRF-12`).</summary>
    private static bool IsSurplus(
        AiMarketPlayer player,
        IReadOnlyDictionary<PositionFamily, int> familyCounts,
        int squadCount) =>
        familyCounts.GetValueOrDefault(player.Family) > WorldRuleSet.AiMarketFamilyCap(player.Family)
        || squadCount > WorldRuleSet.AiMarketTargetSquadSize;

    /// <summary>Reports whether selling a player still leaves a legal squad (`SQ-2`).</summary>
    private static bool SellingKeepsLegal(
        AiMarketPlayer player,
        int remaining,
        IReadOnlyDictionary<PositionFamily, int> familyCounts)
    {
        var goalkeepers = familyCounts.GetValueOrDefault(PositionFamily.Goalkeeper)
            - (player.Family == PositionFamily.Goalkeeper ? 1 : 0);

        return remaining - 1 >= WorldRuleSet.SquadMinimumRegistered
            && goalkeepers >= WorldRuleSet.MinimumGoalkeepers;
    }

    /// <summary>
    /// Reports whether a club wants a listed player: a positional need, or a player who improves on the
    /// weakest the club already has in that family (`TRF-12`).
    /// </summary>
    /// <remarks>
    /// The upgrade clause is what gives the market a first buyer. A need alone would require a squad to have
    /// changed already, and nothing changes until a transfer settles — so a world of balanced squads would
    /// list players nobody ever bid for. "Better than what I have" is a reason to buy that does not depend on
    /// a prior sale, which is what lets the market start.
    /// </remarks>
    private static bool WantsListing(
        AiMarketClub club,
        IReadOnlyDictionary<PositionFamily, int> familyCounts,
        AiMarketListing listing) =>
        familyCounts.GetValueOrDefault(listing.Family) < WorldRuleSet.AiMarketFamilyCap(listing.Family)
        || listing.Ability > WeakestAbilityIn(club.Players, listing.Family);

    /// <summary>Gets the lowest ability the club holds in a family, or zero when it holds nobody there.</summary>
    private static int WeakestAbilityIn(IReadOnlyList<AiMarketPlayer> players, PositionFamily family)
    {
        var weakest = int.MaxValue;

        foreach (var player in players)
        {
            if (player.Family == family && player.Ability < weakest)
            {
                weakest = player.Ability;
            }
        }

        return weakest == int.MaxValue ? 0 : weakest;
    }

    private static Dictionary<PositionFamily, int> CountFamilies(IReadOnlyList<AiMarketPlayer> players)
    {
        var counts = new Dictionary<PositionFamily, int>();

        foreach (var player in players)
        {
            counts[player.Family] = counts.GetValueOrDefault(player.Family) + 1;
        }

        return counts;
    }

    /// <summary>Digests the facts a decision was made from, in a fixed order, for the decision record.</summary>
    private static string InputsHashOf(AiMarketClub club, IReadOnlyList<AiMarketListing> openListings)
    {
        var builder = new StringBuilder();

        builder.Append("club=").Append(club.ClubId.ToString("D"))
            .Append(";tier=").Append(club.Tier.ToString(CultureInfo.InvariantCulture))
            .Append(";cash=").Append(club.SpendableMinor.ToString(CultureInfo.InvariantCulture))
            .Append(";players=");

        foreach (var player in club.Players.OrderBy(player => player.PlayerId))
        {
            builder.Append(player.PlayerId.ToString("D")).Append('|')
                .Append(player.Family.ToCode()).Append('|')
                .Append(player.Ability.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(player.Potential.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(player.Age.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(player.SquadStatus.ToCode()).Append('|')
                .Append(player.ContractEndSeasonNumber.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(player.IsListed ? '1' : '0').Append(',');
        }

        builder.Append(";listings=");

        foreach (var listing in openListings.OrderBy(listing => listing.ListingId))
        {
            builder.Append(listing.ListingId.ToString("D")).Append('|')
                .Append(listing.SellerClubId.ToString("D")).Append('|')
                .Append((listing.LeadingClubId ?? Guid.Empty).ToString("D")).Append('|')
                .Append(listing.PlayerId.ToString("D")).Append('|')
                .Append(listing.Family.ToCode()).Append('|')
                .Append(listing.Ability.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(listing.Potential.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(listing.Age.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(listing.MinimumFeeMinor.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append((listing.LeadingAmountMinor ?? -1).ToString(CultureInfo.InvariantCulture)).Append(',');
        }

        return DeterministicDigest.Of(AiMarketPolicyVersions.Version, builder.ToString());
    }
}
