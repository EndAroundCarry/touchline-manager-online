using FluentAssertions;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.Finance;

/// <summary>What a computer club decides about its sponsor, its objective, its ground and its buildings (`ai-finance-v1`).</summary>
public sealed class AiFinancePolicyTests
{
    private static readonly int[] SoldOut = [10_000, 10_000, 10_000, 10_000, 10_000];

    [Theory]
    [InlineData(AiTemperament.Cautious, SponsorOfferKind.Steady)]
    [InlineData(AiTemperament.Balanced, SponsorOfferKind.Balanced)]
    [InlineData(AiTemperament.Bold, SponsorOfferKind.Performance)]
    public void The_offer_follows_the_temperament(AiTemperament temperament, SponsorOfferKind expected)
    {
        AiFinancePolicy.ChooseOffer(temperament).Should().Be(expected);
    }

    [Fact]
    public void The_objective_is_the_boards_realistic_one()
    {
        AiFinancePolicy.ChooseObjective(5).Should().Be(SeasonObjective.TopSix);
        AiFinancePolicy.ChooseObjective(16).Should().Be(SeasonObjective.StayUp);
    }

    [Fact]
    public void A_ground_that_sold_out_grows_toward_the_demand_in_the_proportions_the_crowd_asks_for()
    {
        var orders = AiFinancePolicy.ChooseStands(Building(fans: 40_000, available: 5_000_000_000));

        orders.Should().NotBeEmpty();
        orders.Sum(order => order.Places).Should().BeGreaterThan(0);

        var standing = orders.Single(order => order.Stand == StadiumStand.Standing).Places;
        var vip = orders.SingleOrDefault(order => order.Stand == StadiumStand.Vip)?.Places ?? 0;

        standing.Should().BeGreaterThan(vip * 5, "the standing places are the biggest share of the demand");
    }

    [Fact]
    public void A_ground_that_did_not_sell_out_does_not_grow()
    {
        var notFull = Building(fans: 40_000, available: 5_000_000_000, sold: [9_000, 9_000, 9_000, 9_000, 9_000]);
        var tooFew = Building(fans: 40_000, available: 5_000_000_000, sold: [10_000, 10_000]);

        AiFinancePolicy.ChooseStands(notFull).Should().BeEmpty();
        AiFinancePolicy.ChooseStands(tooFew).Should().BeEmpty("five home matches are the evidence");
    }

    [Fact]
    public void A_club_in_the_red_builds_and_upgrades_nothing()
    {
        var red = Building(fans: 40_000, available: 5_000_000_000, inTheRed: true);

        AiFinancePolicy.ChooseStands(red).Should().BeEmpty();
        AiFinancePolicy.ChooseUpgrades(red).Should().BeEmpty();
    }

    [Fact]
    public void A_club_spends_at_most_half_of_its_cash_above_the_reserve_on_places()
    {
        var wageBill = 1_000_000_000;
        var available = 3_000_000_000;
        var orders = AiFinancePolicy.ChooseStands(Building(fans: 150_000, available: available, wageBill: wageBill));
        var cost = orders.Sum(order => order.Places * StadiumRuleSet.BuildCostMinorFor(order.Stand, 1));
        var budget = (available - (wageBill / 2)) / 2;

        cost.Should().BeLessThanOrEqualTo(budget);
        cost.Should().BeGreaterThan(budget / 2, "it spends most of what it may");
    }

    [Fact]
    public void A_club_with_no_cash_above_its_reserve_does_not_build()
    {
        AiFinancePolicy.ChooseStands(Building(fans: 40_000, available: 400_000_000, wageBill: 1_000_000_000)).Should().BeEmpty();
    }

    [Fact]
    public void A_full_ground_does_not_grow_past_the_top_level()
    {
        var full = Building(fans: 400_000, available: 50_000_000_000, seats: new StadiumSeats(33_750, 15_000, 21_000, 5_250));

        AiFinancePolicy.ChooseStands(full).Should().BeEmpty();
    }

    [Fact]
    public void A_building_is_upgraded_when_the_step_repays_within_half_a_season_and_not_before()
    {
        var grown = Building(fans: 80_000, available: 5_000_000_000, seats: new StadiumSeats(9_000, 4_000, 5_600, 1_400));
        var opening = Building(fans: 10_000, available: 5_000_000_000);

        AiFinancePolicy.ChooseUpgrades(grown).Should().NotBeEmpty();
        AiFinancePolicy.ChooseUpgrades(opening).Should().BeEmpty("the opening crowd cannot repay a step quickly");
    }

    [Fact]
    public void A_building_is_not_upgraded_when_it_would_leave_less_than_the_reserve()
    {
        var grown = Building(fans: 80_000, available: 30_000_000, seats: new StadiumSeats(9_000, 4_000, 5_600, 1_400), wageBill: 1_000_000_000);

        AiFinancePolicy.ChooseUpgrades(grown).Should().BeEmpty();
    }

    [Fact]
    public void A_building_at_the_top_level_is_not_upgraded()
    {
        var top = Building(fans: 80_000, available: 5_000_000_000, seats: new StadiumSeats(9_000, 4_000, 5_600, 1_400), snackLevel: 10, shopLevel: 10);

        AiFinancePolicy.ChooseUpgrades(top).Should().BeEmpty();
    }

    private static AiBuildingInput Building(
        int fans,
        long available,
        IReadOnlyList<int>? sold = null,
        long wageBill = 0,
        bool inTheRed = false,
        StadiumSeats? seats = null,
        int snackLevel = 1,
        int shopLevel = 1) =>
        new(
            Tier: 1,
            seats ?? new StadiumSeats(3_000, 1_000, 900, 100),
            fans,
            FanRuleSet.CalmMoodBp,
            sold ?? SoldOut,
            available,
            wageBill,
            inTheRed,
            snackLevel,
            shopLevel);
}
