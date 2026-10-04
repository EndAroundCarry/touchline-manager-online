using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.Squad.Training;
using Xunit.Abstractions;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The deterministic daily progression, `training-v2` (`TRN-1`, `TRN-4`, `TRN-9`, `TRN-10`, `TRN-14`…`TRN-16`).
/// </summary>
/// <remarks>
/// The calculator is a pure function, so these tests need neither a clock nor a database. What they pin is
/// the contract the worker depends on: the same input reproduces the same day, development is bounded and
/// spread over the programme's attributes, ageing and aptitude shape the pace, and a partial day is carried
/// rather than lost.
/// </remarks>
public sealed class DailyProgressionTests
{
    private static readonly Guid PlayerId = Guid.Parse("0192f000-0000-7000-8000-0000000000ff");
    private static readonly DateOnly Day = new(2026, 9, 25);

    private readonly ITestOutputHelper _output;

    public DailyProgressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void The_version_is_training_v2()
    {
        DailyProgression.Version.Should().Be("training-v2");
    }

    [Fact]
    public void The_same_input_reproduces_the_same_day()
    {
        var first = DailyProgression.Advance(Input());
        var second = DailyProgression.Advance(Input());

        second.Should().BeEquivalentTo(first, "the draw is seeded from the player, the day, and the version (TRN-9)");
    }

    [Fact]
    public void A_different_day_produces_a_different_development_chain()
    {
        var fromEarly = Simulate(Input(day: Day), days: 120).Attributes.Values;
        var fromLate = Simulate(Input(day: Day.AddDays(30)), days: 120).Attributes.Values;

        fromEarly.Should().NotEqual(fromLate, "the day is part of the draw's identity");
    }

    [Fact]
    public void Every_value_stays_inside_the_scale_and_basis_point_bounds_over_a_forty_year_career()
    {
        var state = Input(age: 16, condition: 9_900, fatigue: 9_900, morale: 100, sharpness: 9_950, remainder: 999);

        for (var day = 0; day < 40 * TrainingAgeCurve.TrainingDaysPerSeason; day++)
        {
            var outcome = DailyProgression.Advance(state with
            {
                Day = Day.AddDays(day),
                Age = 16 + (day / TrainingAgeCurve.TrainingDaysPerSeason),
            });

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
            outcome.DeclineRemainder.Should().BeInRange(0, DailyProgression.DevelopmentBasis - 1, "TRN-16");

            state = Carry(state, outcome);
        }
    }

    [Fact]
    public void A_programme_develops_its_own_attributes_and_leaves_the_others_alone()
    {
        var start = Input(programme: TrainingProgramme.Forward);
        var end = Simulate(start, days: 400).Attributes;
        var trained = TrainingProgrammes.Of(TrainingProgramme.Forward);

        foreach (var name in AttributeNames.All.Where(name => trained.WeightOf(name) == 0))
        {
            end.ValueOf(name).Should().Be(start.Attributes.ValueOf(name), $"{name} is not in the forward programme");
        }

        trained.Attributes.Should().Contain(
            entry => end.ValueOf(entry.Attribute) > start.Attributes.ValueOf(entry.Attribute),
            "the programme develops its attributes (TRN-1)");
    }

    [Fact]
    public void Development_spreads_over_the_programmes_attributes_in_proportion_to_their_weights()
    {
        var start = Input(programme: TrainingProgramme.Forward, age: 17, values: Enumerable.Repeat(4, AttributeNames.Count).ToArray());
        var end = Simulate(start, days: 6 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false).Attributes;
        var forward = TrainingProgrammes.Of(TrainingProgramme.Forward);

        var core = forward.Attributes.Where(entry => entry.Weight == TrainingProgrammes.CoreWeight)
            .Average(entry => end.ValueOf(entry.Attribute));
        var supporting = forward.Attributes.Where(entry => entry.Weight == TrainingProgrammes.SupportingWeight)
            .Average(entry => end.ValueOf(entry.Attribute));

        core.Should().BeGreaterThan(supporting, "a core attribute takes a larger share of the day's points");
        forward.Attributes.Should().OnlyContain(
            entry => end.ValueOf(entry.Attribute) > 4,
            "even a supporting attribute develops over several seasons");
    }

