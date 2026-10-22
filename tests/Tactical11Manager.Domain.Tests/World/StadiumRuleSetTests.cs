using FluentAssertions;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.World;

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
    public void The_tier_one_prices_are_twenty_thirty_five_fifty_and_one_hundred_and_eighty()
    {
        StadiumRuleSet.TicketPriceMinorTier1(StadiumStand.Standing).Should().Be(2_000);
        StadiumRuleSet.TicketPriceMinorTier1(StadiumStand.Seating).Should().Be(3_500);
        StadiumRuleSet.TicketPriceMinorTier1(StadiumStand.CoveredSeating).Should().Be(5_000);
        StadiumRuleSet.TicketPriceMinorTier1(StadiumStand.Vip).Should().Be(18_000);
    }

    [Fact]
    public void A_place_that_sells_out_pays_for_itself_in_about_seven_seasons()
    {
        // 17 home matches in a double round-robin of 18 clubs.
        const int homeMatches = WorldRuleSet.ClubsPerDivision - 1;

        foreach (var stand in StadiumStands.All)
        {
            var seasonsToRepay = (double)StadiumRuleSet.BuildCostMinorTier1(stand)
                / (StadiumRuleSet.TicketPriceMinorTier1(stand) * homeMatches);

            seasonsToRepay.Should().BeInRange(5.5, 8.5, $"{stand} should repay in many seasons, so a ground is a project and not a purchase");
        }
    }

    [Fact]
    public void Prices_and_costs_follow_the_tiers_price_level_and_never_round_to_nothing()
    {
        foreach (var stand in StadiumStands.All)
        {
            StadiumRuleSet.TicketPriceMinorFor(stand, 2).Should().Be(StadiumRuleSet.TicketPriceMinorTier1(stand) * 5_333 / 10_000);
            StadiumRuleSet.BuildCostMinorFor(stand, 3).Should().Be(StadiumRuleSet.BuildCostMinorTier1(stand) * 2_844 / 10_000);
            StadiumRuleSet.TicketPriceMinorFor(stand, 12).Should().Be(StadiumRuleSet.TicketPriceMinorFor(stand, 5), "tier 5 and every tier below share a price level");
            StadiumRuleSet.TicketPriceMinorFor(stand, 12).Should().BeGreaterThan(0, "a price never rounds to nothing");
        }
    }

    [Fact]
    public void The_ground_costs_two_per_cent_of_what_its_places_cost_to_build_each_season()
    {
        var expected = StadiumStands.All.Sum(stand => Opening.Of(stand) * StadiumRuleSet.BuildCostMinorFor(stand, 1)) * 2 / 100;

        StadiumRuleSet.GroundUpkeepSeasonMinorFor(Opening, 1).Should().Be(expected);
        StadiumRuleSet.GroundUpkeepSeasonMinorFor(Opening, 2).Should().BeLessThan(expected);
    }

    [Fact]
    public void The_demand_shares_account_for_every_spectator()
    {
        StadiumStands.All.Sum(stand => StadiumRuleSet.DemandShareBp(stand)).Should().Be(10_000);
    }

    [Fact]
    public void The_mood_factor_runs_in_a_straight_line_from_four_fifths_to_six_fifths_through_calm()
    {
        StadiumRuleSet.MoodFactorBpFor(0).Should().Be(8_000, "furious fans stay away");
        StadiumRuleSet.MoodFactorBpFor(FanRuleSet.CalmMoodBp).Should().Be(10_000, "calm fans are the neutral crowd");
        StadiumRuleSet.MoodFactorBpFor(FanRuleSet.MoodMaxBp).Should().Be(12_000, "euphoric fans pack the ground");
        StadiumRuleSet.MoodFactorBpFor(7_500).Should().Be(11_000);
    }

    [Fact]
    public void A_mood_outside_the_scale_is_a_programming_error()
    {
        var below = () => StadiumRuleSet.MoodFactorBpFor(-1);
        var above = () => StadiumRuleSet.MoodFactorBpFor(FanRuleSet.MoodMaxBp + 1);

        below.Should().Throw<ArgumentOutOfRangeException>();
        above.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Opening_fans_with_a_calm_mood_draw_3000_2250_and_1688_in_tiers_one_two_and_three()
    {
        // FAN-1 / STAD-5: three in ten of the tier's opening fans want to come, and a calm mood changes nothing.
        StadiumRuleSet.MatchdayDemandFor(FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp).Should().Be(3_000);
        StadiumRuleSet.MatchdayDemandFor(FanRuleSet.StartFansFor(2), FanRuleSet.CalmMoodBp).Should().Be(2_250);
        StadiumRuleSet.MatchdayDemandFor(FanRuleSet.StartFansFor(3), FanRuleSet.CalmMoodBp).Should().Be(1_688);
    }

    [Fact]
    public void Demand_falls_with_the_tier_and_rises_with_the_fans_and_their_mood()
    {
        for (var tier = 1; tier < 6; tier++)
        {
            StadiumRuleSet.MatchdayDemandFor(FanRuleSet.StartFansFor(tier + 1), FanRuleSet.CalmMoodBp)
                .Should().BeLessThan(StadiumRuleSet.MatchdayDemandFor(FanRuleSet.StartFansFor(tier), FanRuleSet.CalmMoodBp));
        }

        StadiumRuleSet.MatchdayDemandFor(20_000, FanRuleSet.CalmMoodBp)
            .Should().BeGreaterThan(StadiumRuleSet.MatchdayDemandFor(10_000, FanRuleSet.CalmMoodBp));
        StadiumRuleSet.MatchdayDemandFor(10_000, FanRuleSet.MoodMaxBp)
            .Should().BeGreaterThan(StadiumRuleSet.MatchdayDemandFor(10_000, 0));
        StadiumRuleSet.MatchdayDemandFor(10_000, FanRuleSet.MoodMaxBp).Should().Be(3_600);
        StadiumRuleSet.MatchdayDemandFor(10_000, 0).Should().Be(2_400);
        StadiumRuleSet.MatchdayDemandFor(0, FanRuleSet.CalmMoodBp).Should().Be(0);
    }

    [Fact]
    public void A_club_at_the_tier_one_floor_and_furious_draws_nine_hundred_and_sixty()
    {
        StadiumRuleSet.MatchdayDemandFor(FanRuleSet.FloorFansFor(1), 0).Should().Be(960);
    }

    [Fact]
    public void The_opening_ground_sells_two_thousand_eight_hundred_and_ninety_of_the_three_thousand_who_want_to_come()
    {
        var demand = StadiumRuleSet.MatchdayDemandFor(FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp);

        // The 100 VIP places are oversubscribed from day one (210 want one), and no other kind is full.
        StadiumStands.All.Sum(stand => StadiumRuleSet.SoldFor(stand, Opening.Of(stand), demand)).Should().Be(2_890);
        StadiumRuleSet.SoldFor(StadiumStand.Vip, Opening.Vip, demand).Should().Be(Opening.Vip);
    }

    [Fact]
    public void A_top_tier_ground_fills_only_when_the_fans_have_grown_into_it()
    {
        var opening = StadiumRuleSet.MatchdayDemandFor(FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp);
        var grown = StadiumRuleSet.MatchdayDemandFor(FanRuleSet.StartFansFor(1) * 3, FanRuleSet.CalmMoodBp);

        opening.Should().BeLessThan(Opening.Total, "a new club cannot fill even its opening ground");
        grown.Should().BeGreaterThan(Opening.Total, "a club that has tripled its fans has a crowd waiting for a bigger stadium");
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
    public void An_opening_tier_one_club_takes_the_gate_of_the_places_its_fans_fill()
    {
        var gate = StadiumRuleSet.GateRevenueMinorFor(Opening, tier: 1, FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp);

        // 1,350 on the terrace, 600 in the seats, 840 under cover and all 100 VIP places.
        gate.Should().Be((1_350 * 2_000L) + (600 * 3_500L) + (840 * 5_000L) + (100 * 18_000L));
        gate.Should().BeLessThan(StadiumRuleSet.FullHouseMinorFor(Opening, 1), "the opening crowd does not fill the ground");
    }

    [Fact]
    public void A_crowd_that_outgrows_the_ground_takes_the_full_house()
    {
        StadiumRuleSet.GateRevenueMinorFor(Opening, tier: 1, fans: 100_000, FanRuleSet.CalmMoodBp)
            .Should().Be(StadiumRuleSet.FullHouseMinorFor(Opening, 1));
    }

    [Fact]
    public void Extra_places_the_fans_do_not_fill_earn_nothing()
    {
        // A tier-6 club's opening fans are fewer than even the opening ground holds, so building adds no income;
        // a tier-1 club's opening crowd already wants more VIP places than it has, so one more kind of place pays.
        var bigGround = new StadiumSeats(30_000, 8_000, 9_000, 3_000);
        var deepFans = FanRuleSet.StartFansFor(6);

        StadiumRuleSet.GateRevenueMinorFor(bigGround, tier: 6, deepFans, FanRuleSet.CalmMoodBp)
            .Should().Be(StadiumRuleSet.GateRevenueMinorFor(Opening, tier: 6, deepFans, FanRuleSet.CalmMoodBp));

        var topFans = FanRuleSet.StartFansFor(1);
        var moreVip = new StadiumSeats(3_000, 1_000, 900, 300);

        StadiumRuleSet.GateRevenueMinorFor(moreVip, tier: 1, topFans, FanRuleSet.CalmMoodBp)
            .Should().BeGreaterThan(StadiumRuleSet.GateRevenueMinorFor(Opening, tier: 1, topFans, FanRuleSet.CalmMoodBp));
        StadiumRuleSet.GateRevenueMinorFor(bigGround, tier: 1, topFans, FanRuleSet.CalmMoodBp)
            .Should().BeLessThan(StadiumRuleSet.FullHouseMinorFor(bigGround, 1) / 4, "most of a huge ground sits empty");
    }

    [Fact]
    public void More_fans_and_a_better_mood_never_lower_the_gate()
    {
        for (var mood = 0; mood < FanRuleSet.MoodMaxBp; mood += 500)
        {
            StadiumRuleSet.GateRevenueMinorFor(Opening, 4, 5_000, mood + 500)
                .Should().BeGreaterThanOrEqualTo(StadiumRuleSet.GateRevenueMinorFor(Opening, 4, 5_000, mood));
        }

        for (var fans = 1_000; fans < 40_000; fans += 1_000)
        {
            StadiumRuleSet.GateRevenueMinorFor(Opening, 4, fans + 1_000, FanRuleSet.CalmMoodBp)
                .Should().BeGreaterThanOrEqualTo(StadiumRuleSet.GateRevenueMinorFor(Opening, 4, fans, FanRuleSet.CalmMoodBp));
        }
    }

    [Fact]
    public void A_deeper_tier_takes_less_at_the_gate_than_the_tier_above()
    {
        for (var tier = 1; tier < 6; tier++)
        {
            StadiumRuleSet.GateRevenueMinorFor(Opening, tier + 1, FanRuleSet.StartFansFor(tier + 1), FanRuleSet.CalmMoodBp)
                .Should().BeLessThan(StadiumRuleSet.GateRevenueMinorFor(Opening, tier, FanRuleSet.StartFansFor(tier), FanRuleSet.CalmMoodBp));
        }
    }

    [Fact]
    public void A_season_of_home_gates_is_in_proportion_to_the_league_and_sponsor_money_it_sits_beside()
    {
        // A guard on the calibration: a ground must be a real income without dwarfing the club's other money.
        // 17 home matches at the opening ground against the league's money and the steady sponsor for the season.
        for (var tier = 1; tier <= 6; tier++)
        {
            var season = 17 * StadiumRuleSet.GateRevenueMinorFor(Opening, tier, FanRuleSet.StartFansFor(tier), FanRuleSet.CalmMoodBp);
            var fixedMoney = EconomyRuleSet.LeagueTvSeasonMinorFor(tier) + EconomyRuleSet.SponsorBaseSeasonMinorFor(tier);

            season.Should().BeGreaterThan(fixedMoney / 8, $"tier {tier}'s gate is a real income");
            season.Should().BeLessThan(fixedMoney * 4, $"tier {tier}'s gate does not drown the rest of the economy");
        }
    }

    [Fact]
    public void Attendance_is_every_place_sold_and_a_tier_one_opening_ground_seats_2890()
    {
        // 3,000 want to come: 1,350 standing, 600 seated, 840 covered, 210 VIP; the ground has 100 VIP places.
        StadiumRuleSet.AttendanceFor(Opening, FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp).Should().Be(2_890);
    }

    [Fact]
    public void Attendance_agrees_with_the_gate_it_prices()
    {
        foreach (var moodBp in new[] { 0, 2_500, FanRuleSet.CalmMoodBp, 7_500, FanRuleSet.MoodMaxBp })
        {
            var demand = StadiumRuleSet.MatchdayDemandFor(20_000, moodBp);
            var expected = StadiumStands.All.Sum(stand => StadiumRuleSet.SoldFor(stand, Opening.Of(stand), demand));

            StadiumRuleSet.AttendanceFor(Opening, 20_000, moodBp).Should().Be(expected);
        }
    }

    [Fact]
    public void Attendance_never_passes_the_ground_and_is_empty_without_fans()
    {
        StadiumRuleSet.AttendanceFor(Opening, 1_000_000, FanRuleSet.MoodMaxBp).Should().Be(Opening.Total);
        StadiumRuleSet.AttendanceFor(Opening, 0, FanRuleSet.CalmMoodBp).Should().Be(0);
    }
}
