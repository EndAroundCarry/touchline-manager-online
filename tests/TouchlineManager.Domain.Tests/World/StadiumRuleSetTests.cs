using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// The stadium's prices, costs and crowd: the shape of the economy the ground sits in (`STAD-3`…`STAD-5`).
/// </summary>
public sealed class StadiumRuleSetTests
{
    private static readonly StadiumSeats Opening = new(3_000, 1_000, 900, 100);

    [Fact]
    public void The_opening_ground_is_exactly_the_first_level()
    {
        Opening.Total.Should().Be(StadiumRuleSet.SeatsPerLevel);
        StadiumRuleSet.OpeningStanding.Should().Be(Opening.Standing);
        StadiumRuleSet.OpeningSeating.Should().Be(Opening.Seating);
        StadiumRuleSet.OpeningCoveredSeating.Should().Be(Opening.CoveredSeating);
        StadiumRuleSet.OpeningVip.Should().Be(Opening.Vip);
        StadiumRuleSet.MaxCapacity.Should().Be(StadiumRuleSet.SeatsPerLevel * StadiumRuleSet.MaxLevel);
    }

    [Fact]
    public void A_better_place_costs_more_to_sit_in_and_more_to_build()
    {
        var prices = StadiumStands.All.Select(stand => StadiumRuleSet.TicketPriceMinorTier1(stand)).ToList();
        var costs = StadiumStands.All.Select(stand => StadiumRuleSet.BuildCostMinorTier1(stand)).ToList();

        prices.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        costs.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_suggested_tier_one_prices_are_eight_fifteen_twenty_five_and_ninety()
    {
        StadiumRuleSet.TicketPriceMinorTier1(StadiumStand.Standing).Should().Be(800);
        StadiumRuleSet.TicketPriceMinorTier1(StadiumStand.Seating).Should().Be(1_500);
        StadiumRuleSet.TicketPriceMinorTier1(StadiumStand.CoveredSeating).Should().Be(2_500);
        StadiumRuleSet.TicketPriceMinorTier1(StadiumStand.Vip).Should().Be(9_000);
    }

    [Fact]
    public void A_place_that_sells_out_pays_for_itself_in_a_couple_of_seasons()
    {
        // 17 home matches in a double round-robin of 18 clubs.
        const int homeMatches = WorldRuleSet.ClubsPerDivision - 1;

        foreach (var stand in StadiumStands.All)
        {
            var seasonsToRepay = (double)StadiumRuleSet.BuildCostMinorTier1(stand)
                / (StadiumRuleSet.TicketPriceMinorTier1(stand) * homeMatches);

            seasonsToRepay.Should().BeInRange(1.5, 3.5, $"{stand} should repay in a couple of seasons, not at once or never");
        }
    }

    [Fact]
    public void Prices_and_costs_halve_with_each_tier_like_every_other_baseline()
    {
        foreach (var stand in StadiumStands.All)
        {
            StadiumRuleSet.TicketPriceMinorFor(stand, 2).Should().Be(StadiumRuleSet.TicketPriceMinorFor(stand, 1) / 2);
            StadiumRuleSet.BuildCostMinorFor(stand, 3).Should().Be(StadiumRuleSet.BuildCostMinorFor(stand, 1) / 4);
            StadiumRuleSet.TicketPriceMinorFor(stand, 12).Should().BeGreaterThan(0, "a price never rounds to nothing");
        }
    }

    [Fact]
    public void The_demand_shares_account_for_every_spectator()
    {
        StadiumStands.All.Sum(stand => StadiumRuleSet.DemandShareBp(stand)).Should().Be(10_000);
    }

    [Fact]
    public void Demand_falls_with_the_tier_and_rises_with_league_position()
    {
        StadiumRuleSet.MatchdayDemandFor(2, formRank: 9).Should().BeLessThan(StadiumRuleSet.MatchdayDemandFor(1, 9));
        StadiumRuleSet.MatchdayDemandFor(5, 9).Should().BeLessThan(StadiumRuleSet.MatchdayDemandFor(4, 9));
        StadiumRuleSet.MatchdayDemandFor(1, formRank: 1).Should().BeGreaterThan(StadiumRuleSet.MatchdayDemandFor(1, 18));
        StadiumRuleSet.MatchdayDemandFor(1, formRank: 9).Should().Be(StadiumRuleSet.MatchdayDemandTier1);
        StadiumRuleSet.MatchdayDemandFor(40, formRank: 9).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_top_tier_crowd_is_bigger_than_the_opening_ground_but_a_deep_tier_crowd_is_not()
    {
        StadiumRuleSet.MatchdayDemandFor(1, 9).Should().BeGreaterThan(Opening.Total);
        StadiumRuleSet.MatchdayDemandFor(6, 9).Should().BeLessThan(Opening.Total);
    }

    [Fact]
    public void A_stand_never_sells_more_than_it_holds_or_more_than_the_crowd_wants()
    {
        StadiumRuleSet.SoldFor(StadiumStand.Standing, seats: 3_000, demand: 100_000).Should().Be(3_000);
        StadiumRuleSet.SoldFor(StadiumStand.Standing, seats: 3_000, demand: 1_000).Should().Be(450);
        StadiumRuleSet.SoldFor(StadiumStand.Vip, seats: 0, demand: 100_000).Should().Be(0);
        StadiumRuleSet.SoldFor(StadiumStand.Vip, seats: 100, demand: 0).Should().Be(0);
    }

    [Fact]
    public void An_opening_tier_one_ground_sells_out_and_takes_the_full_house_at_the_gate()
    {
        var gate = StadiumRuleSet.GateRevenueMinorFor(Opening, tier: 1, formRank: 9);

        gate.Should().Be(StadiumRuleSet.FullHouseMinorFor(Opening, 1));
        gate.Should().Be((3_000 * 800L) + (1_000 * 1_500L) + (900 * 2_500L) + (100 * 9_000L));
    }

    [Fact]
    public void Extra_places_nobody_turns_up_for_earn_nothing()
    {
        // A tier-6 crowd is far smaller than a 50,000 ground, so building to the top adds no income.
        var bigGround = new StadiumSeats(30_000, 8_000, 9_000, 3_000);
        var small = StadiumRuleSet.GateRevenueMinorFor(Opening, tier: 6, formRank: 9);
        var large = StadiumRuleSet.GateRevenueMinorFor(bigGround, tier: 6, formRank: 9);

        large.Should().BeGreaterThan(small, "the places the crowd does fill still pay");
        large.Should().BeLessThan(StadiumRuleSet.FullHouseMinorFor(bigGround, 6) / 4, "most of a huge ground sits empty");
    }

    [Fact]
    public void A_better_league_position_never_lowers_the_gate()
    {
        for (var rank = 1; rank < WorldRuleSet.ClubsPerDivision; rank++)
        {
            StadiumRuleSet.GateRevenueMinorFor(Opening, 4, rank)
                .Should().BeGreaterThanOrEqualTo(StadiumRuleSet.GateRevenueMinorFor(Opening, 4, rank + 1));
        }
    }

    [Fact]
    public void A_deeper_tier_takes_less_at_the_gate_than_the_tier_above()
    {
        for (var tier = 1; tier < 6; tier++)
        {
            StadiumRuleSet.GateRevenueMinorFor(Opening, tier + 1, 9)
                .Should().BeLessThan(StadiumRuleSet.GateRevenueMinorFor(Opening, tier, 9));
        }
    }

    [Fact]
    public void A_season_of_home_gates_is_in_proportion_to_the_weekly_sponsorship_it_sits_beside()
    {
        // A guard on the calibration: a ground must be a real income without dwarfing the club's other money.
        // 17 home matches at the opening ground against 34 weekly sponsorship credits.
        for (var tier = 1; tier <= 6; tier++)
        {
            var season = 17 * StadiumRuleSet.GateRevenueMinorFor(Opening, tier, 9);
            var sponsorship = 34 * WorldRuleSet.WeeklySponsorshipMinorForTier(tier);

            season.Should().BeGreaterThan(sponsorship / 4, $"tier {tier}'s gate is a real income");
            season.Should().BeLessThan(sponsorship * 4, $"tier {tier}'s gate does not drown the rest of the economy");
        }
    }
}
