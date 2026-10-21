using FluentAssertions;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Training;
using Xunit.Abstractions;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>
/// The deterministic daily progression, `training-v4` (`TRN-1`, `TRN-4`, `TRN-9`, `TRN-10`, `TRN-14`…`TRN-16`).
/// </summary>
/// <remarks>
/// The calculator is a pure function, so these tests need neither a clock nor a database. What they pin is
/// the contract the worker depends on: the same input reproduces the same day, every attribute a player
/// develops grows, the programme takes the bigger share, development stops at the limit his potential and
/// position set, ageing and aptitude shape the pace, and each attribute carries its own partial progress
/// rather than losing it.
/// </remarks>
public sealed class DailyProgressionTests
{
    private static readonly Guid PlayerId = Guid.Parse("0192f000-0000-7000-8000-0000000000ff");
    private static readonly DateOnly Day = new(2026, 9, 25);

    private readonly ITestOutputHelper _output;

    public DailyProgressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void The_version_is_training_v4()
    {
        DailyProgression.Version.Should().Be("training-v4");
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
        // The jitter on each day's budget is what the day seeds, and it shows in the carried progress long
        // before it moves a whole point.
        var fromEarly = Simulate(Input(day: Day), days: 120).AttributeProgress;
        var fromLate = Simulate(Input(day: Day.AddDays(30)), days: 120).AttributeProgress;

        fromEarly.Should().NotEqual(fromLate, "the day is part of the draw's identity");
    }

