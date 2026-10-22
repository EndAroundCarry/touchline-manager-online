using FluentAssertions;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.Finance;

/// <summary>
/// The economy rules of world-rules-v18: the tier factors, the league money, the prize table, wages, the budget and the clean start (plan toasty-scott).
/// </summary>
public sealed class EconomyRuleSetTests
{
    [Theory]
    [InlineData(1, 10_000)]
    [InlineData(2, 4_000)]
    [InlineData(3, 1_600)]
    [InlineData(4, 640)]
    [InlineData(5, 576)]
    [InlineData(6, 576)]
    [InlineData(65, 576)]
    [InlineData(100, 576)]
    public void The_tier_factor_falls_four_tenths_a_tier_and_tier_five_and_below_are_ninety_per_cent_of_tier_four(int tier, int factorBp)
    {
        EconomyRuleSet.TierFactorBp(tier).Should().Be(factorBp);
    }

    [Theory]
    [InlineData(1, 10_000)]
    [InlineData(2, 5_333)]
    [InlineData(3, 2_844)]
    [InlineData(4, 1_517)]
    [InlineData(5, 1_365)]
    [InlineData(65, 1_365)]
    [InlineData(100, 1_365)]
    public void The_price_level_makes_the_gate_fall_as_fast_as_the_league_money(int tier, int levelBp)
    {
        EconomyRuleSet.PriceLevelBp(tier).Should().Be(levelBp);
    }

