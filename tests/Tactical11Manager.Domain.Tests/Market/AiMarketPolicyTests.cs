using FluentAssertions;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Market;

/// <summary>
/// The deterministic AI market policy (`TRF-12`).
/// </summary>
/// <remarks>
/// The policy is a pure function, so these tests need neither a clock nor a database. What they pin is the
/// contract the evaluator depends on: the same club and market decide the same way, a club only sells what
/// is surplus and never below a legal squad, a club only buys where it has a need and never above its
/// valuation or budget, and every list is bounded. Version 2 adds the wages: a club only buys what its wage
/// budget and its reserve allow, a club in the red buys nothing, and a club over budget sells its best-paid
/// player first.
/// </remarks>
public sealed class AiMarketPolicyTests
{
    [Fact]
    public void The_same_club_and_market_decide_the_same_way()
    {
        var squad = FullSquad();
        var listing = MidfieldListing(Guid.CreateVersion7(), sellerClub: Guid.CreateVersion7(), fee: 1_000_000);

        var first = AiMarketPolicy.Decide(Club(ClubId(1), squad), [listing]);
        var second = AiMarketPolicy.Decide(Club(ClubId(1), squad), [listing]);

        second.Listings.Should().Equal(first.Listings);
        second.Bids.Should().Equal(first.Bids);
        second.InputsHash.Should().Be(first.InputsHash);
    }

    [Fact]
    public void A_full_squad_trims_one_player_and_stops()
    {
        var plan = AiMarketPolicy.Decide(Club(ClubId(1), FullSquad()), []);

        plan.Listings.Should().HaveCount(1, "the squad is one above the target and is trimmed to it, not beyond");
    }

    [Fact]
    public void Different_clubs_price_the_same_player_differently()
    {
        var squad = FullSquad();

        var fees = Enumerable
            .Range(0, 50)
            .Select(index => AiMarketPolicy.Decide(Club(ClubId(index), squad), []).Listings.Single().MinimumFeeMinor)
            .Distinct()
            .ToList();

        fees.Should().HaveCountGreaterThan(1, "clubs vary while a given club never does");
    }

    [Fact]
    public void A_sale_that_would_break_the_squad_floor_is_not_listed()
    {
        // Eight defenders is one over the cap, but the club is at the minimum, so selling any of them would
        // leave seventeen (SQ-2).
        var squad = Squad(Composition(goalkeepers: 2, defenders: 8, midfielders: 5, attackers: 3));

        var plan = AiMarketPolicy.Decide(Club(ClubId(1), squad), []);

        plan.Listings.Should().BeEmpty("a club never sells itself below the minimum squad (SQ-2)");
    }

    [Fact]
    public void A_goalkeeper_is_never_sold_below_the_minimum()
    {
        // Two weak goalkeepers on Prospect terms sort first, but selling either would leave one (SQ-2), so
        // the policy skips them and sells from the outfield instead.
        var squad = new List<AiMarketPlayer>
        {
            Player(1, PositionFamily.Goalkeeper, ability: 5, status: SquadStatus.Prospect),
            Player(2, PositionFamily.Goalkeeper, ability: 5, status: SquadStatus.Prospect),
        };

        for (var ordinal = 3; ordinal <= 22; ordinal++)
        {
            squad.Add(Player(ordinal, PositionFamily.Defence, ability: 14, status: SquadStatus.FirstTeam));
        }

        var plan = AiMarketPolicy.Decide(Club(ClubId(1), squad), []);

        var goalkeepers = squad
            .Where(player => player.Family == PositionFamily.Goalkeeper)
            .Select(player => player.PlayerId)
            .ToList();

        plan.Listings.Should().NotBeEmpty();
        plan.Listings.Select(decision => decision.PlayerId).Should().NotIntersectWith(goalkeepers);
    }

    [Fact]
    public void At_most_the_per_pass_listing_cap_is_reached()
    {
        // Thirty players, so the count surplus is far past the target; the cap still bounds the pass.
        var families = Composition(goalkeepers: 3, defenders: 10, midfielders: 10, attackers: 7);

        var plan = AiMarketPolicy.Decide(Club(ClubId(1), Squad(families)), []);

        plan.Listings.Should().HaveCount(WorldRuleSet.AiMarketMaxListingsPerClub);
    }

