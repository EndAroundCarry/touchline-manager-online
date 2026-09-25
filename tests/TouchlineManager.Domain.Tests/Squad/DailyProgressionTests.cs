using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.Squad.Training;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The deterministic daily progression (`TRN-1`, `TRN-2`, `TRN-4`, `TRN-9`, `TRN-10`).
/// </summary>
/// <remarks>
/// The calculator is a pure function, so these tests need neither a clock nor a database. What they pin is
/// the contract the worker depends on: the same input reproduces the same day, development is bounded and
/// monotonic, the individual focus steers growth, and a partial day is carried rather than lost.
/// </remarks>
public sealed class DailyProgressionTests
{
    private static readonly Guid PlayerId = Guid.Parse("0192f000-0000-7000-8000-0000000000ff");
    private static readonly DateOnly Day = new(2026, 9, 25);

    [Fact]
    public void The_same_input_reproduces_the_same_day()
    {
        var first = DailyProgression.Advance(Input());
        var second = DailyProgression.Advance(Input());

        second.Should().Be(first, "the draw is seeded from the player, the day, and the version (TRN-9)");
    }

    [Fact]
    public void A_different_day_produces_a_different_development_chain()
    {
        var fromEarly = Run(Input(day: Day), days: 120);
        var fromLate = Run(Input(day: Day.AddDays(30)), days: 120);

        fromEarly.Should().NotEqual(fromLate, "the day is part of the draw's identity");
    }

    [Fact]
    public void Every_deviation_stays_inside_the_displayed_scale_and_basis_point_bounds()
    {
        var state = Input(condition: 9_900, fatigue: 9_900, morale: 100, sharpness: 9_950, remainder: 999);

        for (var day = 0; day < 365; day++)
        {
            var outcome = DailyProgression.Advance(state with { Day = Day.AddDays(day) });

            outcome.Attributes.Values.Should().OnlyContain(
                value => value >= WorldRuleSet.AttributeMin && value <= WorldRuleSet.AttributeMax,
                "TRN-4");

            outcome.ConditionBp.Should().BeInRange(
                WorldRuleSet.StateBasisPointsMin, WorldRuleSet.StateBasisPointsMax, "TRN-5");
            outcome.FatigueBp.Should().BeInRange(
                WorldRuleSet.StateBasisPointsMin, WorldRuleSet.StateBasisPointsMax, "TRN-6");
            outcome.MoraleBp.Should().BeInRange(
                WorldRuleSet.StateBasisPointsMin, WorldRuleSet.StateBasisPointsMax, "TRN-7");
            outcome.MatchSharpnessBp.Should().BeInRange(
                WorldRuleSet.StateBasisPointsMin, WorldRuleSet.StateBasisPointsMax);
            outcome.DevelopmentRemainder.Should().BeInRange(0, DailyProgression.DevelopmentBasis - 1, "TRN-10");

            state = state with
            {
                Attributes = outcome.Attributes,
                ConditionBp = outcome.ConditionBp,
                FatigueBp = outcome.FatigueBp,
                MoraleBp = outcome.MoraleBp,
                MatchSharpnessBp = outcome.MatchSharpnessBp,
                DevelopmentRemainder = outcome.DevelopmentRemainder,
            };
        }
    }

    [Fact]
    public void Training_never_lowers_an_attribute()
    {
        var state = Input(focus: TrainingFocus.Balanced, age: 19);

        for (var day = 0; day < 200; day++)
        {
            var outcome = DailyProgression.Advance(state with { Day = Day.AddDays(day) });

            for (var index = 0; index < AttributeNames.Count; index++)
            {
                outcome.Attributes.Values[index].Should().BeGreaterThanOrEqualTo(
                    state.Attributes.Values[index],
                    "training develops or maintains, it does not regress (TRN-4)");
            }

            state = state with { Attributes = outcome.Attributes };
        }
    }

    [Fact]
    public void A_focused_player_develops_that_family_and_leaves_the_others_alone()
    {
        var start = Input(focus: TrainingFocus.Fitness);
        var end = Run(start, days: 400);

        foreach (var name in AttributeNames.All.Where(name => AttributeNames.FamilyOf(name) != AttributeFamily.Physical))
        {
            end[(int)name].Should().Be(start.Attributes.Values[(int)name], $"{name} is outside the emphasised family");
        }

        AttributeNames.All
            .Where(name => AttributeNames.FamilyOf(name) == AttributeFamily.Physical)
            .Should().Contain(name => end[(int)name] > start.Attributes.Values[(int)name],
                "the emphasised family develops (TRN-1)");
    }