    [Fact]
    public void A_tier_below_the_first_is_the_only_thing_that_is_refused()
    {
        FluentActions.Invoking(() => EconomyRuleSet.TierFactorBp(0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => EconomyRuleSet.PriceLevelBp(0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_deep_tier_does_not_wrap_round_to_the_money_of_the_first()
    {
        // The old divisor was a shift that C# masks to six bits, so a tier-65 club would have been paid like a tier-1 club.
        EconomyRuleSet.LeagueTvSeasonMinorFor(65).Should().BeLessThan(EconomyRuleSet.LeagueTvSeasonMinorFor(4));
        EconomyRuleSet.OpeningCashMinorFor(65).Should().Be(EconomyRuleSet.OpeningCashMinorFor(5));
        EconomyRuleSet.PrizeMinorFor(65, 1).Should().Be(EconomyRuleSet.PrizeMinorFor(5, 1));
    }

    [Fact]
    public void The_prize_is_a_straight_line_from_the_last_place_to_the_third_with_bigger_steps_for_the_top_three()
    {
        var share = EconomyRuleSet.PrizeShareMinorTier1;

        var last = EconomyRuleSet.ClubsPerDivision;

        EconomyRuleSet.PrizeMinorFor(1, last).Should().Be(share);

        for (var place = 3; place <= last; place++)
        {
            EconomyRuleSet.PrizeMinorFor(1, place).Should().Be((last + 1 - place) * share, $"place {place}");
        }

        EconomyRuleSet.PrizeMinorFor(1, 2).Should().Be(EconomyRuleSet.PrizeMinorFor(1, 3) * 115 / 100, "the second place gets 15% more than the third");
        EconomyRuleSet.PrizeMinorFor(1, 1).Should().Be(EconomyRuleSet.PrizeMinorFor(1, 2) * 120 / 100, "the champion gets 20% more than the second");
        EconomyRuleSet.PrizeTable(1).Should().BeInDescendingOrder();
    }

    [Fact]
    public void The_prize_of_a_tier_is_the_tier_one_prize_scaled_by_the_tier_factor()
    {
        for (var place = 1; place <= EconomyRuleSet.ClubsPerDivision; place++)
        {
            EconomyRuleSet.PrizeMinorFor(2, place).Should().BeInRange(
                EconomyRuleSet.PrizeMinorFor(1, place) * 4 / 10 - 1,
                EconomyRuleSet.PrizeMinorFor(1, place) * 4 / 10 + 1);
            EconomyRuleSet.PrizeMinorFor(5, place).Should().BeInRange(
                EconomyRuleSet.PrizeMinorFor(4, place) * 9 / 10 - 2,
                EconomyRuleSet.PrizeMinorFor(4, place) * 9 / 10 + 2);
        }
    }

    [Fact]
    public void A_division_pays_out_the_shares_the_rules_state()
    {
        // 14 + 13 + ... + 1 shares for the places 3 to 16, then 16.1 and 19.32: 140.42 shares.
        var shares = EconomyRuleSet.PrizeTable(1).Sum() / (double)EconomyRuleSet.PrizeShareMinorTier1;

        shares.Should().BeApproximately(140.42, 0.001);
    }

    [Fact]
    public void The_first_tier_pays_the_figures_the_plan_states_for_a_division_of_sixteen()
    {
        var table = EconomyRuleSet.PrizeTable(1);

        table[0].Should().Be(656_880_000, "the champion");
        table[1].Should().Be(547_400_000, "the second place");
        table[2].Should().Be(476_000_000, "the third place");
        table[^1].Should().Be(34_000_000, "the last place gets one share");
        (table.Sum() / table.Count).Should().BeInRange(290_000_000, 305_000_000, "the average club keeps the prize it had on 18 clubs at 300k a share");
    }

    [Fact]
    public void A_place_outside_the_division_is_refused_for_a_prize()
    {
        FluentActions.Invoking(() => EconomyRuleSet.PrizeMinorFor(1, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => EconomyRuleSet.PrizeMinorFor(1, EconomyRuleSet.ClubsPerDivision + 1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_season_amount_is_paid_in_thirty_equal_parts()
    {
        EconomyRuleSet.PerMatchday(3_000_000).Should().Be(100_000);
        (EconomyRuleSet.PerMatchday(EconomyRuleSet.LeagueTvSeasonMinorTier1) * 30).Should().BeInRange(EconomyRuleSet.LeagueTvSeasonMinorTier1 - 30, EconomyRuleSet.LeagueTvSeasonMinorTier1);
        EconomyRuleSet.WeeklyRunsPerSeason.Should().Be(11, "one run for each week of the season cycle");
        EconomyRuleSet.PerWeeklyRun(3_300_000).Should().Be(300_000, "a weekly run settles an eleventh of the season");
    }

    [Fact]
    public void The_parachute_is_half_the_television_money_a_relegated_club_loses()
    {
        var lost = EconomyRuleSet.LeagueTvSeasonMinorFor(1) - EconomyRuleSet.LeagueTvSeasonMinorFor(2);

        EconomyRuleSet.ParachuteMinorFor(1).Should().Be(lost / 2);
        EconomyRuleSet.ParachuteMinorFor(40).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void The_relegation_clause_takes_off_a_part_of_a_wage_but_never_takes_it_under_the_floor()
    {
        EconomyRuleSet.AfterRelegationClause(1_000_000_000L).Should().Be(1_000_000_000L * EconomyRuleSet.RelegationKeepBp / 10_000);
        EconomyRuleSet.AfterRelegationClause(EconomyRuleSet.SeasonWageFloorMinor).Should().Be(EconomyRuleSet.SeasonWageFloorMinor);
        EconomyRuleSet.AfterRelegationClause(EconomyRuleSet.SeasonWageFloorMinor + 1).Should().BeGreaterThanOrEqualTo(EconomyRuleSet.SeasonWageFloorMinor);
    }

    [Fact]
    public void A_wage_follows_the_player_rises_with_skill_and_never_falls_under_the_floor()
    {
        var wages = Enumerable.Range(1, 20).Select(ability => EconomyRuleSet.GeneratedSeasonWageMinorFor(ability)).ToList();

        wages.Should().BeInAscendingOrder();
        wages.Should().OnlyContain(wage => wage >= EconomyRuleSet.SeasonWageFloorMinor);
        wages[0].Should().Be(EconomyRuleSet.SeasonWageFloorMinor, "a poor player earns the floor");
        wages[^1].Should().BeGreaterThan(wages[9] * 10, "the best player is paid many times a mid-table one");
    }

    [Fact]
    public void A_wage_above_the_floor_is_a_share_of_the_value_the_player_would_have_at_his_peak()
    {
        var skill = 150;
        var value = EconomyRuleSet.ValueAtPeakMinor(skill);

        EconomyRuleSet.SeasonWageMinorForSkill(skill).Should().Be(value * EconomyRuleSet.WageShareOfValueBp / 10_000);
    }

    [Fact]
    public void A_wage_reads_two_parts_what_a_player_is_and_one_part_what_the_market_takes_him_to_be()
    {
        EconomyRuleSet.WageSkillTenths(100, 28).Should().Be(100, "a player past 24 is what he is");
        EconomyRuleSet.WageSkillTenths(100, 21).Should().Be((200 + 118) / 3);
        EconomyRuleSet.WageSkillTenths(100, 21).Should().BeGreaterThan(EconomyRuleSet.WageSkillTenths(100, 28));
    }

    [Fact]
    public void The_forecast_is_the_leagues_money_the_sponsor_and_the_gate_and_buildings_of_the_home_matches()
    {
        foreach (var tier in new[] { 1, 2, 3, 4, 5 })
        {
            var forecast = EconomyRuleSet.FreshClubForecastMinor(tier);

            forecast.Should().BeGreaterThan(EconomyRuleSet.LeagueTvSeasonMinorFor(tier) + EconomyRuleSet.SponsorBaseSeasonMinorFor(tier));
        }

        EconomyRuleSet.FreshClubForecastMinor(2).Should().BeLessThan(EconomyRuleSet.FreshClubForecastMinor(1));
    }

    [Fact]
    public void The_wage_budget_is_seventy_per_cent_of_the_forecast_and_the_overdraft_a_quarter()
    {
        EconomyRuleSet.WageBudgetMinorFor(1_000_000_000).Should().Be(700_000_000);
        EconomyRuleSet.OverdraftLimitMinorFor(1_000_000_000).Should().Be(250_000_000);
        EconomyRuleSet.WageBudgetMinorFor(-5).Should().Be(0);
        EconomyRuleSet.SafetyNetFloorMinor(250_000_000).Should().Be(-500_000_000);
    }

    [Fact]
    public void A_fresh_club_s_wage_bill_target_is_sixty_per_cent_of_its_forecast_and_the_bands_say_what_is_close_enough()
    {
        var target = EconomyRuleSet.CleanStartWageBillMinorFor(1);

        target.Should().Be(EconomyRuleSet.FreshClubForecastMinor(1) * 6_000 / 10_000);
        EconomyRuleSet.WageBandDistanceMinor(1, target, EconomyRuleSet.CleanStartWageBandLowBp, EconomyRuleSet.CleanStartWageBandHighBp).Should().Be(0);
        EconomyRuleSet.WageBandDistanceMinor(1, target * 2, EconomyRuleSet.CleanStartWageBandLowBp, EconomyRuleSet.CleanStartWageBandHighBp).Should().BePositive();
        EconomyRuleSet.WageBandDistanceMinor(1, target / 2, EconomyRuleSet.CleanStartWageBandLowBp, EconomyRuleSet.CleanStartWageBandHighBp).Should().BePositive();
    }

    [Fact]
    public void Opening_cash_is_about_forty_per_cent_of_the_first_seasons_income_in_every_tier()
    {
        foreach (var tier in new[] { 1, 2, 3, 4 })
        {
            var share = (double)EconomyRuleSet.OpeningCashMinorFor(tier) / EconomyRuleSet.FreshClubForecastMinor(tier);

            share.Should().BeInRange(0.3, 0.6, $"tier {tier}");
        }
    }

    [Fact]
    public void Season_one_income_is_in_proportion_in_every_tier()
    {
        // Every tier's income has the same make-up, so the league money is the same share of it.
        var shares = new[] { 1, 2, 3, 4 }
            .Select(tier => (double)EconomyRuleSet.LeagueTvSeasonMinorFor(tier) / EconomyRuleSet.FreshClubForecastMinor(tier))
            .ToList();

        shares.Max().Should().BeLessThan(shares.Min() * 1.3);
    }

    [Fact]
    public void Standing_codes_are_stable()
    {
        SponsorOfferKind.Steady.ToCode().Should().Be("steady");
        SeasonObjective.WinLeague.ToCode().Should().Be("win_league");
        ObjectiveOutcome.Exceeded.ToCode().Should().Be("exceeded");
    }
}
