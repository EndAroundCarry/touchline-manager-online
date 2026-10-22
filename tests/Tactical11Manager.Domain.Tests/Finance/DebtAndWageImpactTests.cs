using FluentAssertions;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Finance;

/// <summary>In the red, administration and dismissal, the board's sales, and the warning a bid gets about its wage (`DEBT-*`, `WAGE-*`).</summary>
public sealed class DebtAndWageImpactTests
{
    private const long Limit = 250_000_000;

    [Fact]
    public void A_club_with_cash_is_healthy_and_a_club_below_zero_is_in_the_red()
    {
        ClubFinancialState.Next(FinancialStateKind.Healthy, 0, 5, 1, Limit).State.Should().Be(FinancialStateKind.Healthy);
        ClubFinancialState.Next(FinancialStateKind.Healthy, 0, 5, 0, Limit).State.Should().Be(FinancialStateKind.Healthy, "zero is not the red");

        var red = ClubFinancialState.Next(FinancialStateKind.Healthy, 0, 5, -1, Limit);

        red.State.Should().Be(FinancialStateKind.InTheRed);
        red.Transition.Should().Be(FinancialTransition.EnteredRed);
        red.SinceMatchday.Should().Be(5);
    }

    [Fact]
    public void A_club_past_its_overdraft_limit_is_in_administration()
    {
        ClubFinancialState.Next(FinancialStateKind.InTheRed, 5, 9, -Limit, Limit).State.Should().Be(FinancialStateKind.InTheRed, "at the limit is still the red");

        var step = ClubFinancialState.Next(FinancialStateKind.InTheRed, 5, 9, -Limit - 1, Limit);

        step.State.Should().Be(FinancialStateKind.Administration);
        step.Transition.Should().Be(FinancialTransition.EnteredAdministration);
        step.SinceMatchday.Should().Be(9);
        step.Dismiss.Should().BeFalse();
    }

    [Fact]
    public void A_club_leaves_administration_when_it_is_back_inside_its_limit_and_leaves_the_red_at_zero()
    {
        var inside = ClubFinancialState.Next(FinancialStateKind.Administration, 3, 20, -Limit, Limit);
        var healthy = ClubFinancialState.Next(FinancialStateKind.InTheRed, 3, 20, 0, Limit);

        inside.State.Should().Be(FinancialStateKind.InTheRed);
        inside.Transition.Should().Be(FinancialTransition.LeftAdministration);
        healthy.State.Should().Be(FinancialStateKind.Healthy);
        healthy.Transition.Should().Be(FinancialTransition.LeftRed);
    }

    [Fact]
    public void A_manager_still_in_administration_a_whole_season_later_is_dismissed()
    {
        var before = ClubFinancialState.Next(FinancialStateKind.Administration, 10, 43, -Limit - 1, Limit);
        var at = ClubFinancialState.Next(FinancialStateKind.Administration, 10, 44, -Limit - 1, Limit);

        before.Dismiss.Should().BeFalse();
        at.Dismiss.Should().BeTrue();
        at.SinceMatchday.Should().Be(10, "the club has been here since matchday 10");
    }

    [Fact]
    public void The_emergency_grant_is_the_integrity_net_below_twice_the_limit()
    {
        ClubFinancialState.Next(FinancialStateKind.Administration, 1, 5, -2 * Limit, Limit).EmergencyGrantMinor.Should().Be(0);
        ClubFinancialState.Next(FinancialStateKind.Administration, 1, 5, (-2 * Limit) - 7, Limit).EmergencyGrantMinor.Should().Be(7);
    }

    [Fact]
    public void A_player_nobody_buys_is_released_and_his_agent_pays_the_club_thirty_per_cent_of_his_value()
    {
        EconomyRuleSet.AdministrationReleaseFeeMinor(1_000_000_000).Should().Be(300_000_000);
        EconomyRuleSet.AdministrationReleaseFeeMinor(-5).Should().Be(0);
        EconomyRuleSet.AdministrationReleaseFeeMinor(1_000_000_000).Should().BeLessThan(1_000_000_000L * EconomyRuleSet.AdministrationAskingBp / 10_000, "a release brings in less than a sale");
    }

    [Fact]
    public void The_board_lists_the_highest_earners_until_the_bill_is_under_the_budget_at_most_five()
    {
        var squad = Squad(24, wage: index => 10_000_000 + (index * 1_000_000));
        var bill = squad.Sum(player => player.SeasonWageMinor);

        var sales = ClubFinancialState.ChooseSales(squad, bill, bill - 40_000_000);

        sales.Should().HaveCount(2, "the best-paid player earns 33M, which is not enough to save 40M, and the next 32M is");
        sales.Select(sale => sale.PlayerId).Should().Equal(squad[23].PlayerId, squad[22].PlayerId);
        sales[0].AskingMinor.Should().Be(squad[23].ValueMinor * 8_000 / 10_000);
        ClubFinancialState.ChooseSales(squad, bill, 0).Should().HaveCount(5, "at most five in one pass");
        ClubFinancialState.ChooseSales(squad, bill, bill).Should().BeEmpty("under budget there is nothing to sell");
    }

