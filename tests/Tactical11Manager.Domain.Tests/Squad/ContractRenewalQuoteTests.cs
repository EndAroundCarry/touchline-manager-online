using FluentAssertions;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>
/// The deterministic renewal quote (`CON-3`).
/// </summary>
/// <remarks>
/// The quote is a pure function, so its properties are shapes rather than exact wages: the same input always
/// quotes the same wage, a better or more available player costs more, a longer deal costs less each season, a
/// player whose deal is running out holds the stronger hand, and no wage is under the floor.
/// </remarks>
public sealed class ContractRenewalQuoteTests
{
    [Fact]
    public void The_same_player_always_quotes_the_same_wage()
    {
        var input = Input();

        ContractRenewalQuote.Calculate(input, 2).Should().Be(ContractRenewalQuote.Calculate(input, 2));
    }

    [Fact]
    public void A_longer_deal_pays_less_each_season()
    {
        ContractRenewalQuote.Calculate(Input(), 1).SeasonWageMinor.Should()
            .BeGreaterThan(ContractRenewalQuote.Calculate(Input(), 3).SeasonWageMinor);
    }

    [Fact]
    public void A_better_player_costs_more()
    {
        ContractRenewalQuote.Calculate(Input(ability: 16), 2).SeasonWageMinor.Should()
            .BeGreaterThan(ContractRenewalQuote.Calculate(Input(ability: 10), 2).SeasonWageMinor);
    }

    [Fact]
    public void A_player_whose_deal_is_running_out_costs_more()
    {
        ContractRenewalQuote.Calculate(Input(remainingSeasons: 0), 2).SeasonWageMinor.Should()
            .BeGreaterThan(ContractRenewalQuote.Calculate(Input(remainingSeasons: 3), 2).SeasonWageMinor);
    }

    [Fact]
    public void The_quote_names_the_term_it_was_asked_for()
    {
        var terms = ContractRenewalQuote.Calculate(Input(), 3);

        terms.Seasons.Should().Be(3);
        terms.SeasonWageMinor.Should().BeGreaterThanOrEqualTo(EconomyRuleSet.SeasonWageFloorMinor, "a renewal is never free");
    }

    [Fact]
    public void A_young_player_is_quoted_for_what_the_market_takes_him_to_be()
    {
        // The wage reads two parts what he is and one part what the market assumes he will become: no hidden potential.
        var young = ContractRenewalQuote.Calculate(Input(age: 19), 2).SeasonWageMinor;
        var peak = ContractRenewalQuote.Calculate(Input(age: 26), 2).SeasonWageMinor;

        young.Should().BeGreaterThan(peak * 8 / 10, "youth is paid for, and the age factor is the only thing that pulls the other way");
    }

    [Fact]
    public void The_same_ability_costs_the_same_wherever_he_plays()
    {
        typeof(ContractRenewalInput).GetProperties().Select(property => property.Name)
            .Should().NotContain(["Tier", "Potential"], "a player's price follows the player (`CON-3`)");
    }

    [Fact]
    public void A_term_outside_the_rules_is_a_programming_error()
    {
        var act = () => ContractRenewalQuote.Calculate(Input(), 4);

        act.Should().Throw<ArgumentOutOfRangeException>("CON-1 allows one to three seasons");
    }

    [Fact]
    public void An_ability_off_its_scale_is_a_programming_error()
    {
        var act = () => ContractRenewalQuote.Calculate(Input(ability: 21), 2);

        act.Should().Throw<ArgumentOutOfRangeException>("TRN-4 bounds ability at twenty");
    }

    private static ContractRenewalInput Input(
        int ability = 13,
        int age = 25,
        int appearances = 15,
        int moraleBp = 5_000,
        int remainingSeasons = 1) =>
        new(ability * 10, age, appearances, moraleBp, remainingSeasons);
}