    [Fact]
    public void The_recovery_programme_develops_nobody_and_clears_more_fatigue()
    {
        var recovery = DailyProgression.Advance(Input(programme: TrainingProgramme.Recovery));
        var forward = DailyProgression.Advance(Input(programme: TrainingProgramme.Forward));

        recovery.Attributes.Should().Be(Input().Attributes, "recovery trains no attribute");
        recovery.DevelopmentMilli.Should().Be(0);
        recovery.FatigueBp.Should().BeLessThan(forward.FatigueBp, "recovery clears more fatigue (TRN-1)");
    }

    [Fact]
    public void More_intensity_produces_more_development_and_more_fatigue()
    {
        var light = DailyProgression.Advance(Input(intensity: TrainingIntensity.Light));
        var normal = DailyProgression.Advance(Input(intensity: TrainingIntensity.Normal));
        var intense = DailyProgression.Advance(Input(intensity: TrainingIntensity.Intense));

        light.DevelopmentMilli.Should().BeLessThan(normal.DevelopmentMilli);
        normal.DevelopmentMilli.Should().BeLessThan(intense.DevelopmentMilli, "intense training develops faster");

        intense.FatigueBp.Should().BeGreaterThan(
            normal.FatigueBp,
            "intensity costs fatigue — no tactic multiplies a rating without a counter-cost (§3.8)");
        normal.FatigueBp.Should().BeGreaterThan(light.FatigueBp);
    }

    [Fact]
    public void A_tired_player_develops_less()
    {
        var rested = DailyProgression.Advance(Input(fatigue: 2_000));
        var tired = DailyProgression.Advance(Input(fatigue: 9_500));
        var exhausted = DailyProgression.Advance(Input(fatigue: 10_000));

        tired.DevelopmentMilli.Should().BeLessThan(rested.DevelopmentMilli);
        exhausted.DevelopmentMilli.Should().BeLessThan(tired.DevelopmentMilli);
        exhausted.DevelopmentMilli.Should().BeGreaterThan(0, "the penalty is soft, not a stop");
    }

    [Fact]
    public void Intense_training_pays_now_and_leaves_a_tired_player_more_tired_tomorrow()
    {
        // From a player already carrying match load, a fortnight of intense training out-develops normal
        // but leaves them more fatigued, which then costs development.
        var tired = Input(fatigue: 8_000);
        var normal = Simulate(tired with { Intensity = TrainingIntensity.Normal }, days: 10, ageStep: false);
        var intense = Simulate(tired with { Intensity = TrainingIntensity.Intense }, days: 10, ageStep: false);

        intense.Attributes.Values.Sum().Should().BeGreaterThanOrEqualTo(normal.Attributes.Values.Sum());
        intense.FatigueBp.Should().BeGreaterThan(normal.FatigueBp);
    }

    [Fact]
    public void A_high_aptitude_player_develops_faster_than_a_low_one()
    {
        var quick = DailyProgression.Advance(Input(aptitude: 1_400));
        var slow = DailyProgression.Advance(Input(aptitude: 600));

        quick.DevelopmentMilli.Should().BeGreaterThan(slow.DevelopmentMilli);
    }

    [Fact]
    public void A_younger_player_develops_faster_than_an_older_one()
    {
        var at17 = DailyProgression.Advance(Input(age: 17)).DevelopmentMilli;
        var at24 = DailyProgression.Advance(Input(age: 24)).DevelopmentMilli;
        var at29 = DailyProgression.Advance(Input(age: 29)).DevelopmentMilli;

        at17.Should().BeGreaterThan(at24);
        at24.Should().BeGreaterThan(at29);
    }

    [Fact]
    public void A_veteran_still_improves_a_trained_skill_below_potential_just_very_slowly()
    {
        var start = Input(age: 35, programme: TrainingProgramme.Forward);
        var perDay = DailyProgression.Advance(start).DevelopmentMilli;

        perDay.Should().BeGreaterThan(0);

        var earned = 0;
        var state = start;

        for (var day = 0; day < TrainingAgeCurve.TrainingDaysPerSeason * 5; day++)
        {
            var outcome = DailyProgression.Advance(state with { Day = Day.AddDays(day) });

            earned += outcome.DevelopmentMilli;
            state = Carry(state, outcome);
        }

        earned.Should().BeGreaterThan(DailyProgression.DevelopmentBasis, "five seasons earn at least a point at 35");
        earned.Should().BeLessThan(
            DailyProgression.Advance(Input(age: 17)).DevelopmentMilli * TrainingAgeCurve.TrainingDaysPerSeason,
            "but far less than a season at 17");
    }

