using FluentAssertions;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.Squad.Training;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The programme catalogue, the age curve, and the hidden aptitude (`TRN-1`, `TRN-14`…`TRN-16`).
/// </summary>
/// <remarks>
/// The weights and slopes are tunable constants, so these tests pin their shape — validity, bounds, and
/// ordering — and not their values.
/// </remarks>
public sealed class TrainingProgrammesTests
{
    [Fact]
    public void Every_programme_has_a_definition_with_a_unique_code_that_fits_the_column()
    {
        TrainingProgrammes.All.Select(definition => definition.Programme)
            .Should().BeEquivalentTo(Enum.GetValues<TrainingProgramme>());

        TrainingProgrammes.All.Select(definition => definition.Code).Should().OnlyHaveUniqueItems();
        TrainingProgrammes.All.Should().OnlyContain(
            definition => definition.Code.Length <= TrainingProgrammes.MaxCodeLength);
    }

    [Fact]
    public void Codes_round_trip_and_an_unknown_code_is_rejected()
    {
        foreach (var programme in Enum.GetValues<TrainingProgramme>())
        {
            TrainingProgrammes.FromCode(programme.ToCode()).Should().Be(programme);
        }

        TrainingProgrammes.TryFromCode("balanced", out _).Should().BeFalse();
        TrainingProgrammes.TryFromCode(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Every_trained_attribute_is_valid_once_and_every_weight_is_between_one_and_three()
    {
        foreach (var definition in TrainingProgrammes.All)
        {
            definition.Attributes.Select(entry => entry.Attribute).Should().OnlyHaveUniqueItems(definition.Code);
            definition.Attributes.All(
                entry => Enum.IsDefined(entry.Attribute)
                    && entry.Weight >= TrainingProgrammes.SupportingWeight
                    && entry.Weight <= TrainingProgrammes.CoreWeight)
                .Should().BeTrue(definition.Code);
        }
    }

    [Fact]
    public void Every_programme_but_recovery_trains_something_and_names_a_core_attribute()
    {
        TrainingProgrammes.Of(TrainingProgramme.Recovery).Attributes.Should().BeEmpty();

        foreach (var definition in TrainingProgrammes.All.Where(d => d.Programme != TrainingProgramme.Recovery))
        {
            definition.Attributes.Should().Contain(
                entry => entry.Weight == TrainingProgrammes.CoreWeight,
                definition.Code);
            definition.Attributes.Select(entry => entry.Weight).Should().BeInDescendingOrder(
                $"{definition.Code} lists its heaviest attributes first");
        }
    }

    [Fact]
    public void A_weight_lookup_matches_the_attribute_list()
    {
        foreach (var definition in TrainingProgrammes.All)
        {
            foreach (var name in AttributeNames.All)
            {
                var expected = definition.Attributes.SingleOrDefault(entry => entry.Attribute == name)?.Weight ?? 0;

                definition.WeightOf(name).Should().Be(expected, $"{definition.Code}/{name}");
            }
        }
    }

    [Fact]
    public void Every_position_has_a_default_programme_and_the_manual_programmes_are_never_one()
    {
        var defaults = PlayerPositions.All.Select(TrainingProgrammes.DefaultFor).ToList();

        defaults.Should().NotContain(
            [TrainingProgramme.Mental, TrainingProgramme.Physical, TrainingProgramme.Recovery]);

        TrainingProgrammes.DefaultFor(PlayerPosition.Goalkeeper).Should().Be(TrainingProgramme.Goalkeeper);
        TrainingProgrammes.DefaultFor(PlayerPosition.CentreBack).Should().Be(TrainingProgramme.Defender);
        TrainingProgrammes.DefaultFor(PlayerPosition.RightBack).Should().Be(TrainingProgramme.WingBack);
        TrainingProgrammes.DefaultFor(PlayerPosition.LeftBack).Should().Be(TrainingProgramme.WingBack);
        TrainingProgrammes.DefaultFor(PlayerPosition.DefensiveMidfielder).Should().Be(TrainingProgramme.Midfielder);
        TrainingProgrammes.DefaultFor(PlayerPosition.CentralMidfielder).Should().Be(TrainingProgramme.Midfielder);
        TrainingProgrammes.DefaultFor(PlayerPosition.AttackingMidfielder).Should().Be(TrainingProgramme.Midfielder);
        TrainingProgrammes.DefaultFor(PlayerPosition.RightWinger).Should().Be(TrainingProgramme.Winger);
        TrainingProgrammes.DefaultFor(PlayerPosition.LeftWinger).Should().Be(TrainingProgramme.Winger);
        TrainingProgrammes.DefaultFor(PlayerPosition.Striker).Should().Be(TrainingProgramme.Forward);
    }

    [Fact]
    public void The_growth_factor_never_increases_with_age_and_is_never_zero()
    {
        var previous = int.MaxValue;

        for (var age = 0; age <= 50; age++)
        {
            var factor = TrainingAgeCurve.GrowthPermille(age);

            factor.Should().BeLessThanOrEqualTo(previous, $"age {age}");
            factor.Should().BeGreaterThanOrEqualTo(TrainingAgeCurve.FloorPermille, $"age {age}");

            previous = factor;
        }
    }

    [Theory]
    [InlineData(15, 1_000)]
    [InlineData(19, 1_000)]
    [InlineData(20, 900)]
    [InlineData(22, 700)]
    [InlineData(25, 400)]
    public void The_growth_curve_is_linear_from_the_peak_to_twenty_five(int age, int expected)
    {
        TrainingAgeCurve.GrowthPermille(age).Should().Be(expected);
    }

    [Fact]
    public void After_twenty_five_each_year_keeps_three_quarters_of_the_last_down_to_a_floor()
    {
        TrainingAgeCurve.GrowthPermille(26).Should().Be(300);
        TrainingAgeCurve.GrowthPermille(27).Should().Be(225);
        TrainingAgeCurve.GrowthPermille(30).Should().BeInRange(90, 100);
        TrainingAgeCurve.GrowthPermille(35).Should().BeInRange(20, 25);
        TrainingAgeCurve.GrowthPermille(60).Should().Be(TrainingAgeCurve.FloorPermille);
    }

    [Fact]
    public void Physical_attributes_age_before_technical_and_mental_attributes_last()
    {
        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Pace, 27).Should().Be(0);
        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Pace, 28).Should().BeGreaterThan(0);

        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Finishing, 31).Should().Be(0);
        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Finishing, 32).Should().BeGreaterThan(0);

        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Reflexes, 31).Should().Be(0);
        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Reflexes, 32).Should().BeGreaterThan(0);

        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Handling, 33).Should().Be(0);
        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Handling, 34).Should().BeGreaterThan(0);

        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.WorkRate, 30).Should().Be(0);
        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.WorkRate, 31).Should().BeGreaterThan(0);

        foreach (var name in new[]
            {
                AttributeName.Decisions, AttributeName.Vision, AttributeName.Positioning, AttributeName.Composure,
                AttributeName.Anticipation, AttributeName.Aggression, AttributeName.Leadership,
            })
        {
            TrainingAgeCurve.AnnualDeclineMilli(name, 45).Should().Be(0, $"{name} never ages");
        }
    }

    [Fact]
    public void Decline_grows_with_every_year_past_the_start_age()
    {
        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Pace, 29)
            .Should().Be(2 * TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Pace, 28));
        TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Pace, 33)
            .Should().BeGreaterThan(TrainingAgeCurve.AnnualDeclineMilli(AttributeName.Pace, 32));
    }

    [Fact]
    public void Aptitude_is_deterministic_and_inside_its_range()
    {
        for (var i = 0; i < 2_000; i++)
        {
            var id = IdOf(i);
            var aptitude = TrainingAptitude.For(id);

            aptitude.Should().BeInRange(TrainingAptitude.MinPermille, TrainingAptitude.MaxPermille);
            TrainingAptitude.For(id).Should().Be(aptitude);
        }
    }

    [Fact]
    public void Aptitude_averages_close_to_neutral_and_actually_varies()
    {
        var sample = Enumerable.Range(0, 20_000).Select(i => TrainingAptitude.For(IdOf(i))).ToList();

        sample.Average().Should().BeApproximately(TrainingAptitude.Neutral, 10);
        sample.Distinct().Count().Should().BeGreaterThan(300);
        sample.Should().Contain(value => value < 700);
        sample.Should().Contain(value => value > 1_300);
    }

    private static Guid IdOf(int i) => new(i, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
}
