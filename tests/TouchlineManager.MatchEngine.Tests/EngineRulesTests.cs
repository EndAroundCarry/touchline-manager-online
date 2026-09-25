using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The rules set's own guarantees: it validates, and its hash covers every constant it has.
/// </summary>
public sealed class EngineRulesTests
{
    [Fact]
    public void The_default_rules_are_valid()
    {
        var act = EngineRulesV1.Default.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void The_hash_covers_every_constant()
    {
        // A constant that is not in the hash means two genuinely different configurations can claim the same
        // provenance. The canonical description is built by reflection for exactly this reason, so this test is
        // what keeps that honest when somebody adds a field.
        var described = EngineRulesV1.Default.ToCanonicalParts();

        described.Should().Contain(part => part.StartsWith("BaseShotGoalBasisPoints=", StringComparison.Ordinal));
        described.Should().Contain(part => part.StartsWith("HomeAdvantageBasisPoints=", StringComparison.Ordinal));
        described.Should().Contain(part => part.StartsWith("SubstitutionWindows=", StringComparison.Ordinal));
        described[0].Should().Be(EngineRulesV1.Version);

        described.Count.Should().Be(
            EngineRulesV1.Default.GetType().GetProperties().Length + 1,
            "every property is described, plus the version label");
    }

    [Fact]
    public void Changing_a_constant_changes_the_hash()
    {
        var changed = EngineRulesV1.Default with { BaseShotGoalBasisPoints = 999 };

        EngineConfiguration.HashOf(changed).Should().NotBe(EngineConfiguration.HashOf(EngineRulesV1.Default));
    }

    [Fact]
    public void The_hash_is_stable_for_equal_configurations()
    {
        // Built twice rather than reused, so a hash that depended on instance identity would fail here.
        var first = new EngineRulesV1();
        var second = new EngineRulesV1();

        EngineConfiguration.HashOf(first).Should().Be(EngineConfiguration.HashOf(second));
    }

    [Fact]
    public void A_probability_beyond_certainty_is_refused()
    {
        var rules = EngineRulesV1.Default with { BaseFoulBasisPoints = 10_001 };

        var act = rules.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*BaseFoulBasisPoints*");
    }

    [Fact]
    public void A_multiplier_is_not_judged_as_a_probability()
    {
        // Home advantage is above certainty by design — it multiplies ratings rather than rolling against them.
        // A validator that conflated the two would refuse the shipped rules, which is how this was found.
        var rules = EngineRulesV1.Default with { HomeAdvantageBasisPoints = 10_300 };

        var act = rules.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void A_multiplier_outside_its_band_is_refused()
    {
        var rules = EngineRulesV1.Default with { HomeAdvantageBasisPoints = 900 };

        var act = rules.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*HomeAdvantageBasisPoints*");
    }

    [Fact]
    public void A_subtractive_penalty_is_not_judged_as_a_multiplier()
    {
        var rules = EngineRulesV1.Default with { OutOfPositionCohesionPenaltyBasisPoints = 1_400 };

        var act = rules.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void Inverted_bounds_are_refused()
    {
        var rules = EngineRulesV1.Default with { MinProgressBasisPoints = 9_000, MaxProgressBasisPoints = 1_000 };

        var act = rules.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MinProgressBasisPoints*");
    }

    [Fact]
    public void A_substitution_window_outside_the_match_is_refused()
    {
        var rules = EngineRulesV1.Default with { SubstitutionWindows = [46, 120] };

        var act = rules.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*Substitution window 120*");
    }

    [Fact]
    public void A_rating_scale_that_cannot_hold_a_maximum_player_is_refused()
    {
        var rules = EngineRulesV1.Default with { MaxUnitRating = 500 };

        var act = rules.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxUnitRating*");
    }

    [Fact]
    public void The_engine_and_rules_versions_are_labelled()
    {
        EngineVersions.EngineLabel.Should().Be("engine-v1");
        EngineVersions.RuleSetLabel.Should().Be("engine-rules-v1");
        EngineVersions.Engine.Should().Be(1);
        EngineVersions.RuleSet.Should().Be(1);
    }
}