    [Fact]
    public void Total_growth_does_not_depend_on_how_many_attributes_the_programme_covers()
    {
        var forward = DailyProgression.Advance(Input(programme: TrainingProgramme.Forward)).DevelopmentMilli;
        var physical = DailyProgression.Advance(Input(programme: TrainingProgramme.Physical)).DevelopmentMilli;
        var mental = DailyProgression.Advance(Input(programme: TrainingProgramme.Mental)).DevelopmentMilli;

        TrainingProgrammes.Of(TrainingProgramme.Forward).Attributes.Count
            .Should().NotBe(TrainingProgrammes.Of(TrainingProgramme.Physical).Attributes.Count);

        physical.Should().Be(forward);
        mental.Should().Be(forward, "the budget is constant, so a narrow programme concentrates it");
    }

    [Fact]
    public void An_attribute_never_grows_past_the_players_potential()
    {
        var start = Input(
            values: [.. Enumerable.Repeat(11, AttributeNames.Count)],
            potential: 12,
            age: 17);
        var end = Simulate(start, days: 6 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false).Attributes;

        end.Values.Should().OnlyContain(value => value <= 12, "the hidden potential is the ceiling (TRN-9)");
        TrainingProgrammes.Of(TrainingProgramme.Forward).Attributes.Should().OnlyContain(
            entry => end.ValueOf(entry.Attribute) == 12,
            "a young trained player reaches a close ceiling");
    }

    [Fact]
    public void A_player_already_at_their_potential_cannot_grow_past_it()
    {
        var start = Input(values: [.. Enumerable.Repeat(18, AttributeNames.Count)], potential: 18);
        var outcome = DailyProgression.Advance(start);

        outcome.DevelopmentMilli.Should().Be(0);
        Simulate(start, days: 200, ageStep: false).Attributes.Values
            .Should().OnlyContain(value => value <= 18, "the hidden potential is the ceiling (TRN-9)");
    }

    [Fact]
    public void Ageing_costs_physical_most_then_technical_and_leaves_mental_alone_at_thirty()
    {
        // No programme, so nothing grows and nothing is shielded: the net change is pure decline.
        var start = Input(programme: TrainingProgramme.Recovery, age: 30);
        var end = Simulate(start, days: 4 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false).Attributes;

        int Net(AttributeFamily family) => AttributeNames.All
            .Where(name => AttributeNames.FamilyOf(name) == family)
            .Sum(name => end.ValueOf(name) - start.Attributes.ValueOf(name));

        var physical = Net(AttributeFamily.Physical);
        var technical = Net(AttributeFamily.Technical);
        var mental = Net(AttributeFamily.Mental);
        var goalkeeping = Net(AttributeFamily.Goalkeeping);

        physical.Should().BeLessThan(technical);
        technical.Should().BeLessThan(mental);
        mental.Should().Be(0, "mental attributes hold until work rate starts to fall at 31");
        goalkeeping.Should().Be(0, "goalkeeping attributes hold at 30");
    }

    [Fact]
    public void Training_an_attribute_shields_it_from_decline()
    {
        var unshielded = DailyProgression.Advance(Input(programme: TrainingProgramme.Recovery, age: 31));
        var shielded = DailyProgression.Advance(Input(programme: TrainingProgramme.Physical, age: 31));

        unshielded.DeclineMilli.Should().BeGreaterThan(0);
        shielded.DeclineMilli.Should().BeLessThan(unshielded.DeclineMilli, "training slows ageing");
    }

    [Fact]
    public void Decline_never_takes_an_attribute_below_one()
    {
        var start = Input(
            values: [.. Enumerable.Repeat(1, AttributeNames.Count)],
            programme: TrainingProgramme.Recovery,
            age: 40);
        var end = Simulate(start, days: 400, ageStep: false).Attributes;

        end.Values.Should().OnlyContain(value => value == WorldRuleSet.AttributeMin);
    }

    [Fact]
    public void A_young_player_does_not_decline()
    {
        var outcome = DailyProgression.Advance(Input(age: 22));

        outcome.DeclineMilli.Should().Be(0);
        outcome.DeclineRemainder.Should().Be(0);
    }

