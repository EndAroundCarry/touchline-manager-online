using FluentAssertions;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Tests.Market;

/// <summary>
/// The deterministic transfer valuation (`TRF-12`).
/// </summary>
/// <remarks>
/// A pure function, so these tests need neither a clock nor a database. What they pin is the contract the
/// policy depends on: the same facts always produce the same value, the value moves the right way with
/// ability, potential, and age, and no fact can push it out of a sensible band.
/// </remarks>
public sealed class PlayerValuationTests
{
    [Fact]
    public void The_same_facts_value_a_player_the_same_way()
    {
        var input = new PlayerValuationInput(13, 15, 24, 1);

        PlayerValuation.Estimate(input).Should().Be(PlayerValuation.Estimate(input));
    }

    [Fact]
    public void A_better_player_is_worth_more()
    {
        var weaker = PlayerValuation.Estimate(new PlayerValuationInput(10, 14, 25, 1));
        var stronger = PlayerValuation.Estimate(new PlayerValuationInput(16, 16, 25, 1));

        stronger.Should().BeGreaterThan(weaker, "ability drives the value");
    }

    [Fact]
    public void A_higher_ceiling_is_worth_more()
    {
        var lowCeiling = PlayerValuation.Estimate(new PlayerValuationInput(12, 12, 22, 1));
        var highCeiling = PlayerValuation.Estimate(new PlayerValuationInput(12, 19, 22, 1));

        highCeiling.Should().BeGreaterThan(lowCeiling, "the hidden potential is a valuation input (TRN-9)");
    }

    [Fact]
    public void A_veteran_is_worth_less_than_a_player_in_their_peak()
    {
        var peak = PlayerValuation.Estimate(new PlayerValuationInput(13, 14, 26, 1));
        var veteran = PlayerValuation.Estimate(new PlayerValuationInput(13, 14, 34, 1));

        veteran.Should().BeLessThan(peak);
    }

    [Fact]
    public void A_deeper_tier_values_the_same_player_lower()
    {
        var topTier = PlayerValuation.Estimate(new PlayerValuationInput(13, 14, 25, 1));
        var lowerTier = PlayerValuation.Estimate(new PlayerValuationInput(13, 14, 25, 3));

        lowerTier.Should().BeLessThan(topTier, "the wage scale the value derives from halves per tier");
    }

    [Fact]
    public void A_value_is_never_below_one_minor_unit()
    {
        var value = PlayerValuation.Estimate(new PlayerValuationInput(
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMin,
            Age: 60,
            Tier: 12));

        value.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void An_off_scale_ability_or_potential_is_refused()
    {
        var lowAbility = () => PlayerValuation.Estimate(
            new PlayerValuationInput(WorldRuleSet.AttributeMin - 1, 10, 25, 1));
        var highPotential = () => PlayerValuation.Estimate(
            new PlayerValuationInput(10, WorldRuleSet.AttributeMax + 1, 25, 1));
        var negativeAge = () => PlayerValuation.Estimate(new PlayerValuationInput(10, 10, -1, 1));
        var badTier = () => PlayerValuation.Estimate(new PlayerValuationInput(10, 10, 25, 0));

        lowAbility.Should().Throw<ArgumentOutOfRangeException>();
        highPotential.Should().Throw<ArgumentOutOfRangeException>();
        negativeAge.Should().Throw<ArgumentOutOfRangeException>();
        badTier.Should().Throw<ArgumentOutOfRangeException>();
    }
}
