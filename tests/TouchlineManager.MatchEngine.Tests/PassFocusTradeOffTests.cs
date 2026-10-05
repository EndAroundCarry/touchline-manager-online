using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// A pass focus has a price as well as a benefit (`INS-9`): it moves the unit ratings, and no option is a free
/// improvement on another.
/// </summary>
public sealed class PassFocusTradeOffTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    private static int Modifier(MatchUnit unit, MatchPassFocus focus) =>
        TacticalModifiers.For(unit, new MatchInstructionsV1 { PassFocus = focus }, Rules);

    [Theory]
    [InlineData(MatchUnit.BuildUp)]
    [InlineData(MatchUnit.Creation)]
    [InlineData(MatchUnit.Finishing)]
    [InlineData(MatchUnit.DefensivePressure)]
    [InlineData(MatchUnit.DefensiveShape)]
    [InlineData(MatchUnit.Goalkeeping)]
    public void No_preference_changes_nothing(MatchUnit unit) =>
        Modifier(unit, MatchPassFocus.Balanced).Should().Be(EngineRulesV2.Certain);

    [Fact]
    public void Playing_through_the_middle_builds_and_finishes_better_but_creates_less()
    {
        Modifier(MatchUnit.BuildUp, MatchPassFocus.Centre).Should().BeGreaterThan(EngineRulesV2.Certain);
        Modifier(MatchUnit.Finishing, MatchPassFocus.Centre).Should().BeGreaterThan(EngineRulesV2.Certain);
        Modifier(MatchUnit.DefensiveShape, MatchPassFocus.Centre).Should().BeGreaterThan(EngineRulesV2.Certain);
        Modifier(MatchUnit.Creation, MatchPassFocus.Centre).Should().BeLessThan(EngineRulesV2.Certain);
    }

    [Fact]
    public void Playing_down_the_wings_creates_more_but_builds_worse_and_shoots_worse_and_is_looser_at_the_back()
    {
        Modifier(MatchUnit.Creation, MatchPassFocus.Wings).Should().BeGreaterThan(EngineRulesV2.Certain);
        Modifier(MatchUnit.BuildUp, MatchPassFocus.Wings).Should().BeLessThan(EngineRulesV2.Certain);
        Modifier(MatchUnit.Finishing, MatchPassFocus.Wings).Should().BeLessThan(EngineRulesV2.Certain);
        Modifier(MatchUnit.DefensiveShape, MatchPassFocus.Wings).Should().BeLessThan(EngineRulesV2.Certain);
    }

    [Fact]
    public void The_centre_with_a_flank_sits_between_the_centre_and_the_wings_and_leaves_the_other_flank_thin()
    {
        foreach (var unit in new[] { MatchUnit.BuildUp, MatchUnit.Creation, MatchUnit.Finishing })
        {
            var pair = Modifier(unit, MatchPassFocus.CentreAndLeft);
            var centre = Modifier(unit, MatchPassFocus.Centre);
            var wings = Modifier(unit, MatchPassFocus.Wings);

            pair.Should().BeInRange(Math.Min(centre, wings), Math.Max(centre, wings), unit.ToString());
        }

        Modifier(MatchUnit.DefensiveShape, MatchPassFocus.CentreAndLeft).Should().BeLessThan(EngineRulesV2.Certain);
    }

    [Fact]
    public void A_left_focus_and_a_right_focus_cost_and_buy_the_same()
    {
        foreach (var unit in Enum.GetValues<MatchUnit>())
        {
            Modifier(unit, MatchPassFocus.CentreAndLeft).Should().Be(Modifier(unit, MatchPassFocus.CentreAndRight), unit.ToString());
        }
    }

    [Fact]
    public void No_option_is_better_than_balanced_in_every_unit()
    {
        foreach (var focus in new[] { MatchPassFocus.Centre, MatchPassFocus.CentreAndLeft, MatchPassFocus.CentreAndRight, MatchPassFocus.Wings })
        {
            var units = new[] { MatchUnit.BuildUp, MatchUnit.Creation, MatchUnit.Finishing, MatchUnit.DefensiveShape };

            units.Any(unit => Modifier(unit, focus) < EngineRulesV2.Certain)
                .Should().BeTrue($"{focus} must give something up");
            units.Any(unit => Modifier(unit, focus) > EngineRulesV2.Certain)
                .Should().BeTrue($"{focus} must buy something");
        }
    }

    [Fact]
    public void The_goalkeeper_and_the_press_are_not_moved_by_where_the_ball_goes()
    {
        foreach (var focus in Enum.GetValues<MatchPassFocus>())
        {
            Modifier(MatchUnit.Goalkeeping, focus).Should().Be(EngineRulesV2.Certain);
            Modifier(MatchUnit.DefensivePressure, focus).Should().Be(EngineRulesV2.Certain);
        }
    }

    [Fact]
    public void Stacked_on_the_most_extreme_instructions_it_still_stays_inside_the_bounds()
    {
        var extreme = new MatchInstructionsV1
        {
            Mentality = MatchMentality.Attacking,
            Tempo = MatchTempo.High,
            Passing = MatchPassingStyle.ShortPassing,
            Width = MatchWidth.Wide,
            Pressing = MatchPressing.HighPress,
            DefensiveLine = MatchDefensiveLine.High,
            Tackling = MatchTacklingStyle.Aggressive,
        };

        foreach (var focus in Enum.GetValues<MatchPassFocus>())
        {
            foreach (var unit in Enum.GetValues<MatchUnit>())
            {
                TacticalModifiers.For(unit, extreme with { PassFocus = focus }, Rules)
                    .Should().BeInRange(Rules.MinTacticalModifierBasisPoints, Rules.MaxTacticalModifierBasisPoints);
            }
        }
    }
}
