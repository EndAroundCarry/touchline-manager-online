using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// The versioned rule set (game rules §17). These tests are the reason a balancing change has to be a
/// deliberate edit rather than a silent constant tweak.
/// </summary>
public sealed class WorldRuleSetTests
{
    [Fact]
    public void The_settled_constants_match_the_rule_set()
    {
        WorldRuleSet.Version.Should().NotBeNullOrWhiteSpace();
        WorldRuleSet.ClubsPerDivision.Should().Be(18, "WORLD-4");
        WorldRuleSet.MatchdaysPerSeason.Should().Be(34, "CAL-1");
        WorldRuleSet.KickoffUtc.Should().Be(new TimeOnly(19, 0), "CAL-2");
        WorldRuleSet.TeamSheetLockMinutes.Should().Be(30, "CAL-3");
        WorldRuleSet.RolloverDays.Should().Be(7, "CAL-6");
        WorldRuleSet.PromotedPerTier.Should().Be(3, "PR-1");
        WorldRuleSet.RelegatedPerTier.Should().Be(3, "PR-1");
        WorldRuleSet.GeneratorSquadTarget.Should().Be(22, "SQ-1");
    }

    [Fact]
    public void Matchdays_fall_on_tuesday_thursday_and_sunday()
    {
        WorldRuleSet.KickoffWeekdays.Should().Equal(
            DayOfWeek.Tuesday,
            DayOfWeek.Thursday,
            DayOfWeek.Sunday);
    }

    [Fact]
    public void The_inactivity_ladder_is_ordered_warn_then_assist_then_close()
    {
        // OCC-1..OCC-3: the thresholds are also configuration, and this is what stops a later edit
        // from putting the warning after the closure.
        WorldRuleSet.InactivityWarningAfter.Should().Be(TimeSpan.FromDays(10));
        WorldRuleSet.InactivityAiAssistanceAfter.Should().Be(TimeSpan.FromDays(14));
        WorldRuleSet.InactivityCloseAfter.Should().Be(TimeSpan.FromDays(21));
        WorldRuleSet.InactivityWarningAfter.Should().BeLessThan(WorldRuleSet.InactivityAiAssistanceAfter);
        WorldRuleSet.InactivityAiAssistanceAfter.Should().BeLessThan(WorldRuleSet.InactivityCloseAfter);
        WorldRuleSet.ResignationCooldown.Should().Be(TimeSpan.FromDays(7), "OCC-4");
    }

    [Fact]
    public void The_launch_set_is_the_six_settled_countries_in_order()
    {
        // WORLD-2
        LaunchCountries.All.Should().HaveCount(6);
        LaunchCountries.All.Select(country => country.Code)
            .Should().Equal("ENG", "ESP", "GER", "ITA", "FRA", "ROU");
        LaunchCountries.All.Select(country => country.SortOrder)
            .Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void Every_launch_country_carries_a_distinct_name_pool_and_a_locale()
    {
        LaunchCountries.All.Select(country => country.NamePoolKey).Should().OnlyHaveUniqueItems();
        LaunchCountries.All.Select(country => country.Code).Should().OnlyHaveUniqueItems();
        LaunchCountries.All.Should().OnlyContain(country => country.Locale.Length == 5);
    }

    [Fact]
    public void Each_tier_below_the_first_is_worth_half_of_the_one_above_it()
    {
        WorldRuleSet.TierScalingFactor(1).Should().Be(1);
        WorldRuleSet.TierScalingFactor(2).Should().Be(2);
        WorldRuleSet.TierScalingFactor(3).Should().Be(4);

        WorldRuleSet.OpeningCashMinorForTier(1).Should().BeGreaterThan(WorldRuleSet.OpeningCashMinorForTier(2));
        WorldRuleSet.OpeningCashMinorForTier(2).Should().BeGreaterThan(WorldRuleSet.OpeningCashMinorForTier(3));
        WorldRuleSet.OpeningStadiumBaselineForTier(4).Should().BeLessThan(
            WorldRuleSet.OpeningStadiumBaselineForTier(1));
        WorldRuleSet.OpeningReputationForTier(6).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void A_tier_below_one_is_a_programming_error()
    {
        var act = () => WorldRuleSet.TierScalingFactor(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
