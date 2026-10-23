using FluentAssertions;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.Fans;

/// <summary>
/// What the fans expect from a match (`FAN-3`, `FAN-4`): the curve, the form blend, the labels and the score they bet on.
/// </summary>
public sealed class FanRuleSetTests
{
    /// <summary>Every eighth hundredth of a point from -7.20 to +7.20, past either end of the curve.</summary>
    public static TheoryData<int> Gaps()
    {
        var gaps = new TheoryData<int>();

        for (var gap = -720; gap <= 720; gap += 8)
        {
            gaps.Add(gap);
        }

        return gaps;
    }

    [Fact]
    public void The_curve_is_read_straight_off_its_rows_at_a_row()
    {
        var even = FanRuleSet.ExpectationForGap(0, atHome: true);

        even.WinBp.Should().Be(4_547);
        even.DrawBp.Should().Be(2_798);
        even.LossBp.Should().Be(2_655);
        even.ExpectedPointsCentis.Should().Be(164, "3 x 45.47% + 27.98% = 1.6439 points, rounded to the nearest hundredth");
        even.Label.Should().Be(FanExpectationLabel.ShouldWin, "home advantage alone makes an even game one the home fans expect to win");

        var evenAway = FanRuleSet.ExpectationForGap(0, atHome: false);

        evenAway.WinBp.Should().Be(2_655);
        evenAway.LossBp.Should().Be(4_547);
        evenAway.Label.Should().Be(FanExpectationLabel.APointWouldDo);
    }

    [Fact]
    public void A_gap_between_two_rows_lies_between_them()
    {
        var below = FanRuleSet.ExpectationForGap(0, atHome: true);
        var between = FanRuleSet.ExpectationForGap(25, atHome: true);
        var above = FanRuleSet.ExpectationForGap(50, atHome: true);

        between.WinBp.Should().BeInRange(below.WinBp, above.WinBp).And.NotBe(below.WinBp).And.NotBe(above.WinBp);
        between.WinBp.Should().Be(4_759, "4,547 + (4,970 - 4,547) / 2, rounded half away from zero");
    }

    [Theory]
    [InlineData(600)]
    [InlineData(5_000)]
    public void A_gap_beyond_the_curve_reads_as_its_end(int gap)
    {
        FanRuleSet.ExpectationForGap(gap, atHome: true).Should().Be(FanRuleSet.ExpectationForGap(FanRuleSet.CurveLimitCentis, atHome: true));
        FanRuleSet.ExpectationForGap(-gap, atHome: true).Should().Be(FanRuleSet.ExpectationForGap(-FanRuleSet.CurveLimitCentis, atHome: true));
    }

    [Theory]
    [MemberData(nameof(Gaps))]
    public void The_three_chances_make_one_and_the_away_side_sees_the_mirror(int gap)
    {
        var home = FanRuleSet.ExpectationForGap(gap, atHome: true);
        var away = FanRuleSet.ExpectationForGap(gap, atHome: false);

        (home.WinBp + home.DrawBp + home.LossBp).Should().Be(10_000);
        (away.WinBp + away.DrawBp + away.LossBp).Should().Be(10_000);
        new[] { home.WinBp, home.DrawBp, home.LossBp }.Should().OnlyContain(chance => chance >= 0);
        away.WinBp.Should().Be(home.LossBp);
        away.LossBp.Should().Be(home.WinBp);
        away.DrawBp.Should().Be(home.DrawBp);
    }

    [Fact]
    public void A_stronger_side_never_expects_less()
    {
        var previous = FanRuleSet.ExpectationForGap(-700, atHome: true);

        for (var gap = -690; gap <= 700; gap += 10)
        {
            var current = FanRuleSet.ExpectationForGap(gap, atHome: true);

            current.WinBp.Should().BeGreaterThanOrEqualTo(previous.WinBp, $"gap {gap}");
            current.LossBp.Should().BeLessThanOrEqualTo(previous.LossBp, $"gap {gap}");
            current.ExpectedPointsCentis.Should().BeGreaterThanOrEqualTo(previous.ExpectedPointsCentis, $"gap {gap}");
            ((int)current.Label).Should().BeGreaterThanOrEqualTo((int)previous.Label, $"gap {gap}");
            previous = current;
        }
    }

    [Theory]
    [MemberData(nameof(Gaps))]
    public void The_score_the_fans_bet_on_never_contradicts_the_words(int gap)
    {
        foreach (var atHome in new[] { true, false })
        {
            var expectation = FanRuleSet.ExpectationForGap(gap, atHome);
            var scored = expectation.PredictedGoalsFor;
            var conceded = expectation.PredictedGoalsAgainst;

            switch (expectation.Label)
            {
                case >= FanExpectationLabel.ShouldWin:
                    scored.Should().BeGreaterThan(conceded, $"{expectation.Label} at {gap} (home {atHome})");
                    break;
                case FanExpectationLabel.DamageLimitation:
                    scored.Should().BeLessThan(conceded, $"{expectation.Label} at {gap} (home {atHome})");
                    break;
                case FanExpectationLabel.ToughGame or FanExpectationLabel.APointWouldDo:
                    scored.Should().BeLessThanOrEqualTo(conceded, $"{expectation.Label} at {gap} (home {atHome})");
                    break;
            }

            scored.Should().BeInRange(0, 7);
            conceded.Should().BeInRange(0, 7);
        }
    }

