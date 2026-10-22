using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// What the finance screen's forecast, wage gauge, state banner and prize are read from (plan toasty-scott sections 2a, 5 and 6).
/// </summary>
public sealed class FinanceOutlooksTests
{
    private static readonly Guid ClubId = Guid.CreateVersion7();
    private static readonly StadiumSeats Ground = new(5_000, 0, 0, 0);

    [Fact]
    public void The_streams_add_up_to_the_forecast_less_the_costs()
    {
        var basis = Basis(sponsorFixedMatchdayMinor: 100_000, seasonWageMinor: 4_000_000);

        var outlook = FinanceOutlooks.Compose(basis, Money(), Standing(), settledMatchdayOrdinal: 10);

        outlook.ForecastMinor.Should().Be(basis.ForecastMinor);
        outlook.Streams.Where(stream => stream.ForecastMinor > 0).Sum(stream => stream.ForecastMinor)
            .Should().Be(basis.ForecastMinor);
        outlook.Streams.Where(stream => stream.ForecastMinor < 0).Sum(stream => -stream.ForecastMinor)
            .Should().Be(4_000_000 + basis.CostBreakdown.TotalMinor);
        outlook.Streams.Select(stream => stream.Category).Should().Contain(
            ["league_tv", "sponsor_matchday", "gate_receipt", "snack_bar", "merchandise", "wages", "operating_cost", "structure_upkeep", "ground_upkeep"]);
    }

    [Fact]
    public void The_wage_budget_is_seventy_percent_of_the_forecast_and_a_bigger_bill_is_over_it()
    {
        var basis = Basis(seasonWageMinor: 1);
        var budget = EconomyRuleSet.WageBudgetMinorFor(basis.ForecastMinor);

        var fits = FinanceOutlooks.Compose(Basis(seasonWageMinor: budget), Money(), Standing(), 0);
        var over = FinanceOutlooks.Compose(Basis(seasonWageMinor: budget + 1), Money(), Standing(), 0);

        fits.WageBudgetMinor.Should().Be(budget);
        fits.OverBudget.Should().BeFalse();
        over.OverBudget.Should().BeTrue();
        over.WageShareBp.Should().BeGreaterThan(EconomyRuleSet.WageBudgetBp - 1);
    }

    [Fact]
    public void A_healthy_club_has_no_embargo_and_no_dismissal_clock()
    {
        var outlook = FinanceOutlooks.Compose(Basis(), Money(), Standing(), 10);

        outlook.State.Should().Be("healthy");
        outlook.Embargo.Should().BeFalse();
        outlook.AdministrationMatchdaysLeft.Should().BeNull();
    }

    [Fact]
    public void A_club_in_the_red_is_embargoed_without_a_dismissal_clock()
    {
        var outlook = FinanceOutlooks.Compose(Basis(), Money(), Standing(FinancialStateKind.InTheRed, since: 4), 10);

        outlook.State.Should().Be("in_the_red");
        outlook.Embargo.Should().BeTrue();
        outlook.AdministrationMatchdaysLeft.Should().BeNull();
    }

    [Theory]
    [InlineData(10, 10, 30)]
    [InlineData(10, 30, 10)]
    [InlineData(10, 40, 0)]
    [InlineData(10, 80, 0)]
    public void The_dismissal_clock_counts_a_whole_season_from_the_matchday_administration_began(int since, int now, int expected)
    {
        var outlook = FinanceOutlooks.Compose(Basis(), Money(), Standing(FinancialStateKind.Administration, since), now);

        outlook.AdministrationMatchdaysLeft.Should().Be(expected);
    }

    [Fact]
    public void The_prize_is_the_one_the_present_place_would_be_paid_at_the_clubs_tier()
    {
        var outlook = FinanceOutlooks.Compose(Basis(), Money(), Standing(place: 7, clubs: EconomyRuleSet.ClubsPerDivision), 10);

        outlook.Prize.Should().NotBeNull();
        outlook.Prize!.Place.Should().Be(7);
        outlook.Prize.Clubs.Should().Be(EconomyRuleSet.ClubsPerDivision);
        outlook.Prize.AmountMinor.Should().Be(EconomyRuleSet.PrizeMinorFor(1, 7));
    }

    [Fact]
    public void Without_a_table_there_is_no_prize()
    {
        FinanceOutlooks.Compose(Basis(), Money(), Standing(place: null, clubs: 0), 0).Prize.Should().BeNull();
    }

    private static ClubFinanceBasis Basis(long sponsorFixedMatchdayMinor = 0, long seasonWageMinor = 3_000_000) =>
        new(ClubId, 1, Ground, 20_000, FanRuleSet.CalmMoodBp, 1, 1, sponsorFixedMatchdayMinor, seasonWageMinor, 22);

    private static ClubMoneyPosition Money() => new(1_000_000, 0, 0, MatchdaysPlayed: 10);

    private static ClubFinanceStanding Standing(
        FinancialStateKind state = FinancialStateKind.Healthy,
        int since = 0,
        int? place = 5,
        int clubs = EconomyRuleSet.ClubsPerDivision) =>
        new(state, since, place, clubs);
}
