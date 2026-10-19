using FluentAssertions;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>Verifies the curve that takes skills off a tired player (tired skills, Milestone 3).</summary>
public sealed class TickSkillTirednessTests
{
    [Theory]
    [InlineData(10_000, 0)]
    [InlineData(9_500, 100)]
    [InlineData(9_000, 200)]
    [InlineData(8_500, 500)]
    [InlineData(8_000, 800)]
    [InlineData(7_500, 1_100)]
    [InlineData(7_000, 1_400)]
    [InlineData(6_500, 1_700)]
    [InlineData(6_000, 2_000)]
    [InlineData(5_000, 2_500)]
    [InlineData(4_000, 3_000)]
    [InlineData(2_000, 3_000)]
    [InlineData(0, 3_000)]
    public void The_full_drop_follows_the_breakpoints(int condition, int drop)
    {
        TickSkillTiredness.FullDropBasisPoints(condition).Should().Be(drop);
        TickSkillTiredness.DropBasisPoints(condition, MatchAttributeName.Tackling).Should().Be(drop);
        TickSkillTiredness.DropBasisPoints(condition, MatchAttributeFamily.Goalkeeping).Should().Be(drop);
    }

    [Fact]
    public void A_mental_skill_loses_half_as_much()
    {
        foreach (var condition in new[] { 10_000, 9_000, 7_500, 6_000, 5_000, 4_000, 1_000 })
        {
            var full = TickSkillTiredness.FullDropBasisPoints(condition);

            TickSkillTiredness.DropBasisPoints(condition, MatchAttributeName.Decisions).Should().Be(full / 2);
            TickSkillTiredness.DropBasisPoints(condition, MatchAttributeFamily.Mental).Should().Be(full / 2);
        }
    }

    [Fact]
    public void Pace_and_acceleration_lose_nothing_because_the_body_already_slows()
    {
        foreach (var condition in new[] { 9_000, 6_000, 0 })
        {
            TickSkillTiredness.DropBasisPoints(condition, MatchAttributeName.Pace).Should().Be(0);
            TickSkillTiredness.DropBasisPoints(condition, MatchAttributeName.Acceleration).Should().Be(0);
            TickSkillTiredness.DropBasisPoints(condition, MatchAttributeName.Agility).Should().BePositive("only the two speeds are left alone");
        }
    }

    [Fact]
    public void The_drop_never_shrinks_as_condition_falls()
    {
        var previous = 0;

        for (var condition = 10_000; condition >= 0; condition--)
        {
            var drop = TickSkillTiredness.FullDropBasisPoints(condition);

            drop.Should().BeGreaterThanOrEqualTo(previous, $"at {condition}");
            drop.Should().BeInRange(0, 3_000);

            previous = drop;
        }
    }

    [Fact]
    public void A_player_held_at_75_percent_loses_11_percent_of_a_technical_sheet_value_and_5_and_a_half_of_a_mental_one()
    {
        var tired = TickPlayerSkills.Tired(PlayerAttributesV1.Uniform(20), 7_500);
        var raw = tired.Uncurved();

        raw.Tackling.Should().Be(1_780, "20 less 11% is 17.8 sheet points");
        raw.Decisions.Should().Be(1_890, "20 less 5.5% is 18.9 sheet points");
        raw.Pace.Should().Be(2_000);
        raw.Acceleration.Should().Be(2_000);
    }

    [Fact]
    public void The_loss_passes_through_the_skill_curve_like_any_other_difference_from_the_average()
    {
        var fresh = TickPlayerSkills.Rated(PlayerAttributesV1.Uniform(20));
        var tired = TickPlayerSkills.Tired(PlayerAttributesV1.Uniform(20), 7_500);

        fresh.Tackling.Should().Be(1_500, "a sheet 20 plays as a 15");
        tired.Tackling.Should().Be(1_500 - 84, "2.2 sheet points of loss, 38% of it, is 0.84 of a curved point");
        tired.Decisions.Should().Be(1_500 - 42);
        tired.Pace.Should().Be(1_500);
        fresh.Condition.Should().Be(10_000);
        tired.Condition.Should().Be(7_500);
    }

    [Fact]
    public void A_fresh_player_is_exactly_as_the_curve_alone_leaves_him()
    {
        foreach (var value in new[] { 1, 6, 9, 13, 17, 20 })
        {
            var attributes = PlayerAttributesV1.Uniform(value);
            var skills = TickPlayerSkills.Tired(attributes, 10_000);
            var curved = (TickPlayerSkills.CurveCentre + ((value - TickPlayerSkills.CurveCentre) * TickPlayerSkills.CurvePercent / 100)) * TickPlayerSkills.Scale;

            skills.Tackling.Should().Be(curved);
            skills.Decisions.Should().Be(curved);
            skills.Uncurved().Tackling.Should().Be(value * TickPlayerSkills.Scale);
            skills.Should().Be(TickPlayerSkills.Rated(attributes));
        }
    }

    [Fact]
    public void A_spent_player_never_plays_below_what_the_curve_gives_a_worse_player()
    {
        var spent = TickPlayerSkills.Tired(PlayerAttributesV1.Uniform(1), 0);

        spent.Tackling.Should().BeGreaterThan(0);
        spent.Decisions.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Skills_not_built_from_attributes_have_nothing_to_tire()
    {
        var plain = TickPlayerSkills.From(PlayerAttributesV1.Uniform(12));

        plain.Natural.Should().BeNull();
        plain.Uncurved().Should().Be(plain);
    }
}
