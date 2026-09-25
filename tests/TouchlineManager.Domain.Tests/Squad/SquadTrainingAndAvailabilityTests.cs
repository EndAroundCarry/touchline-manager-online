using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// Training persistence, squad legality, unavailability, and the position/state code tables
/// (`TRN-1`, `TRN-2`, `TRN-12`, `DIS-1`, `SQ-2`, `SQ-3`).
/// </summary>
public sealed class SquadTrainingAndAvailabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly EffectiveDate = new(2026, 9, 24);

    [Fact]
    public void Every_training_focus_and_intensity_round_trips_through_its_code()
    {
        foreach (var value in Enum.GetValues<TrainingFocus>())
        {
            TrainingPlans.FromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<TrainingIntensity>())
        {
            TrainingPlans.IntensityFromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<AttributeFamily>())
        {
            AttributeFamilies.FromCode(value.ToCode()).Should().Be(value);
            AttributeNames.All.Should().Contain(
                name => AttributeNames.FamilyOf(name) == value,
                $"TRN-2: family {value} has attributes");
        }
    }

    [Fact]
    public void Every_position_and_family_round_trips_through_its_code()
    {
        foreach (var position in PlayerPositions.All)
        {
            PlayerPositions.FromCode(position.ToCode()).Should().Be(position);
        }

        foreach (var family in Enum.GetValues<PositionFamily>())
        {
            PositionFamilies.FromCode(family.ToCode()).Should().Be(family);
            PlayerPositions.All.Should().Contain(position => PlayerPositions.FamilyOf(position) == family);
        }

        foreach (var role in Enum.GetValues<PlayerRole>())
        {
            PlayerRoles.FromCode(role.ToCode()).Should().Be(role);
        }
    }

    [Fact]
    public void A_position_code_list_round_trips_and_is_order_independent()
    {
        PlayerPositions.JoinCodes([PlayerPosition.LeftBack, PlayerPosition.CentreBack])
            .Should().Be("cb,lb");
        PlayerPositions.ParseCodes("cb,lb")
            .Should().Equal(PlayerPosition.CentreBack, PlayerPosition.LeftBack);
        PlayerPositions.ParseCodes(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void A_training_plan_can_be_set_and_revised()
    {
        var plan = TrainingPlan.Set(
            Guid.CreateVersion7(), Guid.CreateVersion7(), TrainingFocus.Balanced, TrainingIntensity.Normal,
            EffectiveDate, Now);

        plan.TeamFocus.Should().Be(TrainingFocus.Balanced, "TRN-1");
        plan.Version.Should().Be(1);

        plan.Revise(TrainingFocus.Recovery, TrainingIntensity.Light, EffectiveDate.AddDays(1), Now.AddDays(1));

        plan.TeamFocus.Should().Be(TrainingFocus.Recovery);
        plan.Intensity.Should().Be(TrainingIntensity.Light);
        plan.Version.Should().Be(2);
    }

    [Fact]
    public void An_individual_focus_can_be_set_and_revised()
    {
        var focus = PlayerTrainingFocus.Set(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), AttributeFamily.Technical,
            EffectiveDate, Now);

        focus.FocusFamily.Should().Be(AttributeFamily.Technical, "TRN-2");

        focus.Revise(AttributeFamily.Physical, EffectiveDate.AddDays(1), Now.AddDays(1));

        focus.FocusFamily.Should().Be(AttributeFamily.Physical);
        focus.Version.Should().Be(2);
    }

    [Fact]
    public void Squad_legality_accepts_a_full_generated_squad_and_rejects_a_thin_one()
    {
        var full = Enumerable.Repeat(PositionFamily.Midfield, WorldRuleSet.GeneratorSquadTarget)
            .Concat([PositionFamily.Goalkeeper, PositionFamily.Goalkeeper]);

        SquadLegality.IsWithinBounds(WorldRuleSet.GeneratorSquadTarget).Should().BeTrue("SQ-3");
        SquadLegality.IsLegal(full.Count(), full).Should().BeTrue("SQ-2");

        var tooFew = Enumerable.Repeat(PositionFamily.Midfield, WorldRuleSet.SquadMinimumRegistered - 3)
            .Concat([PositionFamily.Goalkeeper, PositionFamily.Goalkeeper])
            .ToList();

        tooFew.Should().HaveCount(WorldRuleSet.SquadMinimumRegistered - 1);
        SquadLegality.MeetsMinimum(tooFew.Count).Should().BeFalse("SQ-2");
        SquadLegality.IsLegal(tooFew.Count, tooFew).Should().BeFalse();

        var noKeepers = Enumerable.Repeat(
            PositionFamily.Defence, WorldRuleSet.SquadMinimumRegistered);
        SquadLegality.HasMinimumGoalkeepers(noKeepers).Should().BeFalse("SQ-2");

        SquadLegality.IsWithinBounds(WorldRuleSet.SquadMaximumRegistered + 1).Should().BeFalse("SQ-3");
    }

    [Fact]
    public void An_unavailability_is_measured_in_fixtures_and_resolves_when_they_are_served()
    {
        var record = PlayerUnavailability.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            UnavailabilityType.Injury, InjurySeverity.Moderate, remainingFixtures: 3, sourceFixtureId: null, Now);

        record.IsOpen.Should().BeTrue("TRN-12");

        record.ServeFixture(Now.AddDays(1)).Should().BeFalse();
        record.RemainingFixtures.Should().Be(2);

        record.ServeFixture(Now.AddDays(2)).Should().BeFalse();
        record.ServeFixture(Now.AddDays(3)).Should().BeTrue();

        record.IsOpen.Should().BeFalse();
        record.RemainingFixtures.Should().Be(0);
        record.ResolvedAt.Should().Be(Now.AddDays(3));
    }

    [Fact]
    public void An_unavailability_with_no_fixtures_or_already_resolved_is_rejected()
    {
        var noFixtures = () => PlayerUnavailability.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            UnavailabilityType.Suspension, InjurySeverity.Minor, remainingFixtures: 0, null, Now);

        noFixtures.Should().Throw<ArgumentOutOfRangeException>("DIS-5: a suspension is served by fixtures");

        var record = PlayerUnavailability.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            UnavailabilityType.Suspension, InjurySeverity.Minor, remainingFixtures: 1, null, Now);

        record.Resolve(Now.AddDays(1));
        record.IsOpen.Should().BeFalse();

        var serveResolved = () => record.ServeFixture(Now.AddDays(2));
        var resolveAgain = () => record.Resolve(Now.AddDays(3));

        serveResolved.Should().Throw<InvalidOperationException>();
        resolveAgain.Should().Throw<InvalidOperationException>();
    }
}