    [Fact]
    public void A_listing_in_a_family_the_club_already_covers_is_not_bid()
    {
        // Every family is exactly at its cap, so the club has no positional need and bids on nothing.
        var listings = new[]
        {
            MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 500_000),
            AttackListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 500_000),
        };

        var plan = AiMarketPolicy.Decide(Club(ClubId(1), FullSquad()), listings);

        plan.Bids.Should().BeEmpty("TRF-12 bids within positional need");
    }

    [Fact]
    public void A_depleted_family_creates_a_bid_at_the_minimum_acceptable_amount()
    {
        // Six midfielders is one below the cap, so the club has a need and bids the floor on a listing.
        var squad = Squad(Composition(goalkeepers: 3, defenders: 7, midfielders: 6, attackers: 5));
        var listing = MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 1_000_000);

        var plan = AiMarketPolicy.Decide(Club(ClubId(1), squad), [listing]);

        plan.Bids.Should().ContainSingle();
        plan.Bids.Single().ListingId.Should().Be(listing.ListingId);
        plan.Bids.Single().AmountMinor.Should().Be(AuctionRules.MinimumAcceptableBid(listing.MinimumFeeMinor, null));
    }

    [Fact]
    public void A_club_with_no_player_in_a_family_can_bid_on_a_listing_in_it()
    {
        // Retirements and expiries can leave a club with no attacker at all; the bid must still be made, not throw.
        var squad = Squad(Composition(goalkeepers: 3, defenders: 7, midfielders: 9, attackers: 0));
        var listing = AttackListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 1_000_000);

        var plan = AiMarketPolicy.Decide(Club(ClubId(1), squad), [listing]);

        plan.Bids.Should().ContainSingle().Which.ListingId.Should().Be(listing.ListingId);
    }

    [Fact]
    public void The_club_never_bids_on_its_own_player_or_its_own_leading_bid()
    {
        var clubId = ClubId(1);
        var squad = Squad(Composition(goalkeepers: 3, defenders: 7, midfielders: 6, attackers: 5));

        var ownPlayer = MidfieldListing(Guid.CreateVersion7(), sellerClub: clubId, fee: 500_000);
        var ownLead = MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 500_000, leadingClubId: clubId);

        var plan = AiMarketPolicy.Decide(Club(clubId, squad), [ownPlayer, ownLead]);

        plan.Bids.Should().BeEmpty();
    }

    [Fact]
    public void A_bid_never_exceeds_the_players_valuation()
    {
        var squad = Squad(Composition(goalkeepers: 3, defenders: 7, midfielders: 6, attackers: 5));

        // A fee far above the valuation is refused; a fee below it is bid.
        var tooDear = MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 5_000_000_000);
        var reasonable = MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 50_000_000);

        var plan = AiMarketPolicy.Decide(Club(ClubId(1), squad), [tooDear, reasonable]);

        plan.Bids.Should().ContainSingle();
        plan.Bids.Single().ListingId.Should().Be(reasonable.ListingId);
    }

    [Fact]
    public void A_bid_is_limited_by_the_budget_band()
    {
        var squad = Squad(Composition(goalkeepers: 3, defenders: 7, midfielders: 6, attackers: 5));
        var listing = MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 1_000_000);

        // Half of a million is 500,000, below the floor, so a cash-poor club does not bid.
        var poor = AiMarketPolicy.Decide(Club(ClubId(1), squad, spendable: 1_000_000), [listing]);
        var rich = AiMarketPolicy.Decide(Club(ClubId(1), squad, spendable: 5_000_000_000), [listing]);

        poor.Bids.Should().BeEmpty("the budget band stops a club spending money it needs (FIN-10)");
        rich.Bids.Should().ContainSingle();
    }

    [Fact]
    public void At_most_the_per_pass_bid_cap_is_reached()
    {
        // Four depleted families and a listing in each, so the cap is what bounds the pass, not the needs.
        var squad = Squad(Composition(goalkeepers: 2, defenders: 6, midfielders: 6, attackers: 4));

        var listings = new[]
        {
            MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 100_000),
            Listing(Guid.CreateVersion7(), Guid.CreateVersion7(), PositionFamily.Goalkeeper, 100_000),
            Listing(Guid.CreateVersion7(), Guid.CreateVersion7(), PositionFamily.Defence, 100_000),
            Listing(Guid.CreateVersion7(), Guid.CreateVersion7(), PositionFamily.Attack, 100_000),
        };

        var plan = AiMarketPolicy.Decide(Club(ClubId(1), squad), listings);

        plan.Bids.Should().HaveCount(WorldRuleSet.AiMarketMaxBidsPerClub);
    }

    [Fact]
    public void A_club_in_the_red_bids_on_nothing()
    {
        var squad = Squad(Composition(goalkeepers: 3, defenders: 7, midfielders: 6, attackers: 5));
        var listing = MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 1_000_000);

        var healthy = AiMarketPolicy.Decide(Club(ClubId(1), squad), [listing]);
        var red = AiMarketPolicy.Decide(Club(ClubId(1), squad, finances: Finances() with { InTheRed = true }), [listing]);

        healthy.Bids.Should().ContainSingle();
        red.Bids.Should().BeEmpty("a club in the red has a transfer embargo (DEBT-1)");
    }

    [Fact]
    public void A_club_does_not_bid_when_the_wage_would_break_its_budget()
    {
        var squad = Squad(Composition(goalkeepers: 3, defenders: 7, midfielders: 6, attackers: 5));
        var listing = MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 1_000_000, buyerWage: 400_000_000);

        var room = AiMarketPolicy.Decide(Club(ClubId(1), squad, finances: Finances(bill: 500_000_000, budget: 1_000_000_000)), [listing]);
        var none = AiMarketPolicy.Decide(Club(ClubId(1), squad, finances: Finances(bill: 700_000_000, budget: 1_000_000_000)), [listing]);

        room.Bids.Should().ContainSingle();
        none.Bids.Should().BeEmpty("the wage bill with the player on it would be 11M against a budget of 10M");
    }

    [Fact]
    public void A_club_keeps_its_reserve_of_cash()
    {
        var squad = Squad(Composition(goalkeepers: 3, defenders: 7, midfielders: 6, attackers: 5));
        var listing = MidfieldListing(Guid.CreateVersion7(), Guid.CreateVersion7(), fee: 100_000_000);

        var noReserve = AiMarketPolicy.Decide(Club(ClubId(1), squad, spendable: 400_000_000), [listing]);
        var reserve = AiMarketPolicy.Decide(Club(ClubId(1), squad, spendable: 400_000_000, finances: Finances(reserve: 350_000_000)), [listing]);

        noReserve.Bids.Should().ContainSingle();
        reserve.Bids.Should().BeEmpty("the bid would leave less cash than the club keeps back");
    }

    [Fact]
    public void A_club_over_its_wage_budget_lists_its_best_paid_player_first()
    {
        // The squad is one above the target, so within its budget the club lists its weakest player; over it, the best paid goes first.
        var squad = FullSquad().Select((player, index) => player with { SeasonWageMinor = 10_000_000 + (index * 1_000_000) }).ToList();
        var best = squad[^1];

        var within = AiMarketPolicy.Decide(Club(ClubId(1), squad, finances: Finances(bill: 100_000_000, budget: 900_000_000)), []);
        var over = AiMarketPolicy.Decide(Club(ClubId(1), squad, finances: Finances(bill: 900_000_000, budget: 100_000_000)), []);

        within.Listings.Should().ContainSingle().Which.PlayerId.Should().NotBe(best.PlayerId);
        over.Listings.Should().NotBeEmpty();
        over.Listings[0].PlayerId.Should().Be(best.PlayerId);
    }

    private static PositionFamily[] Composition(int goalkeepers, int defenders, int midfielders, int attackers) =>
    [
        .. Enumerable.Repeat(PositionFamily.Goalkeeper, goalkeepers),
        .. Enumerable.Repeat(PositionFamily.Defence, defenders),
        .. Enumerable.Repeat(PositionFamily.Midfield, midfielders),
        .. Enumerable.Repeat(PositionFamily.Attack, attackers),
    ];

    private static PositionFamily[] FullComposition() =>
        Composition(
            WorldRuleSet.GeneratedGoalkeepers,
            WorldRuleSet.GeneratedDefenders,
            WorldRuleSet.GeneratedMidfielders,
            WorldRuleSet.GeneratedAttackers);

    private static List<AiMarketPlayer> FullSquad() => Squad(FullComposition());

    private static List<AiMarketPlayer> Squad(
        PositionFamily[] families,
        int ability = 12,
        int age = 25)
    {
        var players = new List<AiMarketPlayer>(families.Length);

        for (var index = 0; index < families.Length; index++)
        {
            players.Add(Player(index + 1, families[index], ability, SquadStatus.Rotation, age));
        }

        return players;
    }

    private static AiMarketPlayer Player(
        int ordinal,
        PositionFamily family,
        int ability,
        SquadStatus status,
        int age = 25) =>
        new(
            PlayerId(ordinal),
            family,
            ability * 10,
            age,
            status,
            3,
            CurrentSeasonNumber: 1,
            IsListed: false,
            SeasonWageMinor: 20_000_000);

    private static AiMarketFinances Finances(long bill = 0, long budget = long.MaxValue, long reserve = 0) =>
        new(bill, budget, reserve, InTheRed: false);

    private static AiMarketClub Club(
        Guid clubId,
        IReadOnlyList<AiMarketPlayer> players,
        long spendable = 5_000_000_000,
        AiMarketFinances? finances = null) =>
        new(clubId, spendable, finances ?? Finances(), players);

    private static AiMarketListing MidfieldListing(
        Guid listingId,
        Guid sellerClub,
        long fee,
        Guid? leadingClubId = null,
        long buyerWage = 20_000_000) =>
        Listing(listingId, sellerClub, PositionFamily.Midfield, fee, leadingClubId, buyerWage);

    private static AiMarketListing AttackListing(Guid listingId, Guid sellerClub, long fee) =>
        Listing(listingId, sellerClub, PositionFamily.Attack, fee);

    private static AiMarketListing Listing(
        Guid listingId,
        Guid sellerClub,
        PositionFamily family,
        long fee,
        Guid? leadingClubId = null,
        long buyerWage = 20_000_000) =>
        new(
            listingId,
            sellerClub,
            leadingClubId,
            PlayerId(900),
            family,
            AbilityTenths: 120,
            Age: 25,
            MinimumFeeMinor: fee,
            LeadingAmountMinor: null,
            BuyerSeasonWageMinor: buyerWage);

    private static Guid ClubId(int index) => Guid.Parse($"0192f100-0000-7000-8000-{index:D12}");

    private static Guid PlayerId(int ordinal) => Guid.Parse($"0192f300-0000-7000-8000-{ordinal:D12}");
}
