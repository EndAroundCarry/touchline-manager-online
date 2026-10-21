using FluentAssertions;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Tests.World;

/// <summary>
/// The snack bar's and the merchandise shop's tables, bonuses and takings (`STRC-1`…`STRC-9`).
/// </summary>
public sealed class StructureRuleSetTests
{
    private static readonly StadiumSeats OpeningGround = new(3_000, 1_000, 900, 100);

    // Matchdays are 3 a week and a season is 34 of them, 17 at home: a club hosts 1.5 matches a week.
    private const int HomeMatchesPerSeason = 17;

    private const int MatchdaysPerSeason = 34;

    private const int MatchdaysPerWeek = 3;

    public static TheoryData<int> Levels => new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_snack_bar_spend_starts_at_one_and_rises_by_a_quarter_a_level(int level)
    {
        StructureRuleSet.SnackSpendMinor(level).Should().Be(100 + ((level - 1) * 25));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_merchandise_spend_starts_at_thirty_and_rises_by_ten_a_level(int level)
    {
        StructureRuleSet.MerchSpendMinor(level).Should().Be(30 + ((level - 1) * 10));
    }

    [Theory]
    [InlineData(1, 30_000)]
    [InlineData(2, 45_000)]
    [InlineData(3, 60_000)]
    [InlineData(4, 80_000)]
    [InlineData(5, 100_000)]
    [InlineData(6, 125_000)]
    [InlineData(7, 150_000)]
    [InlineData(8, 180_000)]
    [InlineData(9, 215_000)]
    [InlineData(10, 250_000)]
    public void The_upkeep_ladder_matches_the_table(int level, long upkeepMinor)
    {
        StructureRuleSet.UpkeepMinorTier1(level).Should().Be(upkeepMinor);
    }

    [Theory]
    [InlineData(2, 2_500_000, 1)]
    [InlineData(3, 3_500_000, 1)]
    [InlineData(4, 5_000_000, 2)]
    [InlineData(5, 7_000_000, 2)]
    [InlineData(6, 9_500_000, 3)]
    [InlineData(7, 13_000_000, 4)]
    [InlineData(8, 17_500_000, 5)]
    [InlineData(9, 23_500_000, 6)]
    [InlineData(10, 31_500_000, 7)]
    public void The_cost_and_build_time_of_each_upgrade_match_the_table(int target, long costMinor, int days)
    {
        StructureRuleSet.UpgradeCostMinorTier1(target).Should().Be(costMinor);
        StructureRuleSet.BuildDaysFor(target).Should().Be(days);
    }

    [Fact]
    public void Every_table_rises_with_the_level_so_a_better_building_never_costs_or_earns_less()
    {
        for (var level = 2; level <= StructureRuleSet.MaxLevel; level++)
        {
            StructureRuleSet.SnackSpendMinor(level).Should().BeGreaterThan(StructureRuleSet.SnackSpendMinor(level - 1));
            StructureRuleSet.MerchSpendMinor(level).Should().BeGreaterThan(StructureRuleSet.MerchSpendMinor(level - 1));
            StructureRuleSet.UpkeepMinorTier1(level).Should().BeGreaterThan(StructureRuleSet.UpkeepMinorTier1(level - 1));
            StructureRuleSet.UpgradeCostMinorTier1(level).Should().BeGreaterThan(level == 2 ? 0 : StructureRuleSet.UpgradeCostMinorTier1(level - 1));
            StructureRuleSet.BuildDaysFor(level).Should().BeGreaterThanOrEqualTo(level == 2 ? 1 : StructureRuleSet.BuildDaysFor(level - 1));
        }
    }

    [Fact]
    public void A_level_outside_the_ten_levels_is_a_programming_error()
    {
        var tooLow = () => StructureRuleSet.SnackSpendMinor(0);
        var tooHigh = () => StructureRuleSet.UpkeepMinorTier1(11);
        var freeUpgrade = () => StructureRuleSet.UpgradeCostMinorTier1(1);
        var pastTheTop = () => StructureRuleSet.BuildDaysFor(11);

        tooLow.Should().Throw<ArgumentOutOfRangeException>();
        tooHigh.Should().Throw<ArgumentOutOfRangeException>();
        freeUpgrade.Should().Throw<ArgumentOutOfRangeException>("level 1 is opened, never bought");
        pastTheTop.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Upkeep_and_upgrade_costs_are_divided_by_the_tier_scaling_factor_and_never_round_to_nothing()
    {
        StructureRuleSet.UpkeepMinorFor(4, 1).Should().Be(80_000);
        StructureRuleSet.UpkeepMinorFor(4, 2).Should().Be(40_000);
        StructureRuleSet.UpgradeCostMinorFor(6, 4).Should().Be(9_500_000 / 8);

        for (var level = 1; level <= StructureRuleSet.MaxLevel; level++)
        {
            StructureRuleSet.UpkeepMinorFor(level, 20).Should().BeGreaterThan(0);
        }

        StructureRuleSet.UpgradeCostMinorFor(2, 20).Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(2, 0, 50, 3_000, "underdog_win")]
    [InlineData(1, 0, 99, 3_000, "underdog_win")]
    [InlineData(1, 0, 100, 1_500, "even_win")]
    [InlineData(3, 2, 130, 1_500, "even_win")]
    [InlineData(1, 0, 159, 1_500, "even_win")]
    [InlineData(1, 0, 160, 500, "favourite_win")]
    [InlineData(4, 0, 210, 500, "favourite_win")]
    [InlineData(1, 1, 50, 1_000, "underdog_draw")]
    [InlineData(0, 0, 99, 1_000, "underdog_draw")]
    [InlineData(0, 0, 100, 0, "none")]
    [InlineData(2, 2, 180, 0, "none")]
    [InlineData(0, 1, 50, 0, "none")]
    [InlineData(0, 3, 210, 0, "none")]
    [InlineData(1, 2, 130, 0, "none")]
    public void The_result_bonus_follows_the_result_and_what_the_fans_expected(
        int goalsFor, int goalsAgainst, int expectedCentis, int bonusBp, string reason)
    {
        var bonus = StructureRuleSet.ResultBonusFor(goalsFor, goalsAgainst, expectedCentis);

        bonus.Bp.Should().Be(bonusBp);
        bonus.Reason.Should().Be(reason);
    }

    [Fact]
    public void No_result_lifts_the_snack_bar_past_thirty_percent()
    {
        var best = 0;

        for (var expected = 0; expected <= 300; expected++)
        {
            for (var goalsFor = 0; goalsFor <= 4; goalsFor++)
            {
                for (var goalsAgainst = 0; goalsAgainst <= 4; goalsAgainst++)
                {
                    best = Math.Max(best, StructureRuleSet.ResultBonusFor(goalsFor, goalsAgainst, expected).Bp);
                }
            }
        }

        best.Should().Be(StructureRuleSet.MaxResultBonusBp).And.Be(3_000);
    }

    [Theory]
    [InlineData(FanMoodWord.Furious, 0)]
    [InlineData(FanMoodWord.Angry, 0)]
    [InlineData(FanMoodWord.Unhappy, 200)]
    [InlineData(FanMoodWord.Calm, 500)]
    [InlineData(FanMoodWord.Content, 1_000)]
    [InlineData(FanMoodWord.Delighted, 1_800)]
    [InlineData(FanMoodWord.Euphoric, 2_500)]
    public void The_mood_bonus_matches_the_table_and_carries_the_mood_word_as_its_reason(FanMoodWord word, int bonusBp)
    {
        var bonus = StructureRuleSet.MoodBonusFor(word);

        bonus.Bp.Should().Be(bonusBp);
        bonus.Reason.Should().Be(FanMoods.Code(word));
        StructureRuleSet.MoodBonusBpFor(word).Should().Be(bonusBp);
    }

    [Fact]
    public void A_happier_crowd_never_buys_less_merchandise()
    {
        var words = Enum.GetValues<FanMoodWord>().OrderBy(word => (int)word).ToList();

        for (var index = 1; index < words.Count; index++)
        {
            StructureRuleSet.MoodBonusBpFor(words[index]).Should().BeGreaterThanOrEqualTo(StructureRuleSet.MoodBonusBpFor(words[index - 1]));
        }
    }

    [Fact]
    public void Every_mood_band_the_fans_can_have_has_a_bonus()
    {
        for (var moodBp = 0; moodBp <= FanRuleSet.MoodMaxBp; moodBp += 250)
        {
            var act = () => StructureRuleSet.MoodBonusFor(FanMoods.WordFor(moodBp));

            act.Should().NotThrow();
        }
    }

    [Fact]
    public void The_snack_bar_takes_the_crowd_times_the_spend_lifted_by_the_result()
    {
        // 2,890 people at level 1 (1.00 each), then lifted by an underdog win.
        StructureRuleSet.SnackBaseMinorFor(1, 2_890).Should().Be(289_000);
        StructureRuleSet.SnackTakingsMinorFor(1, 2_890, 0).Should().Be(289_000);
        StructureRuleSet.SnackTakingsMinorFor(1, 2_890, 3_000).Should().Be(375_700);
        StructureRuleSet.SnackTakingsMinorFor(10, 2_890, 1_500).Should().Be(2_890L * 325 * 115 / 100);
        StructureRuleSet.SnackTakingsMinorFor(1, 0, 3_000).Should().Be(0, "an empty ground sells nothing");
    }

    [Fact]
    public void The_merchandise_shop_takes_the_fans_times_the_spend_lifted_by_their_mood()
    {
        // 10,000 calm fans at level 1 (0.30 each) is 3,000.00 before the 5% mood bonus.
        StructureRuleSet.MerchBaseMinorFor(1, 10_000).Should().Be(300_000);
        StructureRuleSet.MerchTakingsMinorFor(1, 10_000, FanMoodWord.Calm).Should().Be(315_000);
        StructureRuleSet.MerchTakingsMinorFor(1, 10_000, FanMoodWord.Furious).Should().Be(300_000);
        StructureRuleSet.MerchTakingsMinorFor(1, 10_000, FanMoodWord.Euphoric).Should().Be(375_000);
        StructureRuleSet.MerchTakingsMinorFor(10, 10_000, FanMoodWord.Euphoric).Should().Be(1_500_000);
    }

    [Fact]
    public void A_bonus_rounds_down_to_a_whole_minor_unit()
    {
        StructureRuleSet.WithBonus(7, 1_500).Should().Be(8, "7 x 1.15 is 8.05");
        StructureRuleSet.WithBonus(1, 3_000).Should().Be(1, "1.3 rounds down");
        StructureRuleSet.WithBonus(0, 3_000).Should().Be(0);
    }

    [Fact]
    public void Negative_customers_are_a_programming_error()
    {
        var spectators = () => StructureRuleSet.SnackBaseMinorFor(1, -1);
        var fans = () => StructureRuleSet.MerchBaseMinorFor(1, -1);

        spectators.Should().Throw<ArgumentOutOfRangeException>();
        fans.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_tier_one_club_at_its_opening_numbers_matches_the_plans_scale_check()
    {
        var crowd = StadiumRuleSet.AttendanceFor(OpeningGround, FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp);

        crowd.Should().Be(2_890, "the plan's 'about 2,900'");

        var snackPerWeek = SnackWeeklyTakingsMinor(1, crowd);
        var merchPerWeek = MerchWeeklyTakingsMinor(1, FanRuleSet.StartFansFor(1), FanMoodWord.Calm);

        snackPerWeek.Should().BeInRange(430_000, 440_000, "about 4,350.00 a week at level 1");
        merchPerWeek.Should().Be(315_000, "3,150.00 a week for calm fans at level 1");
    }

    [Fact]
    public void The_first_upgrade_of_either_building_repays_in_about_two_and_a_half_seasons_at_tier_one()
    {
        var fans = FanRuleSet.StartFansFor(1);
        var crowd = StadiumRuleSet.AttendanceFor(OpeningGround, fans, FanRuleSet.CalmMoodBp);

        var snackGain = SnackWeeklyProfitMinor(2, 1, crowd) - SnackWeeklyProfitMinor(1, 1, crowd);
        var merchGain = MerchWeeklyProfitMinor(2, 1, fans) - MerchWeeklyProfitMinor(1, 1, fans);
        var cost = StructureRuleSet.UpgradeCostMinorFor(2, 1);

        var snackWeeks = (double)cost / snackGain;
        var merchWeeks = (double)cost / merchGain;

        snackWeeks.Should().BeInRange(25, 33, "the plan quotes 27 to 31 weeks");
        merchWeeks.Should().BeInRange(25, 33, "the plan quotes 27 to 31 weeks");
    }

    // Tiers 5 and and deeper are not asserted: money halves per tier while a club's fans fall by a quarter, so by
    // tier 5 a few top-level steps add nothing at the opening crowd (the build then simply does not pay).
    // The finance rework owns that curve (STRC-9); the plan asks for tiers 1 and 4.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Every_level_step_adds_weekly_profit_at_a_clubs_opening_crowd_in_every_tier(int tier)
    {
        var fans = FanRuleSet.StartFansFor(tier);
        var crowd = StadiumRuleSet.AttendanceFor(OpeningGround, fans, FanRuleSet.CalmMoodBp);

        for (var level = 2; level <= StructureRuleSet.MaxLevel; level++)
        {
            SnackWeeklyProfitMinor(level, tier, crowd)
                .Should().BeGreaterThan(SnackWeeklyProfitMinor(level - 1, tier, crowd), $"snack bar level {level} in tier {tier}");
            MerchWeeklyProfitMinor(level, tier, fans)
                .Should().BeGreaterThan(MerchWeeklyProfitMinor(level - 1, tier, fans), $"merchandise shop level {level} in tier {tier}");
        }
    }

    [Fact]
    public void What_a_spectator_or_a_fan_spends_does_not_depend_on_the_tier_only_how_many_there_are()
    {
        // The same crowd in the same building earns the same in any division; a lower one has fewer people.
        StructureRuleSet.SnackBaseMinorFor(2, 1_000).Should().Be(125_000);
        StructureRuleSet.MerchBaseMinorFor(2, 1_000).Should().Be(40_000);

        var topCrowd = StadiumRuleSet.AttendanceFor(OpeningGround, FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp);
        var lowerCrowd = StadiumRuleSet.AttendanceFor(OpeningGround, FanRuleSet.StartFansFor(4), FanRuleSet.CalmMoodBp);

        lowerCrowd.Should().BeLessThan(topCrowd);
        StructureRuleSet.SnackBaseMinorFor(1, lowerCrowd).Should().BeLessThan(StructureRuleSet.SnackBaseMinorFor(1, topCrowd));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_pages_weekly_quotes_are_the_plans_one_and_a_half_home_matches_and_the_shops_week(int level)
    {
        // The page quotes the next level with the same arithmetic the scale check above uses.
        StructureRuleSet.SnackWeeklyEstimateMinorFor(level, 2_890)
            .Should().Be(SnackWeeklyTakingsMinor(level, 2_890));
        StructureRuleSet.MerchWeeklyEstimateMinorFor(level, 10_000, FanMoodWord.Content)
            .Should().Be(StructureRuleSet.MerchTakingsMinorFor(level, 10_000, FanMoodWord.Content));
    }

    private static long SnackWeeklyTakingsMinor(int level, int crowd) =>
        StructureRuleSet.SnackBaseMinorFor(level, crowd) * HomeMatchesPerSeason * MatchdaysPerWeek / MatchdaysPerSeason;

    private static long MerchWeeklyTakingsMinor(int level, int fans, FanMoodWord mood) =>
        StructureRuleSet.MerchTakingsMinorFor(level, fans, mood);

    private static long SnackWeeklyProfitMinor(int level, int tier, int crowd) =>
        SnackWeeklyTakingsMinor(level, crowd) - StructureRuleSet.UpkeepMinorFor(level, tier);

    private static long MerchWeeklyProfitMinor(int level, int tier, int fans) =>
        MerchWeeklyTakingsMinor(level, fans, FanMoodWord.Calm) - StructureRuleSet.UpkeepMinorFor(level, tier);
}
