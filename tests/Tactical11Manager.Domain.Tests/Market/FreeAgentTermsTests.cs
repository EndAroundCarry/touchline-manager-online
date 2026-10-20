using FluentAssertions;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;

namespace Tactical11Manager.Domain.Tests.Market;

/// <summary>A free agent's price, wage, contract length and standing (`FA-6`, `FA-7`).</summary>
public sealed class FreeAgentTermsTests
{
    [Theory]
    [InlineData(17, 3, SquadStatus.Prospect)]
    [InlineData(21, 3, SquadStatus.Prospect)]
    [InlineData(22, 2, SquadStatus.Rotation)]
    [InlineData(29, 2, SquadStatus.Rotation)]
    [InlineData(30, 1, SquadStatus.Rotation)]
    [InlineData(34, 1, SquadStatus.Rotation)]
    public void The_contract_length_and_standing_follow_age_alone(int age, int seasons, SquadStatus status)
    {
        var terms = FreeAgentTerms.Calculate(ability: 8, age, referenceTier: 1);

        terms.ContractSeasons.Should().Be(seasons);
        terms.SquadStatus.Should().Be(status);
    }

    [Fact]
    public void The_minimum_fee_is_a_quarter_of_the_value_and_never_below_one_bid_increment()
    {
        foreach (var ability in new[] { 6, 8, 10 })
        {
            foreach (var age in new[] { 18, 24, 30 })
            {
                var valuation = PlayerValuation.Estimate(new PlayerValuationInput(ability, ability, age, 1));
                var terms = FreeAgentTerms.Calculate(ability, age, referenceTier: 1);

                terms.MinimumFeeMinor.Should().Be(
                    Math.Max(WorldRuleSet.MinimumBidIncrementMinor, valuation * WorldRuleSet.FreeAgentMinimumFeeBp / 10_000),
                    $"ability {ability}, age {age}");
                terms.MinimumFeeMinor.Should().BeGreaterThanOrEqualTo(WorldRuleSet.MinimumBidIncrementMinor);
                terms.WeeklyWageMinor.Should().BePositive();
            }
        }
    }

    [Fact]
    public void A_better_player_costs_and_earns_more_and_a_deeper_tier_costs_less()
    {
        var weak = FreeAgentTerms.Calculate(6, 26, 1);
        var strong = FreeAgentTerms.Calculate(10, 26, 1);
        var lower = FreeAgentTerms.Calculate(8, 26, 3);
        var top = FreeAgentTerms.Calculate(8, 26, 1);

        strong.MinimumFeeMinor.Should().BeGreaterThan(weak.MinimumFeeMinor);
        strong.WeeklyWageMinor.Should().BeGreaterThan(weak.WeeklyWageMinor);
        lower.WeeklyWageMinor.Should().BeLessThan(top.WeeklyWageMinor);
    }

    [Fact]
    public void A_gems_price_and_wage_are_those_of_a_normal_player_of_his_shown_ability_and_age()
    {
        var request = new FreeAgentBatchRequest(
            "free-agent-world",
            Guid.CreateVersion7(),
            new DateOnly(2026, 10, 19),
            2026,
            [new FreeAgentCountry("ENG", "england", 100_000)],
            new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

        var batch = FreeAgentGenerator.Generate(request);
        var gems = batch.Where(agent => agent.IsGem).ToList();

        gems.Should().NotBeEmpty();

        foreach (var gem in gems)
        {
            var age = gem.Player.AgeIn(2026);
            var terms = FreeAgentTerms.Calculate(gem.Ability, age, referenceTier: 1);

            // Priced as if his ceiling were his ability, exactly like the player beside him on the page.
            var asShown = PlayerValuation.Estimate(new PlayerValuationInput(gem.Ability, gem.Ability, age, 1));
            var withCeiling = PlayerValuation.Estimate(new PlayerValuationInput(gem.Ability, gem.Player.Potential, age, 1));

            terms.MinimumFeeMinor.Should().Be(
                Math.Max(WorldRuleSet.MinimumBidIncrementMinor, asShown * WorldRuleSet.FreeAgentMinimumFeeBp / 10_000));

            withCeiling.Should().BeGreaterThan(
                asShown,
                "reading the ceiling would have priced him higher, so the price would have given him away");
        }
    }

    [Fact]
    public void A_released_player_costs_his_full_value_with_his_real_potential()
    {
        foreach (var ability in new[] { 4, 10, 16 })
        {
            foreach (var age in new[] { 19, 25, 33 })
            {
                var potential = Math.Min(WorldRuleSet.AttributeMax, ability + 2);
                var valuation = PlayerValuation.Estimate(new PlayerValuationInput(ability, potential, age, 1));
                var released = FreeAgentTerms.CalculateReleased(ability, potential, age, referenceTier: 1);
                var generated = FreeAgentTerms.Calculate(ability, age, referenceTier: 1);

                released.MinimumFeeMinor.Should().Be(
                    Math.Max(WorldRuleSet.MinimumBidIncrementMinor, valuation),
                    $"ability {ability}, age {age}");
                released.MinimumFeeMinor.Should().BeGreaterThanOrEqualTo(generated.MinimumFeeMinor);
                released.WeeklyWageMinor.Should().Be(generated.WeeklyWageMinor, "the wage is priced on ability alone");
                released.ContractSeasons.Should().Be(generated.ContractSeasons);
                released.SquadStatus.Should().Be(generated.SquadStatus);
            }
        }
    }

    [Fact]
    public void A_released_player_with_a_higher_ceiling_costs_more()
    {
        var low = FreeAgentTerms.CalculateReleased(10, 10, 20, 1);
        var high = FreeAgentTerms.CalculateReleased(10, 16, 20, 1);

        high.MinimumFeeMinor.Should().BeGreaterThan(low.MinimumFeeMinor);
    }

    [Fact]
    public void Terms_off_the_scale_are_refused()
    {
        FluentActions.Invoking(() => FreeAgentTerms.Calculate(0, 25, 1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => FreeAgentTerms.Calculate(21, 25, 1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => FreeAgentTerms.Calculate(8, 25, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => FreeAgentTerms.CalculateReleased(8, 21, 25, 1)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