    [Fact]
    public void The_outcome_lists_exactly_the_attributes_that_moved()
    {
        var start = Input(programme: TrainingProgramme.Forward);
        var state = start;

        for (var day = 0; day < 120; day++)
        {
            var outcome = DailyProgression.Advance(state with { Day = Day.AddDays(day) });

            foreach (var name in AttributeNames.All)
            {
                var delta = outcome.Attributes.ValueOf(name) - state.Attributes.ValueOf(name);
                var listed = outcome.AttributeChanges.SingleOrDefault(change => change.Attribute == name)?.Delta ?? 0;

                listed.Should().Be(delta, $"{name} on day {day}");
            }

            state = Carry(state, outcome);
        }
    }

    [Fact]
    public void A_negative_remainder_or_out_of_scale_potential_is_a_programming_error()
    {
        var potential = () => DailyProgression.Advance(Input(potential: 0));
        var development = () => DailyProgression.Advance(Input(remainder: -1));
        var decline = () => DailyProgression.Advance(Input(declineRemainder: -1));

        potential.Should().Throw<ArgumentOutOfRangeException>("TRN-4");
        development.Should().Throw<ArgumentOutOfRangeException>("TRN-10");
        decline.Should().Throw<ArgumentOutOfRangeException>("TRN-16");
    }

    [Fact]
    public void Calibration_an_average_young_player_closes_most_of_the_gap_to_potential_by_twenty_four()
    {
        var average = Calibrate(aptitude: TrainingAptitude.Neutral);
        var slow = Calibrate(aptitude: TrainingAptitude.MinPermille);
        var quick = Calibrate(aptitude: TrainingAptitude.MaxPermille);

        _output.WriteLine($"gap to potential at 24 (trained attributes, mean): slow {slow:F2}, average {average:F2}, quick {quick:F2}");

        average.Should().BeLessThanOrEqualTo(1.0, "an average learner is within a point of potential by about 24");
        slow.Should().BeGreaterThan(average + 1.5, "a low-aptitude learner is clearly slower");
        quick.Should().BeLessThan(0.5, "a quick learner gets there early");
    }

    /// <summary>
    /// Runs a 17-year-old who starts seven points under a potential of 18, through seven seasons of normal
    /// training, and returns how far the trained attributes still are from potential.
    /// </summary>
    private static double Calibrate(int aptitude)
    {
        var start = Input(
            values: [.. Enumerable.Repeat(11, AttributeNames.Count)],
            potential: 18,
            age: 17,
            aptitude: aptitude,
            programme: TrainingProgramme.Forward);

        var end = Simulate(start, days: 7 * TrainingAgeCurve.TrainingDaysPerSeason).Attributes;

        return TrainingProgrammes.Of(TrainingProgramme.Forward).Attributes.Average(entry => 18 - end.ValueOf(entry.Attribute));
    }

    /// <summary>Runs the calculator for a number of days, carrying each day's output into the next.</summary>
    private static DailyProgressionInput Simulate(DailyProgressionInput start, int days, bool ageStep = true)
    {
        var state = start;

        for (var day = 0; day < days; day++)
        {
            var outcome = DailyProgression.Advance(state with
            {
                Day = start.Day.AddDays(day),
                Age = ageStep ? start.Age + (day / TrainingAgeCurve.TrainingDaysPerSeason) : start.Age,
            });

            state = Carry(state, outcome);
        }

        return state;
    }

    private static DailyProgressionInput Carry(DailyProgressionInput state, DailyProgressionOutcome outcome) =>
        state with
        {
            Attributes = outcome.Attributes,
            ConditionBp = outcome.ConditionBp,
            FatigueBp = outcome.FatigueBp,
            MoraleBp = outcome.MoraleBp,
            MatchSharpnessBp = outcome.MatchSharpnessBp,
            DevelopmentRemainder = outcome.DevelopmentRemainder,
            DeclineRemainder = outcome.DeclineRemainder,
        };

    private static DailyProgressionInput Input(
        int[]? values = null,
        int potential = 20,
        int age = 18,
        int aptitude = TrainingAptitude.Neutral,
        TrainingProgramme programme = TrainingProgramme.Forward,
        TrainingIntensity intensity = TrainingIntensity.Normal,
        int condition = 8_000,
        int fatigue = 5_000,
        int morale = 5_000,
        int sharpness = 5_000,
        int remainder = 0,
        int declineRemainder = 0,
        DateOnly? day = null) =>
        new(
            PlayerId,
            age,
            PlayerAttributeSet.FromValues(values ?? [.. Enumerable.Repeat(10, AttributeNames.Count)]),
            potential,
            aptitude,
            programme,
            intensity,
            condition,
            fatigue,
            morale,
            sharpness,
            remainder,
            declineRemainder,
            day ?? Day);
}
