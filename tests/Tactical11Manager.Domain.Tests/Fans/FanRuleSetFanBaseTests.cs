using FluentAssertions;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Tests.Fans;

/// <summary>
/// The fan count and the fan mood (`FAN-1`, `FAN-2`, `FAN-5`..`FAN-9`): tier numbers, what a result, a week, a season, a change of division
/// and a takeover do, and how the whole behaves over simulated seasons.
/// </summary>
public sealed class FanRuleSetFanBaseTests
{
    private const int Tier1 = 1;

    private static Guid Id(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static FanBaseState Calm(int fans) => new(fans, FanRuleSet.CalmMoodBp);

    // ---- tier numbers -------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 10_000, 4_000, 183_333, 3_000)]
    [InlineData(2, 7_500, 3_000, 137_500, 2_250)]
    [InlineData(3, 5_625, 2_250, 103_125, 1_688)]
    public void A_tier_opens_with_the_plans_numbers(int tier, int start, int floor, int cap, int wantToCome)
    {
        FanRuleSet.StartFansFor(tier).Should().Be(start);
        FanRuleSet.FloorFansFor(tier).Should().Be(floor);
        FanRuleSet.SoftCapFansFor(tier).Should().Be(cap);
        FanRuleSet.WantToComeFor(FanRuleSet.StartFansFor(tier)).Should().Be(wantToCome);
    }

    [Fact]
    public void Each_tier_down_keeps_three_quarters_and_the_floor_is_below_the_start_and_the_start_below_the_cap()
    {
        for (var tier = 1; tier <= 9; tier++)
        {
            FanRuleSet.FloorFansFor(tier).Should().BeGreaterThan(0).And.BeLessThan(FanRuleSet.StartFansFor(tier));
            FanRuleSet.StartFansFor(tier).Should().BeLessThan(FanRuleSet.SoftCapFansFor(tier));

            if (tier > 1)
            {
                ((double)FanRuleSet.StartFansFor(tier) / FanRuleSet.StartFansFor(tier - 1)).Should().BeApproximately(0.75, 0.001);
                ((double)FanRuleSet.SoftCapFansFor(tier) / FanRuleSet.SoftCapFansFor(tier - 1)).Should().BeApproximately(0.75, 0.001);
            }
        }
    }

    [Fact]
    public void A_tier_below_the_first_is_refused_to_be_asked_for()
    {
        var act = () => FanRuleSet.StartFansFor(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_new_club_and_a_new_manager_start_from_the_same_opening_numbers()
    {
        FanRuleSet.OpeningFor(2).Should().Be(new FanBaseState(7_500, FanRuleSet.CalmMoodBp, null));
        FanRuleSet.OnTakeover(2).Should().Be(FanRuleSet.OpeningFor(2));
    }

    // ---- the random amount --------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_random_amount_stays_in_its_range_and_is_the_same_for_the_same_match()
    {
        var seen = new HashSet<int>();

        for (var fixture = 0; fixture < 4_000; fixture++)
        {
            var random = FanRuleSet.MatchRandomFor(Id(fixture), Id(fixture % 18));

            random.Should().BeInRange(FanRuleSet.MatchRandomLow, FanRuleSet.MatchRandomHigh);
            random.Should().Be(FanRuleSet.MatchRandomFor(Id(fixture), Id(fixture % 18)));
            seen.Add(random);
        }

        seen.Should().Contain(FanRuleSet.MatchRandomLow).And.Contain(FanRuleSet.MatchRandomHigh, "both ends of the range are reachable");
        seen.Count.Should().Be(FanRuleSet.MatchRandomHigh - FanRuleSet.MatchRandomLow + 1);
    }

    [Fact]
    public void The_random_amount_differs_between_the_two_clubs_of_a_fixture()
    {
        var differ = Enumerable.Range(0, 200).Count(fixture => FanRuleSet.MatchRandomFor(Id(fixture), Id(1)) != FanRuleSet.MatchRandomFor(Id(fixture), Id(2)));

        differ.Should().BeGreaterThan(150);
    }

    // ---- a result and the count ---------------------------------------------------------------------------------------------------

    /// <summary>People who want to come that a match added (positive) or took away, read back from the fans at tier 1 with its random amount removed.</summary>
    private static int PeopleMoved(int before, int expectedCentis, int goalsFor, int goalsAgainst, int fixture = 7)
    {
        var after = FanRuleSet.AfterMatch(Calm(before), Tier1, expectedCentis, goalsFor, goalsAgainst, Id(fixture), Id(3), firstSeason: false);

        return (int)Math.Round((after.Fans - before) * FanRuleSet.WantToComeShareBp / 10_000.0);
    }

    [Theory]
    [InlineData(60, 600)]
    [InlineData(99, 600)]
    [InlineData(100, 450)]
    [InlineData(159, 450)]
    [InlineData(160, 325)]
    [InlineData(280, 325)]
    public void A_win_brings_fans_by_what_the_fans_expected(int expectedCentis, int people)
    {
        var random = FanRuleSet.MatchRandomFor(Id(7), Id(3));

        PeopleMoved(30_000, expectedCentis, 2, 0).Should().BeCloseTo(people + random, 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void A_draw_the_fans_did_not_expect_brings_a_hundred(int expectedCentis)
    {
        var random = FanRuleSet.MatchRandomFor(Id(7), Id(3));

        PeopleMoved(30_000, expectedCentis, 1, 1).Should().BeCloseTo(FanRuleSet.JoinAfterUnderdogDraw + random, 1);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(159)]
    [InlineData(160)]
    [InlineData(250)]
    public void A_draw_in_an_even_game_or_as_the_favourite_changes_no_count(int expectedCentis)
    {
        PeopleMoved(30_000, expectedCentis, 0, 0).Should().Be(0);
    }

    [Theory]
    [InlineData(160, 300)]
    [InlineData(299, 300)]
    [InlineData(100, 250)]
    [InlineData(159, 250)]
    [InlineData(99, 0)]
    [InlineData(10, 0)]
    public void A_defeat_costs_fans_by_what_the_fans_expected(int expectedCentis, int people)
    {
        var random = FanRuleSet.MatchRandomFor(Id(7), Id(3));
        var expected = people == 0 ? 0 : -(people + random);

        PeopleMoved(30_000, expectedCentis, 0, 1).Should().BeCloseTo(expected, 1);
    }

    [Fact]
    public void A_gain_and_a_loss_keep_the_same_range_around_their_amount()
    {
        var gains = new List<int>();
        var losses = new List<int>();

        for (var fixture = 0; fixture < 600; fixture++)
        {
            gains.Add(PeopleMoved(30_000, 130, 3, 0, fixture));
            losses.Add(-PeopleMoved(30_000, 130, 0, 3, fixture));
        }

        gains.Min().Should().BeInRange(450 - 15 - 1, 450 - 15 + 3);
        gains.Max().Should().BeInRange(450 + 50 - 3, 450 + 50 + 1);
        losses.Min().Should().BeInRange(250 - 15 - 1, 250 - 15 + 3);
        losses.Max().Should().BeInRange(250 + 50 - 3, 250 + 50 + 1);
    }

    [Fact]
    public void A_tier_down_scales_a_gain_by_three_quarters()
    {
        var tier1 = FanRuleSet.AfterMatch(Calm(10_000), 1, 130, 3, 0, Id(7), Id(3), firstSeason: false).Fans - 10_000;
        var tier2 = FanRuleSet.AfterMatch(Calm(10_000), 2, 130, 3, 0, Id(7), Id(3), firstSeason: false).Fans - 10_000;

        ((double)tier2 / tier1).Should().BeApproximately(0.75, 0.002);
    }

    [Fact]
    public void The_same_match_gives_the_same_result_every_time()
    {
        var first = FanRuleSet.AfterMatch(new FanBaseState(40_000, 6_100, 2), 1, 143, 2, 1, Id(11), Id(5), firstSeason: false);
        var second = FanRuleSet.AfterMatch(new FanBaseState(40_000, 6_100, 2), 1, 143, 2, 1, Id(11), Id(5), firstSeason: false);

        second.Should().Be(first);
        first.LastWeekGap.Should().Be(2, "a match leaves the last review where it was");
    }

    // ---- the floor and the soft cap -----------------------------------------------------------------------------------------------

    [Fact]
    public void The_floor_is_never_crossed_however_many_favourites_lose()
    {
        var state = Calm(FanRuleSet.FloorFansFor(Tier1) + 500);

        for (var match = 0; match < 200; match++)
        {
            state = FanRuleSet.AfterMatch(state, Tier1, 250, 0, 3, Id(match), Id(1), firstSeason: false);
            state.Fans.Should().BeGreaterThanOrEqualTo(FanRuleSet.FloorFansFor(Tier1));
        }

        state.Fans.Should().Be(FanRuleSet.FloorFansFor(Tier1), "the loyal core stays");
        state.MoodBp.Should().BeInRange(0, FanRuleSet.MoodMaxBp);
    }

    [Fact]
    public void Gains_fade_out_over_the_last_quarter_below_the_cap_and_stop_at_it()
    {
        var cap = FanRuleSet.SoftCapFansFor(Tier1);
        var gains = new List<int>();

        foreach (var share in new[] { 0.50, 0.74, 0.80, 0.875, 0.95, 0.999, 1.0, 1.10 })
        {
            var fans = (int)(cap * share);

            gains.Add(FanRuleSet.AfterMatch(Calm(fans), Tier1, 130, 2, 0, Id(7), Id(3), firstSeason: false).Fans - fans);
        }

        gains[0].Should().Be(gains[1], "below three quarters of the cap nothing fades");
        gains.Should().BeInDescendingOrder();
        gains[2].Should().BeLessThan(gains[1]);
        ((double)gains[3] / gains[1]).Should().BeApproximately(0.5, 0.01, "half way through the fade");
        gains[5].Should().BeLessThan(gains[1] / 100);
        gains[6].Should().Be(0);
        gains[7].Should().Be(0, "a club over the cap gains nothing");
    }

    [Fact]
    public void A_club_over_the_cap_still_loses_fans_after_a_defeat()
    {
        var fans = (int)(FanRuleSet.SoftCapFansFor(Tier1) * 1.1);

        FanRuleSet.AfterMatch(Calm(fans), Tier1, 200, 0, 2, Id(7), Id(3), firstSeason: false).Fans.Should().BeLessThan(fans);
    }

    // ---- the first-season boost ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 0.10, 0.11)]
    [InlineData(2, 0.05, 0.055)]
    [InlineData(3, 0.0, 0.0)]
    [InlineData(5, 0.0, 0.0)]
    public void A_clubs_first_season_boost_separates_the_tiers_by_five_per_cent(int tier, double atLeast, double atMost)
    {
        var state = Calm(FanRuleSet.StartFansFor(tier));
        var start = state.Fans;

        // A season of games the fans took for granted (a draw as the favourite) so only the boost moves the count.
        for (var match = 0; match < FanRuleSet.FirstSeasonMatches; match++)
        {
            state = FanRuleSet.AfterMatch(state, tier, 250, 1, 1, Id(match), Id(1), firstSeason: true);
        }

        var growth = (double)state.Fans / start - 1;

        growth.Should().BeInRange(atLeast, atMost);

        var second = FanRuleSet.AfterMatch(Calm(start), tier, 250, 1, 1, Id(1), Id(1), firstSeason: false);

        second.Fans.Should().Be(start, "after the first season there is no boost");
    }

    // ---- the mood after a match ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(210, 1, 0)]
    [InlineData(210, 0, 1)]
    [InlineData(60, 3, 0)]
    [InlineData(60, 0, 0)]
    [InlineData(150, 1, 1)]
    [InlineData(280, 0, 2)]
    public void A_surprise_moves_the_mood_by_its_size_to_the_power_three_quarters(int expectedCentis, int goalsFor, int goalsAgainst)
    {
        var points = goalsFor > goalsAgainst ? 3 : goalsFor == goalsAgainst ? 1 : 0;
        var surprise = ((points * 100) - expectedCentis) / 100.0;
        var move = Math.Sign(surprise) * 300 * Math.Pow(Math.Abs(surprise), 0.75) * (surprise < 0 ? 1.5 : 1.0);
        var margin = Math.Sign(goalsFor - goalsAgainst) * Math.Min(Math.Abs(goalsFor - goalsAgainst), 4) * 25;

        FanRuleSet.MatchMoodShiftBp(expectedCentis, goalsFor, goalsAgainst).Should().BeCloseTo((int)Math.Round(move + margin), 1);
    }

    [Fact]
    public void A_defeat_weighs_one_and_a_half_times_a_win_of_the_same_surprise()
    {
        // 1.5 expected points: a win is a surprise of +1.5, a defeat of -1.5. Same goal margin both ways so the margin term cancels.
        var win = FanRuleSet.MatchMoodShiftBp(150, 1, 0);
        var loss = FanRuleSet.MatchMoodShiftBp(150, 0, 1);

        (-loss - 25).Should().BeCloseTo((int)Math.Round((win - 25) * 1.5), 2);
    }

    [Fact]
    public void A_big_surprise_counts_for_less_than_double()
    {
        var small = FanRuleSet.MatchMoodShiftBp(200, 1, 0) - 25;
        var large = FanRuleSet.MatchMoodShiftBp(0, 1, 0) - 25;

        large.Should().BeGreaterThan(small);
        large.Should().BeLessThan(small * 3, "a win from nothing is a surprise of 3.0 against 1.0");
    }

    [Fact]
    public void A_bigger_margin_counts_for_a_little_more_up_to_four_goals()
    {
        var one = FanRuleSet.MatchMoodShiftBp(140, 1, 0);
        var four = FanRuleSet.MatchMoodShiftBp(140, 4, 0);
        var seven = FanRuleSet.MatchMoodShiftBp(140, 7, 0);

        (four - one).Should().Be(75);
        seven.Should().Be(four);
    }

    [Fact]
    public void After_a_match_the_mood_is_pulled_toward_calm_and_kept_on_its_scale()
    {
        var content = FanRuleSet.AfterMatch(new FanBaseState(20_000, 8_000), 1, 145, 0, 0, Id(1), Id(1), firstSeason: false);
        var gloomy = FanRuleSet.AfterMatch(new FanBaseState(20_000, 2_000), 1, 145, 0, 0, Id(1), Id(1), firstSeason: false);

        // A draw against 1.45 expected is a small bad surprise; the pull is 2% of the distance to calm.
        content.MoodBp.Should().BeLessThan(8_000);
        gloomy.MoodBp.Should().BeGreaterThan(2_000 - 100);

        FanRuleSet.AfterMatch(new FanBaseState(20_000, 9_990), 1, 0, 6, 0, Id(1), Id(1), firstSeason: false).MoodBp.Should().Be(FanRuleSet.MoodMaxBp);
        FanRuleSet.AfterMatch(new FanBaseState(20_000, 10), 1, 300, 0, 6, Id(1), Id(1), firstSeason: false).MoodBp.Should().Be(0);
    }

    // ---- a weekly review ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_review_on_target_changes_nothing_but_remembers_the_gap()
    {
        var after = FanRuleSet.AfterWeek(new FanBaseState(30_000, 6_200), gap: 0, gamesPlayed: 17, seasonGames: 34);

        after.Should().Be(new FanBaseState(30_000, 6_200, 0));
    }

    [Fact]
    public void A_review_below_the_target_costs_mood_by_the_place_and_the_part_of_the_season_gone()
    {
        // Four places short, half the season: -40 x 4 = -160, halved.
        FanRuleSet.AfterWeek(Calm(30_000), 4, 17, 34).MoodBp.Should().Be(5_000 - 80);
        FanRuleSet.AfterWeek(Calm(30_000), -4, 17, 34).MoodBp.Should().Be(5_000 + 80);
        FanRuleSet.AfterWeek(Calm(30_000), 4, 34, 34).MoodBp.Should().Be(5_000 - 160);
        FanRuleSet.AfterWeek(Calm(30_000), 4, 0, 34).MoodBp.Should().Be(5_000, "a review before a game is played counts for nothing");
    }

    [Fact]
    public void A_review_counts_at_most_six_places_of_gap()
    {
        FanRuleSet.AfterWeek(Calm(30_000), 6, 34, 34).MoodBp.Should().Be(FanRuleSet.AfterWeek(Calm(30_000), 16, 34, 34).MoodBp);
        FanRuleSet.AfterWeek(Calm(30_000), -6, 34, 34).MoodBp.Should().Be(FanRuleSet.AfterWeek(Calm(30_000), -16, 34, 34).MoodBp);
    }

    [Fact]
    public void A_club_closing_in_on_its_target_is_rewarded_and_one_falling_away_is_punished()
    {
        var closing = FanRuleSet.AfterWeek(new FanBaseState(30_000, 5_000, 6), 4, 34, 34);
        var falling = FanRuleSet.AfterWeek(new FanBaseState(30_000, 5_000, 2), 4, 34, 34);

        closing.MoodBp.Should().Be(5_000 - 160 + 200, "two places closer since last week");
        falling.MoodBp.Should().Be(5_000 - 160 - 200, "two places further");
        closing.LastWeekGap.Should().Be(4);
    }

    [Fact]
    public void A_review_leaves_the_count_alone_and_keeps_the_mood_on_its_scale()
    {
        var low = FanRuleSet.AfterWeek(new FanBaseState(30_000, 50, 0), 6, 34, 34);
        var high = FanRuleSet.AfterWeek(new FanBaseState(30_000, 9_990, 0), -6, 34, 34);

        low.MoodBp.Should().BeGreaterThanOrEqualTo(0);
        high.MoodBp.Should().BeLessThanOrEqualTo(FanRuleSet.MoodMaxBp);
        low.Fans.Should().Be(30_000);
        high.Fans.Should().Be(30_000);
    }

    // ---- a season, a change of division and a takeover ----------------------------------------------------------------------------

    [Fact]
    public void The_end_of_a_season_judges_the_place_and_then_moves_the_mood_halfway_back_to_calm()
    {
        // Three places short of the target: -150 x 3 = -450, then half of what is left of the distance.
        var after = FanRuleSet.AfterSeason(new FanBaseState(30_000, 8_000, 3), Tier1, finalGap: 3);

        after.MoodBp.Should().Be(5_000 + ((8_000 - 450 - 5_000) / 2));
        after.LastWeekGap.Should().BeNull("a new season brings a new target");
        after.Fans.Should().Be(30_000);
    }

    [Fact]
    public void A_season_without_a_target_only_moves_the_mood_halfway_back()
    {
        FanRuleSet.AfterSeason(new FanBaseState(30_000, 1_000), Tier1, finalGap: null).MoodBp.Should().Be(5_000 + ((1_000 - 5_000) / 2));
    }

    [Fact]
    public void Fans_above_the_cap_drift_down_a_quarter_of_the_excess_a_season_but_never_to_the_floor()
    {
        var cap = FanRuleSet.SoftCapFansFor(2);
        var after = FanRuleSet.AfterSeason(new FanBaseState(cap + 20_000, 5_000), 2, finalGap: 0);

        after.Fans.Should().Be(cap + 15_000);
        FanRuleSet.AfterSeason(new FanBaseState(cap - 1, 5_000), 2, 0).Fans.Should().Be(cap - 1, "under the cap nothing drifts");
        FanRuleSet.AfterSeason(new FanBaseState(1, 5_000), 2, 0).Fans.Should().Be(FanRuleSet.FloorFansFor(2));
    }

    [Theory]
    [InlineData(2, 1, 100_000, 110_000)]
    [InlineData(1, 2, 100_000, 90_000)]
    [InlineData(1, 1, 100_000, 100_000)]
    [InlineData(3, 1, 100_000, 121_000)]
    [InlineData(1, 3, 100_000, 81_000)]
    public void Promotion_adds_ten_per_cent_and_relegation_takes_ten_per_cent_for_each_tier(int from, int to, int fans, int expected)
    {
        FanRuleSet.OnTierChange(new FanBaseState(fans, 6_500, 3), from, to).Should().Be(new FanBaseState(expected, 6_500, 3));
    }

    [Fact]
    public void A_promoted_club_is_lifted_to_the_new_floor_and_a_relegated_one_keeps_what_is_left()
    {
        FanRuleSet.OnTierChange(Calm(FanRuleSet.FloorFansFor(2)), 2, 1).Fans.Should().Be(FanRuleSet.FloorFansFor(1), "3,000 plus a tenth is still under tier 1's floor of 4,000");
        FanRuleSet.OnTierChange(Calm(FanRuleSet.FloorFansFor(1)), 1, 2).Fans.Should().Be(3_600, "a tenth off 4,000 is 3,600, over tier 2's floor of 3,000");
    }

    [Fact]
    public void A_takeover_resets_whatever_the_club_was_like_to_the_tiers_defaults()
    {
        var wreck = new FanBaseState(FanRuleSet.FloorFansFor(Tier1), 300, 6);
        var giant = new FanBaseState(FanRuleSet.SoftCapFansFor(Tier1), 9_700, -5);

        FanRuleSet.OnTakeover(Tier1).Should().Be(new FanBaseState(10_000, 5_000, null));
        FanRuleSet.OnTakeover(Tier1).Should().NotBe(wreck).And.NotBe(giant);
    }

    // ---- the league ---------------------------------------------------------------------------------------------------------------

    /// <summary>The strength of each of the 18 sides in a league the way the sandbox spread them: a rank apart is 0.30 of an attribute point.</summary>
    private static int[] LeagueStrengths()
    {
        var strengths = new int[18];

        strengths[0] = 1_500;

        for (var club = 1; club < 18; club++)
        {
            strengths[club] = 1_500 + ((club - 9) * 30);
        }

        return strengths;
    }

    /// <summary>The exact mean of <see cref="FanRuleSet.MatchMoodShiftBp"/> over every match of an 18-side league, from each side's point of view.</summary>
    private static double MeanShiftOverALeagueBp()
    {
        var strengths = LeagueStrengths();
        var total = 0.0;
        var count = 0;

        for (var home = 0; home < strengths.Length; home++)
        {
            for (var away = 0; away < strengths.Length; away++)
            {
                if (home == away)
                {
                    continue;
                }

                var gap = strengths[home] - strengths[away];
                var (homeGoals, awayGoals) = FanRuleSet.ExpectedGoalsForGap(gap);

                foreach (var atHome in new[] { true, false })
                {
                    total += MeanShiftBp(FanRuleSet.ExpectationForGap(gap, atHome), atHome ? homeGoals : awayGoals, atHome ? awayGoals : homeGoals);
                    count++;
                }
            }
        }

        return total / count;
    }

    /// <summary>
    /// The mean shift for one side of one fixture. The result's chances come from the curve; given the result, the score follows two
    /// Poisson laws around the curve's goals, so a win by a lot is rarer than a win by one.
    /// </summary>
    private static double MeanShiftBp(FanExpectation expectation, int goalsForCentis, int goalsAgainstCentis)
    {
        const int Most = 12;
        var forChances = Poisson(goalsForCentis / 100.0, Most);
        var againstChances = Poisson(goalsAgainstCentis / 100.0, Most);
        var classMass = new double[3];

        for (var scored = 0; scored <= Most; scored++)
        {
            for (var conceded = 0; conceded <= Most; conceded++)
            {
                classMass[ClassOf(scored, conceded)] += forChances[scored] * againstChances[conceded];
            }
        }

        var target = new[] { expectation.LossBp / 10_000.0, expectation.DrawBp / 10_000.0, expectation.WinBp / 10_000.0 };
        var mean = 0.0;

        for (var scored = 0; scored <= Most; scored++)
        {
            for (var conceded = 0; conceded <= Most; conceded++)
            {
                var cls = ClassOf(scored, conceded);
                var chance = forChances[scored] * againstChances[conceded] / classMass[cls] * target[cls];

                mean += chance * FanRuleSet.MatchMoodShiftBp(expectation.ExpectedPointsCentis, scored, conceded);
            }
        }

        return mean;
    }

    private static int ClassOf(int scored, int conceded) => scored < conceded ? 0 : scored == conceded ? 1 : 2;

    private static double[] Poisson(double mean, int most)
    {
        var chances = new double[most + 1];

        chances[0] = Math.Exp(-mean);

        for (var goals = 1; goals <= most; goals++)
        {
            chances[goals] = chances[goals - 1] * mean / goals;
        }

        return chances;
    }

    [Fact]
    public void The_neutral_offset_cancels_what_a_league_of_expectations_would_do_to_the_mood()
    {
        var mean = MeanShiftOverALeagueBp();

        // Defeats weigh more than wins, so an exactly average club would drift down; the offset is what brings it back to level.
        mean.Should().BeLessThan(0, "a bad surprise weighs one and a half times a good one");
        (mean + FanRuleSet.NeutralOffsetBp).Should().BeApproximately(0, 2, $"mean shift over the league is {mean:0.0} bp a match");
    }

    // ---- simulated seasons --------------------------------------------------------------------------------------------------------

    private sealed record ClubRun(IReadOnlyList<int> FansAtSeasonEnd, IReadOnlyList<int> MoodAfterEachMatch, int FansAtStart, IReadOnlyList<int> Places);

    /// <summary>Plays an 18-side league for several seasons with the club under test as side 0, and follows its fans.</summary>
    private static ClubRun Simulate(int hiddenSkillCentis, int seasons, ulong seed, bool weeklyReviews)
    {
        var strengths = LeagueStrengths();
        var truth = (int[])strengths.Clone();

        truth[0] += hiddenSkillCentis;

        var target = 1 + strengths.Skip(1).Count(strength => strength > strengths[0]);
        var random = new Pcg32(seed);
        var rounds = Schedule(18);
        var state = FanRuleSet.OpeningFor(Tier1);
        var start = state.Fans;
        var ends = new List<int>();
        var moods = new List<int>();
        var places = new List<int>();

        for (var season = 0; season < seasons; season++)
        {
            var points = new int[18];
            var games = new int[18];
            var goalDifference = new int[18];

            for (var round = 0; round < rounds.Count; round++)
            {
                foreach (var (home, away) in rounds[round])
                {
                    var forms = (home: new FanForm(games[home], points[home]), away: new FanForm(games[away], points[away]));
                    var (homeGoals, awayGoals) = PlayMatch(FanRuleSet.ExpectationForGap(truth[home] - truth[away], atHome: true), FanRuleSet.ExpectedGoalsForGap(truth[home] - truth[away]), random);

                    if (home == 0 || away == 0)
                    {
                        var atHome = home == 0;
                        var opponent = atHome ? away : home;
                        var expectation = FanRuleSet.ExpectationFor(
                            strengths[0],
                            atHome ? forms.home : forms.away,
                            strengths[opponent],
                            atHome ? forms.away : forms.home,
                            atHome);

                        state = FanRuleSet.AfterMatch(
                            state,
                            Tier1,
                            expectation.ExpectedPointsCentis,
                            atHome ? homeGoals : awayGoals,
                            atHome ? awayGoals : homeGoals,
                            Id((season * 1_000) + (round * 10) + (atHome ? home : away)),
                            Id(0),
                            firstSeason: false);
                        state.Fans.Should().BeGreaterThanOrEqualTo(FanRuleSet.FloorFansFor(Tier1));
                        moods.Add(state.MoodBp);
                    }

                    games[home]++;
                    games[away]++;
                    goalDifference[home] += homeGoals - awayGoals;
                    goalDifference[away] += awayGoals - homeGoals;
                    points[home] += homeGoals > awayGoals ? 3 : homeGoals == awayGoals ? 1 : 0;
                    points[away] += awayGoals > homeGoals ? 3 : homeGoals == awayGoals ? 1 : 0;
                }

                if (weeklyReviews && (round + 1) % 3 == 0 && round + 1 < rounds.Count)
                {
                    state = FanRuleSet.AfterWeek(state, PlaceOfClub0(points, goalDifference) - target, round + 1, rounds.Count);
                }
            }

            ends.Add(state.Fans);
            places.Add(PlaceOfClub0(points, goalDifference));
            state = FanRuleSet.AfterSeason(state, Tier1, weeklyReviews ? PlaceOfClub0(points, goalDifference) - target : null);
        }

        return new ClubRun(ends, moods, start, places);
    }

    private static int PlaceOfClub0(int[] points, int[] goalDifference) =>
        1 + Enumerable.Range(1, 17).Count(club => points[club] > points[0] || (points[club] == points[0] && goalDifference[club] > goalDifference[0]));

    /// <summary>Draws a result from the curve's chances, then a score for it from two Poisson laws around the curve's goals.</summary>
    private static (int Home, int Away) PlayMatch(FanExpectation homeView, (int HomeGoalsCentis, int AwayGoalsCentis) goals, Pcg32 random)
    {
        var roll = random.NextInt(10_000);
        var result = roll < homeView.WinBp ? 2 : roll < homeView.WinBp + homeView.DrawBp ? 1 : 0;

        for (var attempt = 0; attempt < 500; attempt++)
        {
            var home = PoissonDraw(goals.HomeGoalsCentis / 100.0, random);
            var away = PoissonDraw(goals.AwayGoalsCentis / 100.0, random);

            if (ClassOf(home, away) == result)
            {
                return (home, away);
            }
        }

        return result == 2 ? (1, 0) : result == 1 ? (0, 0) : (0, 1);
    }

    private static int PoissonDraw(double mean, Pcg32 random)
    {
        var limit = Math.Exp(-mean);
        var goals = 0;
        var product = random.NextDouble();

        while (product > limit && goals < 15)
        {
            goals++;
            product *= random.NextDouble();
        }

        return goals;
    }

    /// <summary>A double round robin by the circle method: 34 rounds of 9 fixtures, the second half the first with the venues swapped.</summary>
    private static List<List<(int Home, int Away)>> Schedule(int clubs)
    {
        var order = Enumerable.Range(0, clubs).ToArray();
        var first = new List<List<(int Home, int Away)>>();

        for (var round = 0; round < clubs - 1; round++)
        {
            var fixtures = new List<(int Home, int Away)>();

            for (var slot = 0; slot < clubs / 2; slot++)
            {
                var one = order[slot];
                var two = order[clubs - 1 - slot];
                var swap = slot == 0 ? round % 2 == 0 : (round + slot) % 2 == 0;

                fixtures.Add(swap ? (one, two) : (two, one));
            }

            first.Add(fixtures);
            order = [order[0], order[^1], .. order[1..^1]];
        }

        return [.. first, .. first.Select(round => round.Select(fixture => (fixture.Away, fixture.Home)).ToList())];
    }

    /// <summary>The people who want to come that a club of the given hidden skill adds a season over its first seasons, and where it finishes, on average.</summary>
    private static (double PeopleAdded, double Place) Archetype(int hiddenSkillCentis, int seasons, int runs, ulong seed)
    {
        var added = 0.0;
        var place = 0.0;

        for (var run = 0; run < runs; run++)
        {
            var result = Simulate(hiddenSkillCentis, seasons, seed + (ulong)(run * 7_919), weeklyReviews: true);

            added += FanRuleSet.WantToComeFor(result.FansAtSeasonEnd[^1]) - FanRuleSet.WantToComeFor(result.FansAtStart);
            place += result.Places.Average();
        }

        return (added / runs / seasons, place / runs);
    }

    [Fact]
    public void A_club_that_meets_what_the_fans_expect_stays_calm()
    {
        var moods = new List<int>();

        for (var run = 0; run < 24; run++)
        {
            moods.AddRange(Simulate(0, 8, 31_000UL + (ulong)(run * 7_919), weeklyReviews: true).MoodAfterEachMatch);
        }

        moods.Average().Should().BeInRange(4_500, 5_500, "meeting expectations averages Calm (40 to 60)");
    }

    /// <remarks>
    /// The plan aims at about 6,000 people a season for a club that beats what its fans expect, 60% of that for one that only meets it, and
    /// half of the meeting rate for one much worse than expected. The amounts were retuned on the tick engine's own curve (milestone 2),
    /// where the fans follow a club's real quality through its form and so are less surprised by it than in the sandbox: 6,090 / 63% / 49%
    /// over 200 runs. The bands below leave room for the run-to-run noise of 40 runs.
    /// </remarks>
    [Fact]
    public void Clubs_that_beat_meet_and_fall_short_of_what_the_fans_expect_grow_in_that_order()
    {
        var beats = Archetype(220, 3, 40, 4_000);
        var meets = Archetype(0, 3, 40, 6_000);
        var worse = Archetype(-200, 3, 40, 8_000);

        beats.Place.Should().BeInRange(2, 5, "beating expectations finishes about 3rd");
        meets.Place.Should().BeInRange(7.5, 11, "meeting expectations finishes mid-table");
        worse.Place.Should().BeInRange(13, 17, "much worse than expected finishes about 15th");
        beats.PeopleAdded.Should().BeInRange(5_400, 6_800, "a club that beats what its fans expect adds about 6,000 a season");
        (meets.PeopleAdded / beats.PeopleAdded).Should().BeInRange(0.52, 0.70, $"aim 0.60: meeting adds {meets.PeopleAdded:0} and beating {beats.PeopleAdded:0} people a season");
        (worse.PeopleAdded / meets.PeopleAdded).Should().BeInRange(0.40, 0.60, $"aim 0.50: much worse than expected adds {worse.PeopleAdded:0} people a season");
        worse.PeopleAdded.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_club_that_beats_what_the_fans_expect_needs_the_biggest_ground_after_seven_to_ten_seasons()
    {
        const int Runs = 40;
        const int Seasons = 15;
        var needed = new List<int>();

        for (var run = 0; run < Runs; run++)
        {
            var ends = Simulate(220, Seasons, 55_000UL + (ulong)(run * 7_919), weeklyReviews: true).FansAtSeasonEnd;
            var season = ends.ToList().FindIndex(fans => FanRuleSet.WantToComeFor(fans) >= 47_500);

            needed.Add(season < 0 ? Seasons + 1 : season + 1);
        }

        needed.Average().Should().BeInRange(7.0, 10.0, "until 47,500 people want to come, 95% of level 10's 50,000 places");
    }

    [Fact]
    public void A_season_of_simulated_results_never_takes_the_count_below_the_floor_or_the_mood_off_its_scale()
    {
        foreach (var skill in new[] { -400, -200, 0, 220, 600 })
        {
            var result = Simulate(skill, 6, 91_000UL + (ulong)skill, weeklyReviews: true);

            result.FansAtSeasonEnd.Should().OnlyContain(fans => fans >= FanRuleSet.FloorFansFor(Tier1));
            result.MoodAfterEachMatch.Should().OnlyContain(mood => mood >= 0 && mood <= FanRuleSet.MoodMaxBp);
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_seasons()
    {
        var first = Simulate(100, 3, 12_345, weeklyReviews: true);
        var second = Simulate(100, 3, 12_345, weeklyReviews: true);

        second.FansAtSeasonEnd.Should().Equal(first.FansAtSeasonEnd);
        second.MoodAfterEachMatch.Should().Equal(first.MoodAfterEachMatch);
    }
}
