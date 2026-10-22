using FluentAssertions;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.Market;

/// <summary>
/// The deterministic transfer valuation (`TRF-12`, `player-valuation-v2`).
/// </summary>
/// <remarks>
/// A pure function, so these tests need neither a clock nor a database. What they pin is the contract the
/// policy depends on: the same facts always produce the same value, the value rises with skill steeply and
/// smoothly, the market pays for youth and discounts age, nothing hidden is read, and no club's tier changes it.
/// </remarks>
public sealed class PlayerValuationTests
{
    [Fact]
    public void The_same_facts_value_a_player_the_same_way()
    {
        var input = new PlayerValuationInput(130, 24);

        PlayerValuation.Estimate(input).Should().Be(PlayerValuation.Estimate(input));
    }

    [Fact]
    public void The_value_grows_by_about_sixty_per_cent_for_each_point_of_skill_and_faster_above_seventeen()
    {
        long Value(int skill) => PlayerValuation.Estimate(PlayerValuationInput.FromAbility(skill, 26));

        ((double)Value(13) / Value(12)).Should().BeApproximately(1.6, 0.01);
        ((double)Value(15) / Value(14)).Should().BeApproximately(1.6, 0.01);
        ((double)Value(18) / Value(17)).Should().BeGreaterThan(1.65, "the curve is steeper above 17");
        ((double)Value(20) / Value(19)).Should().BeGreaterThan(1.85);
    }

    [Fact]
    public void The_anchors_of_the_curve_are_the_ones_the_rules_state()
    {
        PlayerValuation.Estimate(PlayerValuationInput.FromAbility(10, 26)).Should().Be(EconomyRuleSet.ValueAtSkillTenMinor);
        PlayerValuation.Estimate(PlayerValuationInput.FromAbility(19, 26)).Should().BeInRange(5_000_000_000, 7_000_000_000, "a 19-rated player at his peak costs about 58M");
        PlayerValuation.Estimate(PlayerValuationInput.FromAbility(20, 26)).Should().BeInRange(10_000_000_000, 12_000_000_000, "the best there is costs about 100M");
    }

    [Fact]
    public void A_value_moves_smoothly_as_a_player_trains()
    {
        var values = Enumerable.Range(100, 101).Select(tenths => PlayerValuation.Estimate(new PlayerValuationInput(tenths, 26))).ToList();

        values.Should().BeInAscendingOrder();
        values.Zip(values.Skip(1), (low, high) => (double)high / low).Should().OnlyContain(step => step < 1.07, "a tenth of a skill point moves the price by under 7%");
    }

    [Theory]
    [InlineData(19, 13_000)]
    [InlineData(21, 13_000)]
    [InlineData(22, 12_000)]
    [InlineData(23, 12_000)]
    [InlineData(24, 10_000)]
    [InlineData(28, 10_000)]
    [InlineData(29, 7_500)]
    [InlineData(30, 7_500)]
    [InlineData(31, 5_000)]
    [InlineData(33, 3_000)]
    public void The_age_factor_follows_the_table(int age, int factorBp)
    {
        EconomyRuleSet.ValueAgeFactorBp(age).Should().Be(factorBp);
    }

    [Fact]
    public void A_veteran_is_worth_less_than_a_player_in_his_peak()
    {
        var peak = PlayerValuation.Estimate(PlayerValuationInput.FromAbility(13, 26));
        var veteran = PlayerValuation.Estimate(PlayerValuationInput.FromAbility(13, 34));

        veteran.Should().BeLessThan(peak);
    }

    [Fact]
    public void The_market_adds_six_tenths_of_a_point_for_each_year_under_twenty_four_and_no_more_than_twenty()
    {
        EconomyRuleSet.PerceivedSkillTenths(100, 24).Should().Be(100);
        EconomyRuleSet.PerceivedSkillTenths(100, 20).Should().Be(124);
        EconomyRuleSet.PerceivedSkillTenths(100, 17).Should().Be(142);
        EconomyRuleSet.PerceivedSkillTenths(195, 18).Should().Be(200, "never above 20");
        EconomyRuleSet.PerceivedSkillTenths(100, 30).Should().Be(100, "an older player is not assumed to have grown");
    }

    [Fact]
    public void A_young_player_is_worth_more_than_the_same_skill_at_his_peak()
    {
        var young = PlayerValuation.Estimate(PlayerValuationInput.FromAbility(12, 19));
        var peak = PlayerValuation.Estimate(PlayerValuationInput.FromAbility(12, 26));

        young.Should().BeGreaterThan(peak);
    }

    [Fact]
    public void The_value_does_not_read_the_hidden_potential_or_the_clubs_tier()
    {
        // Neither is an input: the record has only the skill and the age.
        typeof(PlayerValuationInput).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(nameof(PlayerValuationInput.AbilityTenths), nameof(PlayerValuationInput.Age));
    }

    [Fact]
    public void A_value_is_never_below_the_minimum()
    {
        var value = PlayerValuation.Estimate(new PlayerValuationInput(WorldRuleSet.AttributeMin * 10, Age: 60));

        value.Should().BeGreaterThanOrEqualTo(EconomyRuleSet.MinimumPlayerValueMinor);
    }

    [Fact]
    public void An_off_scale_skill_or_age_is_refused()
    {
        var low = () => PlayerValuation.Estimate(new PlayerValuationInput((WorldRuleSet.AttributeMin * 10) - 1, 25));
        var high = () => PlayerValuation.Estimate(new PlayerValuationInput((WorldRuleSet.AttributeMax * 10) + 1, 25));
        var negativeAge = () => PlayerValuation.Estimate(new PlayerValuationInput(100, -1));

        low.Should().Throw<ArgumentOutOfRangeException>();
        high.Should().Throw<ArgumentOutOfRangeException>();
        negativeAge.Should().Throw<ArgumentOutOfRangeException>();
    }
}
