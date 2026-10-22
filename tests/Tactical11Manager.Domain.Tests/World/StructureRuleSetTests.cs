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
    public void The_snack_bar_spend_starts_at_six_and_rises_by_a_little_under_a_sixth_a_level(int level)
    {
        StructureRuleSet.SnackSpendMinorTier1(level).Should().Be(600 + ((level - 1) * 90));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_merchandise_spend_starts_at_two_fifty_and_reaches_ten_at_the_top(int level)
    {
        StructureRuleSet.MerchSpendMinorTier1(level).Should().BeInRange(250, 1_000);
        StructureRuleSet.MerchSpendMinorTier1(1).Should().Be(250);
        StructureRuleSet.MerchSpendMinorTier1(10).Should().Be(1_000);
    }

    [Theory]
    [InlineData(1, 10_000_000)]
    [InlineData(2, 13_000_000)]
    [InlineData(3, 17_000_000)]
    [InlineData(4, 22_000_000)]
    [InlineData(5, 29_000_000)]
    [InlineData(6, 38_000_000)]
    [InlineData(7, 49_000_000)]
    [InlineData(8, 64_000_000)]
    [InlineData(9, 83_000_000)]
    [InlineData(10, 108_000_000)]
    public void The_season_upkeep_ladder_matches_the_table(int level, long upkeepMinor)
    {
        StructureRuleSet.UpkeepSeasonMinorTier1(level).Should().Be(upkeepMinor);
    }

    [Theory]
    [InlineData(2, 15_000_000, 1)]
    [InlineData(3, 25_000_000, 1)]
    [InlineData(4, 40_000_000, 2)]
    [InlineData(5, 60_000_000, 2)]
    [InlineData(6, 90_000_000, 3)]
    [InlineData(7, 130_000_000, 4)]
    [InlineData(8, 190_000_000, 5)]
    [InlineData(9, 270_000_000, 6)]
    [InlineData(10, 380_000_000, 7)]
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
            StructureRuleSet.SnackSpendMinorTier1(level).Should().BeGreaterThan(StructureRuleSet.SnackSpendMinorTier1(level - 1));
            StructureRuleSet.MerchSpendMinorTier1(level).Should().BeGreaterThan(StructureRuleSet.MerchSpendMinorTier1(level - 1));
            StructureRuleSet.UpkeepSeasonMinorTier1(level).Should().BeGreaterThan(StructureRuleSet.UpkeepSeasonMinorTier1(level - 1));
            StructureRuleSet.UpgradeCostMinorTier1(level).Should().BeGreaterThan(level == 2 ? 0 : StructureRuleSet.UpgradeCostMinorTier1(level - 1));
            StructureRuleSet.BuildDaysFor(level).Should().BeGreaterThanOrEqualTo(level == 2 ? 1 : StructureRuleSet.BuildDaysFor(level - 1));
        }
    }

    [Fact]
    public void A_level_outside_the_ten_levels_is_a_programming_error()
    {
        var tooLow = () => StructureRuleSet.SnackSpendMinorTier1(0);
        var tooHigh = () => StructureRuleSet.UpkeepSeasonMinorTier1(11);
        var freeUpgrade = () => StructureRuleSet.UpgradeCostMinorTier1(1);
        var pastTheTop = () => StructureRuleSet.BuildDaysFor(11);

        tooLow.Should().Throw<ArgumentOutOfRangeException>();
        tooHigh.Should().Throw<ArgumentOutOfRangeException>();
        freeUpgrade.Should().Throw<ArgumentOutOfRangeException>("level 1 is opened, never bought");
        pastTheTop.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Upkeep_and_an_upgrade_follow_the_league_money_and_neither_rounds_to_nothing()
    {
        StructureRuleSet.UpkeepSeasonMinorFor(4, 1).Should().Be(22_000_000);
        StructureRuleSet.UpkeepSeasonMinorFor(4, 2).Should().Be(22_000_000L * 4 / 10, "a tier-2 club keeps its buildings for 0.4 of the tier-1 cost");
        StructureRuleSet.UpkeepMinorFor(4, 1).Should().Be(22_000_000L * 3 / EconomyRuleSet.MatchdaysPerSeason, "a weekly run settles three matchdays of the season");
        StructureRuleSet.UpgradeCostMinorFor(6, 2).Should().Be(90_000_000L * 4 / 10);

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
        // 2,890 people at level 1 (6.00 each), then lifted by an underdog win.
        StructureRuleSet.SnackBaseMinorFor(1, 2_890, 1).Should().Be(1_734_000);
        StructureRuleSet.SnackTakingsMinorFor(1, 2_890, 0, 1).Should().Be(1_734_000);
        StructureRuleSet.SnackTakingsMinorFor(1, 2_890, 3_000, 1).Should().Be(2_254_200);
        StructureRuleSet.SnackTakingsMinorFor(10, 2_890, 1_500, 1).Should().Be(2_890L * 1_410 * 115 / 100);
        StructureRuleSet.SnackTakingsMinorFor(1, 0, 3_000, 1).Should().Be(0, "an empty ground sells nothing");
        StructureRuleSet.SnackTakingsMinorFor(1, 2_890, 0, 2).Should().Be(2_890L * (600L * 5_333 / 10_000), "a tier-2 spectator spends at the tier-2 price level");
    }

    [Fact]
    public void The_merchandise_shop_takes_the_fans_times_the_spend_lifted_by_their_mood()
    {
        // 10,000 calm fans at level 1 (2.50 each) is 25,000.00 before the 5% mood bonus.
        StructureRuleSet.MerchBaseMinorFor(1, 10_000, 1).Should().Be(2_500_000);
        StructureRuleSet.MerchTakingsMinorFor(1, 10_000, FanMoodWord.Calm, 1).Should().Be(2_625_000);
        StructureRuleSet.MerchTakingsMinorFor(1, 10_000, FanMoodWord.Furious, 1).Should().Be(2_500_000);
        StructureRuleSet.MerchTakingsMinorFor(1, 10_000, FanMoodWord.Euphoric, 1).Should().Be(3_125_000);
        StructureRuleSet.MerchTakingsMinorFor(10, 10_000, FanMoodWord.Euphoric, 1).Should().Be(12_500_000);
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
        var spectators = () => StructureRuleSet.SnackBaseMinorFor(1, -1, 1);
        var fans = () => StructureRuleSet.MerchBaseMinorFor(1, -1, 1);

        spectators.Should().Throw<ArgumentOutOfRangeException>();
        fans.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_tier_one_club_at_its_opening_numbers_takes_about_twenty_six_thousand_a_week_from_each_building()
    {
        var crowd = StadiumRuleSet.AttendanceFor(OpeningGround, FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp);

        crowd.Should().Be(2_890);

        var snackPerWeek = SnackWeeklyTakingsMinor(1, 1, crowd);
        var merchPerWeek = MerchWeeklyTakingsMinor(1, 1, FanRuleSet.StartFansFor(1), FanMoodWord.Calm);

        snackPerWeek.Should().BeInRange(2_550_000, 2_650_000, "about 26,000.00 a week at level 1");
        merchPerWeek.Should().Be(2_625_000, "26,250.00 a week for calm fans at level 1");
    }

    // A club that has grown past its opening crowd is the one that upgrades: its first steps repay in a few seasons and the dearest in a dozen. The
    // opening club's do not, which is the point (an upgrade is bought when the crowd is there to use it).
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void An_upgrade_repays_in_a_few_seasons_for_a_grown_club_in_every_tier(int tier)
    {
        for (var target = 2; target <= StructureRuleSet.MaxLevel; target++)
        {
            // The dearest steps are bought by a club with a crowd to match: 20,000 spectators for the first five, 50,000 after.
            var (fans, crowd) = GrownClub(tier, target <= 5 ? 1 : 2.5);

            var snackGain = (SnackWeeklyProfitMinor(target, tier, crowd) - SnackWeeklyProfitMinor(target - 1, tier, crowd)) * MatchdaysPerSeason / MatchdaysPerWeek;
            var merchGain = (MerchWeeklyProfitMinor(target, tier, fans) - MerchWeeklyProfitMinor(target - 1, tier, fans)) * MatchdaysPerSeason / MatchdaysPerWeek;
            var cost = StructureRuleSet.UpgradeCostMinorFor(target, tier);

            snackGain.Should().BeGreaterThan(0, $"snack bar level {target} in tier {tier}");
            merchGain.Should().BeGreaterThan(0, $"merchandise shop level {target} in tier {tier}");
            cost.Should().BeLessThan(snackGain * 12, $"snack bar level {target} in tier {tier} repays within twelve seasons");
            cost.Should().BeLessThan(merchGain * 12, $"merchandise shop level {target} in tier {tier} repays within twelve seasons");

            if (target <= 3)
            {
                cost.Should().BeLessThan(snackGain * 3, $"the first steps of the snack bar in tier {tier} repay within three seasons");
                cost.Should().BeLessThan(merchGain * 3, $"the first steps of the shop in tier {tier} repay within three seasons");
            }
        }
    }

    [Fact]
    public void The_buildings_are_worth_the_same_share_of_a_clubs_money_in_every_tier()
    {
        // Spend follows the price level and the crowd follows the fans, so a tier-4 club gains from a step what a tier-1 club does, in proportion
        // to what it earns: the ratio of the two clubs' season gains is the ratio of the league money (the finding of ADR-0080 settled).
        var (fans1, crowd1) = GrownClub(1);
        var (fans4, crowd4) = GrownClub(4);

        double Gain(int tier, int fans, int crowd) =>
            (double)(SnackWeeklyProfitMinor(6, tier, crowd) - SnackWeeklyProfitMinor(1, tier, crowd))
            + (MerchWeeklyProfitMinor(6, tier, fans) - MerchWeeklyProfitMinor(1, tier, fans));

        var buildings = Gain(4, fans4, crowd4) / Gain(1, fans1, crowd1);
        var leagueMoney = (double)EconomyRuleSet.LeagueTvSeasonMinorFor(4) / EconomyRuleSet.LeagueTvSeasonMinorFor(1);

        (buildings / leagueMoney).Should().BeInRange(0.8, 1.25, "the tier-4 to tier-1 relative gain from buildings is about 1, not above 2");
    }

    [Fact]
    public void What_a_spectator_or_a_fan_spends_follows_the_tier_price_level()
    {
        StructureRuleSet.SnackBaseMinorFor(2, 1_000, 1).Should().Be(690_000);
        StructureRuleSet.MerchBaseMinorFor(2, 1_000, 1).Should().Be(330_000);
        StructureRuleSet.SnackBaseMinorFor(2, 1_000, 2).Should().Be(1_000L * (690L * 5_333 / 10_000));
        StructureRuleSet.SnackSpendMinorFor(1, 5).Should().Be(StructureRuleSet.SnackSpendMinorFor(1, 90), "tier 5 and every tier below share a price level");

        var topCrowd = StadiumRuleSet.AttendanceFor(OpeningGround, FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp);
        var lowerCrowd = StadiumRuleSet.AttendanceFor(OpeningGround, FanRuleSet.StartFansFor(4), FanRuleSet.CalmMoodBp);

        lowerCrowd.Should().BeLessThan(topCrowd);
        StructureRuleSet.SnackBaseMinorFor(1, lowerCrowd, 4).Should().BeLessThan(StructureRuleSet.SnackBaseMinorFor(1, topCrowd, 1));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_pages_weekly_quotes_are_the_plans_one_and_a_half_home_matches_and_the_shops_week(int level)
    {
        // The page quotes the next level with the same arithmetic the scale check above uses.
        StructureRuleSet.SnackWeeklyEstimateMinorFor(level, 2_890, 1)
            .Should().Be(StructureRuleSet.SnackBaseMinorFor(level, 2_890, 1) * 3 / 2);
        StructureRuleSet.MerchWeeklyEstimateMinorFor(level, 10_000, FanMoodWord.Content, 1)
            .Should().Be(StructureRuleSet.MerchTakingsMinorFor(level, 10_000, FanMoodWord.Content, 1));
    }

    // A club of the tier with 80,000 fans at the tier-1 scale and the crowd that fills 20,000 places of the tier-1 scale: both fall by a quarter a tier.
    private static (int Fans, int Crowd) GrownClub(int tier, double size = 1)
    {
        var factor = Math.Pow(0.75, tier - 1) * size;

        return ((int)(80_000 * factor), (int)(20_000 * factor));
    }

    private static long SnackWeeklyTakingsMinor(int level, int tier, int crowd) =>
        StructureRuleSet.SnackBaseMinorFor(level, crowd, tier) * HomeMatchesPerSeason * MatchdaysPerWeek / MatchdaysPerSeason;

    private static long MerchWeeklyTakingsMinor(int level, int tier, int fans, FanMoodWord mood) =>
        StructureRuleSet.MerchTakingsMinorFor(level, fans, mood, tier);

    private static long SnackWeeklyProfitMinor(int level, int tier, int crowd) =>
        SnackWeeklyTakingsMinor(level, tier, crowd) - StructureRuleSet.UpkeepMinorFor(level, tier);

    private static long MerchWeeklyProfitMinor(int level, int tier, int fans) =>
        MerchWeeklyTakingsMinor(level, tier, fans, FanMoodWord.Calm) - StructureRuleSet.UpkeepMinorFor(level, tier);
}