    [Theory]
    [InlineData(150, 100, FanExpectationLabel.ShouldWin, 1, 0)]
    [InlineData(110, 110, FanExpectationLabel.CouldGoEitherWay, 1, 1)]
    [InlineData(250, 60, FanExpectationLabel.ShouldHammerThem, 2, 0)]
    [InlineData(60, 250, FanExpectationLabel.DamageLimitation, 0, 2)]
    [InlineData(150, 100, FanExpectationLabel.DamageLimitation, 0, 1)]
    public void The_score_is_the_likeliest_one_the_label_allows(int goalsFor, int goalsAgainst, FanExpectationLabel label, int scored, int conceded)
    {
        FanRuleSet.PredictedScore(goalsFor, goalsAgainst, label).Should().Be((scored, conceded));
    }

    [Theory]
    [InlineData(0, FanExpectationLabel.DamageLimitation)]
    [InlineData(49, FanExpectationLabel.DamageLimitation)]
    [InlineData(50, FanExpectationLabel.ToughGame)]
    [InlineData(79, FanExpectationLabel.ToughGame)]
    [InlineData(80, FanExpectationLabel.APointWouldDo)]
    [InlineData(119, FanExpectationLabel.APointWouldDo)]
    [InlineData(120, FanExpectationLabel.CouldGoEitherWay)]
    [InlineData(159, FanExpectationLabel.CouldGoEitherWay)]
    [InlineData(160, FanExpectationLabel.ShouldWin)]
    [InlineData(199, FanExpectationLabel.ShouldWin)]
    [InlineData(200, FanExpectationLabel.ExpectAWin)]
    [InlineData(239, FanExpectationLabel.ExpectAWin)]
    [InlineData(240, FanExpectationLabel.ShouldHammerThem)]
    [InlineData(300, FanExpectationLabel.ShouldHammerThem)]
    public void The_label_follows_the_expected_points(int points, FanExpectationLabel label)
    {
        FanRuleSet.LabelFor(points).Should().Be(label);
    }

    [Fact]
    public void Every_label_has_words_and_a_code_that_reads_back()
    {
        foreach (var label in Enum.GetValues<FanExpectationLabel>())
        {
            FanExpectationLabels.Phrase(label).Should().NotBeNullOrWhiteSpace();
            FanExpectationLabels.FromCode(FanExpectationLabels.Code(label)).Should().Be(label);
        }

        FanExpectationLabels.Phrase(FanExpectationLabel.ShouldHammerThem).Should().Be("Should hammer them");
        FanExpectationLabels.FromCode("nonsense").Should().BeNull();
    }

    [Fact]
    public void Before_the_first_game_the_gap_is_the_squad_gap()
    {
        FanRuleSet.GapCentis(1_500, FanForm.None, 1_300, FanForm.None).Should().Be(200);

        // One side with games and one without: the side that has played less sets the weight, so form says nothing yet.
        FanRuleSet.GapCentis(1_500, new FanForm(10, 30), 1_300, FanForm.None).Should().Be(200);
    }

    [Fact]
    public void Form_counts_for_half_after_ten_games()
    {
        // 2.5 points a game against 0.5: a gap of 2.0 points a game is worth 2.00 / 0.22 = 9.09 attribute points; half of it with an even squad.
        var gap = FanRuleSet.GapCentis(1_400, new FanForm(10, 25), 1_400, new FanForm(10, 5));

        gap.Should().Be(454);
    }

    [Fact]
    public void Form_counts_for_more_as_the_season_goes_on()
    {
        var early = FanRuleSet.GapCentis(1_400, new FanForm(5, 12), 1_400, new FanForm(5, 3));
        var late = FanRuleSet.GapCentis(1_400, new FanForm(30, 72), 1_400, new FanForm(30, 18));

        // The same 1.8 points a game apart: a third of the weight after five games, three quarters after thirty.
        early.Should().BeLessThan(late);
        late.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_side_better_on_paper_and_in_form_is_expected_to_win_more_than_one_only_better_on_paper()
    {
        var onPaper = FanRuleSet.ExpectationFor(1_500, FanForm.None, 1_400, FanForm.None, atHome: true);
        var inForm = FanRuleSet.ExpectationFor(1_500, new FanForm(10, 26), 1_400, new FanForm(10, 8), atHome: true);

        inForm.ExpectedPointsCentis.Should().BeGreaterThan(onPaper.ExpectedPointsCentis);
    }

    [Fact]
    public void The_two_sides_of_a_fixture_see_the_same_fixture_from_opposite_ends()
    {
        var homeForm = new FanForm(12, 25);
        var awayForm = new FanForm(12, 14);

        var home = FanRuleSet.ExpectationFor(1_500, homeForm, 1_380, awayForm, atHome: true);
        var away = FanRuleSet.ExpectationFor(1_380, awayForm, 1_500, homeForm, atHome: false);

        away.WinBp.Should().Be(home.LossBp);
        away.LossBp.Should().Be(home.WinBp);
        away.DrawBp.Should().Be(home.DrawBp);
        away.PredictedGoalsFor.Should().BeLessThanOrEqualTo(home.PredictedGoalsFor);
    }

    [Fact]
    public void The_same_inputs_always_give_the_same_expectation()
    {
        FanRuleSet.ExpectationFor(1_437, new FanForm(9, 14), 1_522, new FanForm(9, 17), atHome: false)
            .Should().Be(FanRuleSet.ExpectationFor(1_437, new FanForm(9, 14), 1_522, new FanForm(9, 17), atHome: false));
    }

    [Fact]
    public void A_version_label_is_stamped_on_the_rules()
    {
        FanRuleSet.Version.Should().Be("fan-rules-v2");
    }
}