    [Fact]
    public void An_individual_focus_steers_development_away_from_the_team_plan()
    {
        // Balanced would develop every family; an individual technical focus must narrow it to technical.
        var start = Input(focus: TrainingFocus.Balanced, individual: AttributeFamily.Technical);
        var end = Run(start, days: 400);

        foreach (var name in AttributeNames.All.Where(name => AttributeNames.FamilyOf(name) != AttributeFamily.Technical))
        {
            end[(int)name].Should().Be(start.Attributes.Values[(int)name], $"{name} is outside the individual focus");
        }

        AttributeNames.All
            .Where(name => AttributeNames.FamilyOf(name) == AttributeFamily.Technical)
            .Should().Contain(name => end[(int)name] > start.Attributes.Values[(int)name],
                "the individual focus develops its family (TRN-2)");
    }

    [Fact]
    public void Development_lands_on_more_than_one_attribute_of_a_family_over_a_season()
    {
        // A whole season is long enough that the family develops as a group rather than one attribute
        // carrying every point, which is what a manager reading the profile would expect to see.
        var start = Input(focus: TrainingFocus.Fitness);
        var end = Run(start, days: 1_000);

        var physical = AttributeNames.All
            .Where(name => AttributeNames.FamilyOf(name) == AttributeFamily.Physical)
            .Count(name => end[(int)name] > start.Attributes.Values[(int)name]);

        physical.Should().BeGreaterThan(1, "development spreads across the emphasised family");
    }

    [Fact]
    public void A_recovery_focus_develops_nobody_and_clears_more_fatigue()
    {
        var recovery = DailyProgression.Advance(Input(focus: TrainingFocus.Recovery));
        var balanced = DailyProgression.Advance(Input(focus: TrainingFocus.Balanced));

        recovery.Attributes.Should().Be(Input().Attributes, "recovery develops no family");
        recovery.FatigueBp.Should().BeLessThan(balanced.FatigueBp, "recovery clears more fatigue (TRN-1)");
    }

    [Fact]
    public void More_intensity_produces_more_development_and_more_fatigue()
    {
        var light = Run(Input(intensity: TrainingIntensity.Light), days: 200);
        var intense = Run(Input(intensity: TrainingIntensity.Intense), days: 200);

        intense.Sum().Should().BeGreaterThan(light.Sum(), "intense training develops faster");

        var rested = Input(intensity: TrainingIntensity.Light);
        var hard = Input(intensity: TrainingIntensity.Intense);

        DailyProgression.Advance(hard).FatigueBp.Should().BeGreaterThan(
            DailyProgression.Advance(rested).FatigueBp,
            "intensity costs fatigue — no tactic multiplies a rating without a counter-cost (§3.8)");
    }

    [Fact]
    public void A_player_beyond_the_development_age_maintains()
    {
        var start = Input(age: DailyProgression.DevelopmentAgeLimit);
        var end = Run(start, days: 120);

        end.Should().Equal(start.Attributes.Values, "past the age limit a player trains to maintain, not to grow");
    }

    [Fact]
    public void A_player_already_at_their_potential_cannot_grow_past_it()
    {
        // Every attribute starts at the ceiling, so there is no room and no eligibility.
        var start = Input(values: [.. Enumerable.Repeat(18, AttributeNames.Count)], potential: 18);
        var end = Run(start, days: 200);

        end.Should().OnlyContain(value => value <= 18, "the hidden potential is the ceiling (TRN-9)");
    }

    [Fact]
    public void A_potential_outside_the_scale_is_a_programming_error()
    {
        var act = () => DailyProgression.Advance(Input(potential: 0));

        act.Should().Throw<ArgumentOutOfRangeException>("TRN-4");
    }

    /// <summary>Runs the calculator for a number of days, carrying each day's output into the next.</summary>
    private static int[] Run(DailyProgressionInput start, int days)
    {
        var state = start;
        var attributes = start.Attributes.Values.ToArray();

        for (var day = 0; day < days; day++)
        {
            var outcome = DailyProgression.Advance(state with { Day = start.Day.AddDays(day) });

            attributes = [.. outcome.Attributes.Values];
            state = state with
            {
                Attributes = outcome.Attributes,
                ConditionBp = outcome.ConditionBp,
                FatigueBp = outcome.FatigueBp,
                MoraleBp = outcome.MoraleBp,
                MatchSharpnessBp = outcome.MatchSharpnessBp,
                DevelopmentRemainder = outcome.DevelopmentRemainder,
            };
        }

        return attributes;
    }

    private static DailyProgressionInput Input(
        int[]? values = null,
        int potential = 20,
        int age = 18,
        TrainingFocus focus = TrainingFocus.Fitness,
        TrainingIntensity intensity = TrainingIntensity.Normal,
        AttributeFamily? individual = null,
        int condition = 8_000,
        int fatigue = 5_000,
        int morale = 5_000,
        int sharpness = 5_000,
        int remainder = 0,
        DateOnly? day = null) =>
        new(
            PlayerId,
            age,
            PlayerAttributeSet.FromValues(values ?? [.. Enumerable.Repeat(10, AttributeNames.Count)]),
            potential,
            focus,
            intensity,
            individual,
            condition,
            fatigue,
            morale,
            sharpness,
            remainder,
            day ?? Day);
}
