using FluentAssertions;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.World;

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
        WorldRuleSet.ClubsPerDivision.Should().Be(16, "WORLD-4");
        WorldRuleSet.MatchdaysPerSeason.Should().Be(30, "CAL-1");
        WorldRuleSet.MatchdaysPerSeason.Should().Be((WorldRuleSet.ClubsPerDivision - 1) * 2, "CAL-1");
        WorldRuleSet.SeasonCycleWeeks.Should().Be(11, "CAL-6");
        WorldRuleSet.LargestDivisionEver.Should().BeGreaterThanOrEqualTo(WorldRuleSet.ClubsPerDivision);
        WorldRuleSet.LongestSeasonEver.Should().BeGreaterThanOrEqualTo(WorldRuleSet.MatchdaysPerSeason);
        WorldRuleSet.KickoffUtc.Should().Be(new TimeOnly(19, 0), "CAL-2");
        WorldRuleSet.TeamSheetLockMinutes.Should().Be(30, "CAL-3");
        WorldRuleSet.RolloverDays.Should().Be(7, "CAL-6");
        WorldRuleSet.PromotedPerTier.Should().Be(3, "PR-1");
        WorldRuleSet.RelegatedPerTier.Should().Be(3, "PR-1");
        WorldRuleSet.GeneratorSquadTarget.Should().Be(24, "SQ-1");
    }

    [Fact]
    public void The_generated_squad_composition_sums_to_the_squad_target()
    {
        // SQ-1: the four positional quotas are the target split, so they must never drift apart from it.
        (WorldRuleSet.GeneratedGoalkeepers
            + WorldRuleSet.GeneratedDefenders
            + WorldRuleSet.GeneratedMidfielders
            + WorldRuleSet.GeneratedAttackers).Should().Be(WorldRuleSet.GeneratorSquadTarget);

        WorldRuleSet.GeneratedGoalkeepers.Should()
            .BeGreaterThanOrEqualTo(WorldRuleSet.MinimumGoalkeepers, "SQ-2");
    }

    [Fact]
    public void The_squad_and_attribute_bounds_match_the_rule_set()
    {
        // SQ-2, SQ-3, SQ-4, TRN-4
        WorldRuleSet.SquadMinimumRegistered.Should().Be(18);
        WorldRuleSet.MinimumGoalkeepers.Should().Be(2);
        WorldRuleSet.SquadMaximumRegistered.Should().Be(27);
        WorldRuleSet.SquadMinimumRegistered.Should().BeLessThan(WorldRuleSet.SquadMaximumRegistered);
        WorldRuleSet.GeneratorSquadTarget.Should().BeInRange(
            WorldRuleSet.SquadMinimumRegistered,
            WorldRuleSet.SquadMaximumRegistered);
        WorldRuleSet.TeamSheetStarters.Should().Be(11);
        WorldRuleSet.TeamSheetSubstitutes.Should().Be(7);
        WorldRuleSet.AttributeMin.Should().Be(1);
        WorldRuleSet.AttributeMax.Should().Be(20);
        WorldRuleSet.StateBasisPointsMin.Should().Be(0);
        WorldRuleSet.StateBasisPointsMax.Should().Be(10_000, "TRN-5..TRN-7");
    }

    [Fact]
    public void The_player_state_and_slot_coordinate_scales_are_the_documented_ones()
    {
        WorldRuleSet.SlotCoordinateMin.Should().Be(0, "TAC-9");
        WorldRuleSet.SlotCoordinateMax.Should().Be(10_000, "TAC-9");
        WorldRuleSet.ContractMinSeasons.Should().Be(1, "CON-1");
        WorldRuleSet.ContractMaxSeasons.Should().Be(3, "CON-1");
        WorldRuleSet.PlayerMinimumAge.Should().BeLessThan(WorldRuleSet.PlayerMaximumAge);
    }

    [Fact]
    public void Squad_ability_falls_with_depth_without_collapsing_to_the_floor()
    {
        WorldRuleSet.GeneratedAbilityMeanForTier(1).Should().Be(WorldRuleSet.GeneratedAbilityMeanTier1);
        WorldRuleSet.GeneratedAbilityMeanForTier(2).Should()
            .BeLessThan(WorldRuleSet.GeneratedAbilityMeanForTier(1));
        WorldRuleSet.GeneratedAbilityMeanForTier(6).Should()
            .BeGreaterThanOrEqualTo(WorldRuleSet.AttributeMin);
    }

    [Fact]
    public void A_tier_below_one_is_a_programming_error_for_squad_ability_too()
    {
        var act = () => WorldRuleSet.GeneratedAbilityMeanForTier(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Youth_potential_falls_with_depth_on_its_own_scale()
    {
        WorldRuleSet.YouthPotentialMeanForTier(1).Should().Be(WorldRuleSet.YouthPotentialMeanTier1);
        WorldRuleSet.YouthPotentialMeanForTier(2).Should()
            .Be(WorldRuleSet.YouthPotentialMeanTier1 - WorldRuleSet.YouthPotentialDropPerTier);
        WorldRuleSet.YouthPotentialMeanForTier(20).Should().Be(WorldRuleSet.AttributeMin);
    }

    [Fact]
    public void A_tier_below_one_is_a_programming_error_for_youth_potential_too()
    {
        var act = () => WorldRuleSet.YouthPotentialMeanForTier(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
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
    public void Each_tier_below_the_first_has_less_money_and_a_lower_opening_standing()
    {
        WorldRuleSet.OpeningCashMinorForTier(1).Should().BeGreaterThan(WorldRuleSet.OpeningCashMinorForTier(2));
        WorldRuleSet.OpeningCashMinorForTier(2).Should().BeGreaterThan(WorldRuleSet.OpeningCashMinorForTier(3));
        WorldRuleSet.OpeningReputationForTier(1).Should().Be(50);
        WorldRuleSet.OpeningReputationForTier(2).Should().Be(40);
        WorldRuleSet.OpeningReputationForTier(6).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void A_tier_below_one_is_a_programming_error()
    {
        var act = () => EconomyRuleSet.TierFactorBp(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(2026, 10, 16, 12, 2026, 10, 18)] // Friday: the coming Sunday.
    [InlineData(2026, 10, 17, 19, 2026, 10, 18)] // Saturday evening.
    [InlineData(2026, 10, 18, 0, 2026, 10, 18)] // Sunday morning: that Sunday's own run.
    [InlineData(2026, 10, 18, 22, 2026, 10, 18)] // Sunday before the 23:00 boundary.
    [InlineData(2026, 10, 18, 23, 2026, 10, 25)] // Sunday at the boundary: the run has already taken that week.
    [InlineData(2026, 10, 19, 1, 2026, 10, 25)] // Monday: the next Sunday.
    public void A_weekly_finance_date_is_the_sunday_boundary_that_settles_the_instant(
        int year, int month, int day, int hour, int weekYear, int weekMonth, int weekDay)
    {
        var instant = new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero);

        WorldRuleSet.WeeklyFinanceDateFor(instant).Should().Be(new DateOnly(weekYear, weekMonth, weekDay));
    }

    [Fact]
    public void The_weekly_run_is_anchored_to_sunday_night_and_payroll_risk_is_measured_in_weeks()
    {
        WorldRuleSet.WeeklyFinanceUtc.Should().Be(new TimeOnly(23, 0), "CON-2 settles the week after Sunday");

        WorldRuleSet.PayrollRiskWeeks.Should().BeGreaterThan(0, "a club holds weeks of wages before payroll is a risk (FIN-16)");
    }
}