    [Fact]
    public void The_board_never_sells_below_the_legal_squad_or_the_second_goalkeeper()
    {
        var squad = Squad(18, wage: index => 10_000_000 + (index * 1_000_000));
        var bill = squad.Sum(player => player.SeasonWageMinor);

        ClubFinancialState.ChooseSales(squad, bill, 0).Should().BeEmpty("eighteen is the minimum squad");

        var keepers = Squad(20, wage: index => index < 2 ? 90_000_000 : 10_000_000, keepers: 2);

        ClubFinancialState.ChooseSales(keepers, keepers.Sum(player => player.SeasonWageMinor), 0)
            .Select(sale => sale.PlayerId).Should().NotIntersectWith(keepers.Where(player => player.Family == PositionFamily.Goalkeeper).Select(player => player.PlayerId));
    }

    [Fact]
    public void A_player_already_on_the_market_counts_as_gone_and_is_not_listed_again()
    {
        var squad = Squad(24, wage: index => 10_000_000 + (index * 1_000_000));
        squad[23] = squad[23] with { IsListed = true };
        var bill = squad.Sum(player => player.SeasonWageMinor);

        var sales = ClubFinancialState.ChooseSales(squad, bill, bill - 33_000_000);

        sales.Should().BeEmpty("the listed 33M player already brings the bill under the budget when he sells");
    }

    [Fact]
    public void A_signing_that_fits_the_budget_and_leaves_cash_is_fine()
    {
        var result = WageImpact.Evaluate(Input(cash: 500_000_000, fee: 100_000_000, wage: 50_000_000, bill: 400_000_000, forecast: 1_000_000_000), Limit);

        result.Level.Should().Be(WageImpactLevel.Fits);
        result.NeedsConfirmation.Should().BeFalse();
        result.WageBillAfterMinor.Should().Be(450_000_000);
        result.WageBudgetMinor.Should().Be(700_000_000);
        result.WageShareBp.Should().Be(4_500);
    }

    [Fact]
    public void A_signing_over_the_budget_is_stretching_while_the_season_still_ends_with_cash()
    {
        var result = WageImpact.Evaluate(Input(cash: 900_000_000, fee: 0, wage: 120_000_000, bill: 650_000_000, forecast: 1_000_000_000, fixedCost: 100_000_000), Limit);

        result.Level.Should().Be(WageImpactLevel.Stretching);
        result.NeedsConfirmation.Should().BeFalse();
        result.SeasonEndCashMinor.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_signing_that_puts_the_club_in_the_red_before_the_season_ends_is_danger_and_says_when()
    {
        var result = WageImpact.Evaluate(Input(cash: 100_000_000, fee: 0, wage: 400_000_000, bill: 700_000_000, forecast: 1_000_000_000, fixedCost: 100_000_000, played: 10), Limit);

        result.Level.Should().Be(WageImpactLevel.Danger);
        result.NeedsConfirmation.Should().BeTrue("a danger bid asks for a confirm tick but is never refused");
        result.SeasonEndCashMinor.Should().BeNegative();
        result.RedFromMatchday.Should().BeInRange(11, 34);
    }

    [Fact]
    public void A_signing_that_would_take_the_club_past_its_limit_is_an_administration_risk()
    {
        var result = WageImpact.Evaluate(Input(cash: 100_000_000, fee: 0, wage: 1_500_000_000, bill: 700_000_000, forecast: 1_000_000_000, fixedCost: 100_000_000), Limit);

        result.Level.Should().Be(WageImpactLevel.AdministrationRisk);
        result.NeedsConfirmation.Should().BeTrue();
    }

    [Fact]
    public void The_fee_counts_against_the_cash_the_season_ends_with()
    {
        var cheap = WageImpact.Evaluate(Input(cash: 500_000_000, fee: 0, wage: 50_000_000, bill: 400_000_000, forecast: 1_000_000_000), Limit);
        var dear = WageImpact.Evaluate(Input(cash: 500_000_000, fee: 500_000_000, wage: 50_000_000, bill: 400_000_000, forecast: 1_000_000_000), Limit);

        (cheap.SeasonEndCashMinor - dear.SeasonEndCashMinor).Should().Be(500_000_000);
    }

    [Fact]
    public void Late_in_the_season_a_wage_costs_only_the_matchdays_that_are_left()
    {
        var early = WageImpact.Evaluate(Input(cash: 0, fee: 0, wage: 340_000_000, bill: 0, forecast: 100_000_000, played: 0), Limit);
        var late = WageImpact.Evaluate(Input(cash: 0, fee: 0, wage: 340_000_000, bill: 0, forecast: 100_000_000, played: 30), Limit);

        late.SeasonEndCashMinor.Should().BeGreaterThan(early.SeasonEndCashMinor);
    }

    private static List<AdministrationPlayer> Squad(int count, Func<int, long> wage, int keepers = 3) =>
        [.. Enumerable.Range(0, count).Select(index => new AdministrationPlayer(
            Guid.Parse($"0192f300-0000-7000-8000-{index + 1:D12}"),
            index < keepers ? PositionFamily.Goalkeeper : PositionFamily.Midfield,
            wage(index),
            wage(index) * 10,
            IsListed: false))];

    private static WageImpactInput Input(
        long cash,
        long fee,
        long wage,
        long bill,
        long forecast,
        long fixedCost = 0,
        int played = 0) =>
        new(cash, fee, wage, bill, forecast, fixedCost, played);
}