    [Fact]
    public void Every_value_stays_inside_the_scale_and_basis_point_bounds_over_a_forty_year_career()
    {
        var state = Input(age: 16, condition: 9_900, fatigue: 9_900, morale: 100, sharpness: 9_950, progress: Progress(999_999));

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
            AttributeProgress.IsValid(outcome.AttributeProgress).Should().BeTrue(
                "every attribute's progress stays inside one point (TRN-10, TRN-16)");

            state = Carry(state, outcome);
        }
    }

    [Fact]
    public void A_programme_is_a_focus_so_its_attributes_grow_faster_than_the_rest()
    {
        var start = Input(programme: TrainingProgramme.Forward, age: 17, values: Enumerable.Repeat(8, AttributeNames.Count).ToArray());
        var end = Simulate(start, days: 2 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false).Attributes;
        var forward = TrainingProgrammes.Of(TrainingProgramme.Forward);
        var others = DevelopmentLimit.DevelopingAttributes(PlayerPosition.Striker).Where(name => forward.WeightOf(name) == 0).ToList();

        double Gain(IEnumerable<AttributeName> names) => names.Average(name => end.ValueOf(name) - start.Attributes.ValueOf(name));

        Gain(forward.Attributes.Select(entry => entry.Attribute)).Should().BeGreaterThan(
            Gain(others),
            "the programme takes the bigger share of the day (TRN-1, TRN-10)");
        others.Should().Contain(name => end.ValueOf(name) > 8, "the rest of the player develops too");
    }

    [Fact]
    public void Every_developing_attribute_of_a_youth_grows_within_two_seasons()
    {
        var start = Input(age: 17, potential: 20, values: Enumerable.Repeat(6, AttributeNames.Count).ToArray());
        var end = Simulate(start, days: 2 * TrainingAgeCurve.TrainingDaysPerSeason).Attributes;

        foreach (var name in DevelopmentLimit.DevelopingAttributes(PlayerPosition.Striker))
        {
            end.ValueOf(name).Should().BeGreaterThan(6, $"{name} is developed whatever the programme (TRN-10)");
        }
    }

    [Fact]
    public void An_outfielders_goalkeeping_attributes_never_grow()
    {
        var start = Input(age: 17, values: Enumerable.Repeat(6, AttributeNames.Count).ToArray());
        var end = Simulate(start, days: 6 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false).Attributes;

        foreach (var name in AttributeNames.All.Where(name => AttributeNames.FamilyOf(name) == AttributeFamily.Goalkeeping))
        {
            end.ValueOf(name).Should().Be(6, $"{name} is a goalkeeper's skill (TRN-4)");
        }
    }

    [Fact]
    public void A_goalkeeper_develops_all_twenty_eight_attributes()
    {
        var start = Input(
            age: 17,
            position: PlayerPosition.Goalkeeper,
            programme: TrainingProgramme.Goalkeeper,
            values: Enumerable.Repeat(6, AttributeNames.Count).ToArray());
        var end = Simulate(start, days: 3 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false).Attributes;

        DevelopmentLimit.DevelopingAttributes(PlayerPosition.Goalkeeper).Should().HaveCount(AttributeNames.Count);

        foreach (var name in AttributeNames.All)
        {
            end.ValueOf(name).Should().BeGreaterThan(6, $"a keeper's {name} feeds distribution and reading the game (TRN-4)");
        }
    }

    [Fact]
    public void A_narrower_programme_grows_its_attributes_faster()
    {
        var values = Enumerable.Repeat(6, AttributeNames.Count).ToArray();
        var narrow = Simulate(Input(age: 17, programme: TrainingProgramme.Physical, values: values), 2 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false);
        var wide = Simulate(Input(age: 17, programme: TrainingProgramme.Forward, values: values), 2 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false);

        double Gain(DailyProgressionInput end, TrainingProgramme programme) => TrainingProgrammes.Of(programme).Attributes
            .Average(entry => end.Attributes.ValueOf(entry.Attribute) - 6);

        Gain(narrow, TrainingProgramme.Physical).Should().BeGreaterThan(
            Gain(wide, TrainingProgramme.Forward),
            "the same budget is concentrated on fewer skills (TRN-10)");
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
    public void No_attribute_passes_its_limit_and_the_sum_never_passes_the_cap_through_development()
    {
        var position = PlayerPosition.Striker;
        var potential = 15;
        var limits = DevelopmentLimit.Limits(position, potential);
        var cap = DevelopmentLimit.CapSum(position, potential);
        var state = Input(
            potential: potential,
            age: 17,
            values: [.. AttributeNames.All.Select(name => Math.Max(1, DevelopmentLimit.FormulaValue(position, potential, name) - 7))]);

        for (var day = 0; day < 9 * TrainingAgeCurve.TrainingDaysPerSeason; day++)
        {
            var outcome = DailyProgression.Advance(state with
            {
                Day = Day.AddDays(day),
                Age = 17 + (day / TrainingAgeCurve.TrainingDaysPerSeason),
            });

            foreach (var name in DevelopmentLimit.DevelopingAttributes(position))
            {
                outcome.Attributes.ValueOf(name).Should().BeLessThanOrEqualTo(limits[(int)name], $"{name}'s own limit, day {day} (TRN-4)");
            }

            DevelopmentLimit.DevelopingAttributes(position).Sum(name => outcome.Attributes.ValueOf(name))
                .Should().BeLessThanOrEqualTo(cap, $"the overall limit, day {day} (TRN-4)");

            state = Carry(state, outcome);
        }
    }

    [Fact]
    public void A_player_at_his_cap_takes_no_development_and_holds_no_progress()
    {
        var start = Input(
            values: [.. AttributeNames.All.Select(name => DevelopmentLimit.FormulaValue(PlayerPosition.Striker, 15, name))],
            potential: 15,
            progress: Progress(500_000));
        var outcome = DailyProgression.Advance(start);

        outcome.DevelopmentMilli.Should().Be(0);
        outcome.Attributes.Should().Be(start.Attributes);
        outcome.AttributeProgress[(int)AttributeName.Finishing].Should().Be(0, "progress towards a point he cannot gain is dropped");
    }

    [Fact]
    public void An_attribute_may_pass_its_formula_value_by_the_headroom_while_the_player_is_under_his_cap()
    {
        var position = PlayerPosition.Striker;
        const int potential = 10;
        var formula = DevelopmentLimit.FormulaValue(position, potential, AttributeName.Finishing);
        var values = AttributeNames.All.Select(name => DevelopmentLimit.FormulaValue(position, potential, name) - 3).ToArray();

        values[(int)AttributeName.Finishing] = formula;
        formula.Should().BeLessThan(WorldRuleSet.AttributeMax, "the headroom is not clamped away");

        var end = Simulate(Input(values: values, potential: potential, age: 17), 4 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false).Attributes;

        end.Finishing.Should().Be(formula + DevelopmentLimit.CeilingHeadroom, "a programme may tilt a player by the headroom, no more");
    }

    [Fact]
    public void A_potential_twenty_youth_reaches_twenty_in_his_key_attributes()
    {
        var start = Input(age: 17, potential: 20, values: Enumerable.Repeat(8, AttributeNames.Count).ToArray());
        var end = Simulate(start, days: 9 * TrainingAgeCurve.TrainingDaysPerSeason).Attributes;

        end.Finishing.Should().Be(20);
        end.Composure.Should().Be(20);
    }

    [Fact]
    public void Ageing_costs_physical_most_then_technical_and_leaves_mental_alone_at_thirty()
    {
        // No programme, so nothing grows and nothing is shielded: the net change is pure decline.
        var start = Input(programme: TrainingProgramme.Recovery, age: 30);
        // Six seasons at a fixed age: each technical attribute loses a fifth of a point a season, so it takes
        // five seasons before the first of them is down a whole point.
        var end = Simulate(start, days: 6 * TrainingAgeCurve.TrainingDaysPerSeason, ageStep: false).Attributes;

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
        outcome.ProgressChanges.Should().OnlyContain(change => change.DeltaMicro > 0, "nothing ages at 22");
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
    public void An_invalid_progress_or_out_of_scale_potential_is_a_programming_error()
    {
        var potential = () => DailyProgression.Advance(Input(potential: 0));
        var wholePoint = () => DailyProgression.Advance(Input(progress: Progress(AttributeProgress.Basis)));
        var wholePointLost = () => DailyProgression.Advance(Input(progress: Progress(-AttributeProgress.Basis)));
        var wrongLength = () => DailyProgression.Advance(Input(progress: [1, 2, 3]));

        potential.Should().Throw<ArgumentOutOfRangeException>("TRN-4");
        wholePoint.Should().Throw<ArgumentOutOfRangeException>("a whole point is spent, never carried (TRN-10)");
        wholePointLost.Should().Throw<ArgumentOutOfRangeException>("TRN-16");
        wrongLength.Should().Throw<ArgumentOutOfRangeException>("one value per attribute (TRN-10)");
    }

    [Fact]
    public void The_days_development_is_split_by_weight_across_the_programme_and_equally_across_the_rest()
    {
        var outcome = DailyProgression.Advance(Input(programme: TrainingProgramme.Forward));
        var forward = TrainingProgrammes.Of(TrainingProgramme.Forward);
        var budget = outcome.ProgressChanges.Sum(change => change.DeltaMicro);
        var focusBudget = (double)budget * DailyProgression.ProgrammeSharePermille / 1_000;
        var rest = DevelopmentLimit.DevelopingAttributes(PlayerPosition.Striker).Where(name => forward.WeightOf(name) == 0).ToList();
        var totalWeight = forward.Attributes.Sum(entry => entry.Weight);

        outcome.ProgressChanges.Select(change => change.Attribute).Should().BeEquivalentTo(
            DevelopmentLimit.DevelopingAttributes(PlayerPosition.Striker),
            "every developing attribute earns a share, and nothing else does");

        foreach (var entry in forward.Attributes)
        {
            var expected = focusBudget * entry.Weight / totalWeight;

            outcome.ProgressChanges.Single(change => change.Attribute == entry.Attribute).DeltaMicro.Should().BeInRange(
                (int)Math.Floor(expected),
                (int)Math.Ceiling(expected) + 1,
                $"{entry.Attribute} takes its weight's share of the programme's part of the day (TRN-10)");
        }

        foreach (var name in rest)
        {
            var expected = (budget - focusBudget) / rest.Count;

            outcome.ProgressChanges.Single(change => change.Attribute == name).DeltaMicro.Should().BeInRange(
                (int)Math.Floor(expected),
                (int)Math.Ceiling(expected) + 1,
                $"{name} takes an equal part of the rest (TRN-10)");
        }

        budget.Should().BeInRange(
            (outcome.DevelopmentMilli * 1_000) - 500,
            (outcome.DevelopmentMilli * 1_000) + 500,
            "the whole budget is spent, nothing is lost to the split");
    }

    [Fact]
    public void A_core_attribute_earns_three_times_the_share_of_a_supporting_one()
    {
        var outcome = DailyProgression.Advance(Input(programme: TrainingProgramme.Forward));

        int Share(AttributeName name) => outcome.ProgressChanges.Single(change => change.Attribute == name).DeltaMicro;

        Share(AttributeName.Finishing).Should().BeCloseTo(3 * Share(AttributeName.Dribbling), 3, "3 core against 1 supporting");
        Share(AttributeName.Positioning).Should().BeCloseTo(2 * Share(AttributeName.Pace), 2, "2 important against 1 supporting");
    }

    [Fact]
    public void Each_attributes_progress_carries_from_day_to_day()
    {
        var first = DailyProgression.Advance(Input(programme: TrainingProgramme.Forward));
        var second = DailyProgression.Advance(Input(programme: TrainingProgramme.Forward, progress: first.AttributeProgress));

        first.AttributeProgress[(int)AttributeName.Finishing].Should().BeGreaterThan(0);
        second.AttributeProgress[(int)AttributeName.Finishing].Should().BeGreaterThan(
            first.AttributeProgress[(int)AttributeName.Finishing],
            "a second day adds to the first day's progress");
        first.AttributeProgress[(int)AttributeName.Handling].Should().Be(0, "a goalkeeping skill is not in the forward programme");
    }

    [Fact]
    public void An_attribute_gains_a_point_when_its_own_progress_reaches_one()
    {
        var start = Input(programme: TrainingProgramme.Forward, progress: Progress(990_000));
        var outcome = DailyProgression.Advance(start);
        var share = outcome.ProgressChanges.Single(change => change.Attribute == AttributeName.Finishing).DeltaMicro;

        share.Should().BeGreaterThan(10_000, "the day's share carries Finishing over the line");
        outcome.Attributes.Finishing.Should().Be(start.Attributes.Finishing + 1);
        outcome.AttributeChanges.Should().ContainSingle(change => change.Attribute == AttributeName.Finishing)
            .Which.Delta.Should().Be(1);
        outcome.AttributeProgress[(int)AttributeName.Finishing].Should().Be(
            990_000 + share - AttributeProgress.Basis,
            "the point is taken off and the rest is carried");
    }

    [Fact]
    public void An_attribute_at_its_limit_takes_no_share_and_its_share_moves_to_the_others()
    {
        var limit = DevelopmentLimit.LimitOf(PlayerPosition.Striker, 15, AttributeName.Finishing);
        var values = new int[AttributeNames.Count];

        Array.Fill(values, 8);
        values[(int)AttributeName.Finishing] = limit;

        var capped = DailyProgression.Advance(Input(values: values, potential: 15, progress: Progress(500_000)));
        var open = DailyProgression.Advance(Input(values: [.. Enumerable.Repeat(8, AttributeNames.Count)], potential: 15));

        capped.ProgressChanges.Select(change => change.Attribute).Should().NotContain(AttributeName.Finishing);
        capped.AttributeProgress[(int)AttributeName.Finishing].Should().Be(0, "progress towards a point it cannot gain is dropped");
        capped.ProgressChanges.Sum(change => change.DeltaMicro).Should().BeCloseTo(
            open.ProgressChanges.Sum(change => change.DeltaMicro),
            2,
            "the budget does not shrink; the capped attribute's share moves to the others");
        capped.ProgressChanges.Single(change => change.Attribute == AttributeName.Composure).DeltaMicro.Should().BeGreaterThan(
            open.ProgressChanges.Single(change => change.Attribute == AttributeName.Composure).DeltaMicro);
    }

    [Fact]
    public void No_budget_is_lost_while_the_programmes_attributes_are_all_at_their_limit()
    {
        var forward = TrainingProgrammes.Of(TrainingProgramme.Forward);
        var values = new int[AttributeNames.Count];

        Array.Fill(values, 8);

        foreach (var entry in forward.Attributes)
        {
            values[(int)entry.Attribute] = DevelopmentLimit.LimitOf(PlayerPosition.Striker, 15, entry.Attribute);
        }

        var focused = DailyProgression.Advance(Input(values: values, potential: 15));
        var open = DailyProgression.Advance(Input(values: [.. Enumerable.Repeat(8, AttributeNames.Count)], potential: 15));

        focused.DevelopmentMilli.Should().Be(open.DevelopmentMilli, "the whole budget goes to the rest of the player");
        focused.ProgressChanges.Select(change => change.Attribute).Should().NotContain(forward.Attributes.Select(entry => entry.Attribute));
    }

    [Fact]
    public void Every_day_spends_the_whole_budget_until_the_player_is_complete()
    {
        var position = PlayerPosition.Striker;
        var cap = DevelopmentLimit.CapSum(position, 15);
        var state = Input(
            potential: 15,
            age: 17,
            values: [.. AttributeNames.All.Select(name => Math.Max(1, DevelopmentLimit.FormulaValue(position, 15, name) - 7))]);
        var developing = DevelopmentLimit.DevelopingAttributes(position);
        var daysWithBudget = 0;

        for (var day = 0; day < 9 * TrainingAgeCurve.TrainingDaysPerSeason; day++)
        {
            var age = 17 + (day / TrainingAgeCurve.TrainingDaysPerSeason);
            var complete = developing.Sum(name => state.Attributes.ValueOf(name)) >= cap;
            var outcome = DailyProgression.Advance(state with { Day = Day.AddDays(day), Age = age });

            if (!complete && age <= 25)
            {
                outcome.DevelopmentMilli.Should().BeGreaterThan(0, $"nothing is lost before the cap, day {day} (TRN-10)");
                daysWithBudget++;
            }
            else if (complete)
            {
                outcome.DevelopmentMilli.Should().Be(0, $"a complete player develops no more, day {day}");
            }

            state = Carry(state, outcome);
        }

        daysWithBudget.Should().BeGreaterThan(TrainingAgeCurve.TrainingDaysPerSeason, "the youth spent several seasons developing");
    }

    [Fact]
    public void An_ageing_attribute_loses_progress_and_falls_a_point_when_it_reaches_minus_one()
    {
        // Pace has been ageing since 28, so at 31 it loses progress every day.
        var start = Input(programme: TrainingProgramme.Recovery, age: 31, progress: Progress(-999_999, AttributeName.Pace));
        var outcome = DailyProgression.Advance(start);

        outcome.ProgressChanges.Should().OnlyContain(change => change.DeltaMicro < 0, "nothing is trained, so everything only ages");

        var fallen = outcome.AttributeChanges.Should().ContainSingle(change => change.Attribute == AttributeName.Pace).Which;

        fallen.Delta.Should().Be(-1);
        outcome.Attributes.Pace.Should().Be(start.Attributes.Pace - 1);
        outcome.AttributeProgress[(int)AttributeName.Pace].Should().BeInRange(
            -AttributeProgress.Basis + 1,
            0,
            "the point is taken off and the remainder carried");
    }

    [Fact]
    public void A_trained_and_ageing_attribute_nets_its_growth_against_its_decline()
    {
        var trained = DailyProgression.Advance(Input(programme: TrainingProgramme.Physical, age: 31));
        var idle = DailyProgression.Advance(Input(programme: TrainingProgramme.Recovery, age: 31));

        int Pace(DailyProgressionOutcome outcome) =>
            outcome.ProgressChanges.Single(change => change.Attribute == AttributeName.Pace).DeltaMicro;

        Pace(idle).Should().BeNegative("an idle attribute only ages");
        Pace(trained).Should().BeGreaterThan(Pace(idle), "training adds a share and halves the decline");
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void Calibration_an_average_young_player_is_at_his_limit_at_about_twenty_four(PlayerPosition position)
    {
        var average = Calibrate(position, TrainingAptitude.Neutral);
        var slow = Calibrate(position, TrainingAptitude.MinPermille);
        var quick = Calibrate(position, TrainingAptitude.MaxPermille);

        _output.WriteLine(
            $"{position}: shortfall of the developing attributes' average from the limit at 19/21/22/24 (slow / average / quick): "
            + $"{Describe(slow)} / {Describe(average)} / {Describe(quick)}");

        average[At(24)].Should().BeLessThanOrEqualTo(0.3, "an average learner is within 0.3 of his limit by 24");
        average[At(21)].Should().BeGreaterThan(0.3, "but not before 21");
        slow[At(24)].Should().BeGreaterThan(average[At(24)], "a low-aptitude learner is clearly slower");
        slow[At(24)].Should().BeGreaterThan(0.3, "and falls short by 24");
        quick[At(22)].Should().BeLessThanOrEqualTo(0.3, "a quick learner is there by 22");
        quick[At(21)].Should().BeLessThan(average[At(21)], "a quick learner is ahead of an average one");
    }

    public static IEnumerable<object[]> Positions() => Enum.GetValues<PlayerPosition>().Select(position => new object[] { position });

    private static string Describe(double[] shortfalls) =>
        string.Join("/", new[] { 19, 21, 22, 24 }.Select(age => shortfalls[At(age)].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// Runs a 17-year-old, who starts at the generator's youth level for a potential of 15 (seven points under
    /// the adult formula), through seven seasons of normal training. Returns the shortfall of the developing
    /// attributes' average from the limit at the start of each season, ages 17 to 24 (read with <see cref="At"/>).
    /// </summary>
    private static double[] Calibrate(PlayerPosition position, int aptitude)
    {
        const int potential = 15;
        var programme = TrainingProgrammes.DefaultFor(position);
        var developing = DevelopmentLimit.DevelopingAttributes(position);
        var cap = DevelopmentLimit.CapSum(position, potential);
        var state = Input(
            values: [.. AttributeNames.All.Select(name => Math.Max(
                WorldRuleSet.AttributeMin,
                DevelopmentLimit.FormulaValue(position, potential, name) - 7))],
            potential: potential,
            age: 17,
            aptitude: aptitude,
            programme: programme,
            position: position);

        double Shortfall() => (cap - developing.Sum(name => state.Attributes.ValueOf(name))) / (double)developing.Count;

        var result = new double[8];

        for (var season = 0; season < 8; season++)
        {
            result[season] = Shortfall();

            for (var day = 0; season < 7 && day < TrainingAgeCurve.TrainingDaysPerSeason; day++)
            {
                state = Carry(state, DailyProgression.Advance(state with
                {
                    Day = Day.AddDays((season * TrainingAgeCurve.TrainingDaysPerSeason) + day),
                    Age = 17 + season,
                }));
            }
        }

        return result;
    }

    /// <summary>The age a calibration shortfall is read at, from the 17-year-old start.</summary>
    private static int At(int age) => age - 17;

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
            AttributeProgress = outcome.AttributeProgress,
        };

    private static DailyProgressionInput Input(
        int[]? values = null,
        int potential = 20,
        int age = 18,
        int aptitude = TrainingAptitude.Neutral,
        PlayerPosition position = PlayerPosition.Striker,
        TrainingProgramme programme = TrainingProgramme.Forward,
        TrainingIntensity intensity = TrainingIntensity.Normal,
        int condition = 8_000,
        int fatigue = 5_000,
        int morale = 5_000,
        int sharpness = 5_000,
        IReadOnlyList<int>? progress = null,
        DateOnly? day = null) =>
        new(
            PlayerId,
            age,
            position,
            PlayerAttributeSet.FromValues(values ??[.. Enumerable.Repeat(10, AttributeNames.Count)]),
            potential,
            aptitude,
            programme,
            intensity,
            condition,
            fatigue,
            morale,
            sharpness,
            progress ?? AttributeProgress.None(),
            day ?? Day);

    /// <summary>Builds a progress set with a value on one attribute, Finishing unless another is named.</summary>
    private static int[] Progress(int micro, AttributeName attribute = AttributeName.Finishing)
    {
        var progress = new int[AttributeNames.Count];

        progress[(int)attribute] = micro;

        return progress;
    }
}
