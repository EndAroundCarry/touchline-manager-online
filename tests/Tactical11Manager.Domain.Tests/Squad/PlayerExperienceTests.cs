using FluentAssertions;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>The experience rule: earning, starting value, levels, and the skill bonus.</summary>
public sealed class PlayerExperienceTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(14, 0)]
    [InlineData(15, 50)]
    [InlineData(90, 50)]
    public void A_match_earns_half_a_point_from_fifteen_minutes(int minutes, int expected) =>
        PlayerExperience.EarnedFor(minutes).Should().Be(expected);

    [Theory]
    [InlineData(17, 0)]
    [InlineData(18, 0)]
    [InlineData(19, 400)]
    [InlineData(25, 2_800)]
    [InlineData(30, 4_800)]
    [InlineData(31, 5_000)]
    [InlineData(40, 5_000)]
    public void A_new_player_starts_with_four_points_a_year_from_eighteen_up_to_fifty(int age, int expected) =>
        PlayerExperience.StartingFor(age).Should().Be(expected);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1_999, 0, 0)]
    [InlineData(2_000, 1, 100)]
    [InlineData(3_999, 1, 100)]
    [InlineData(4_000, 2, 200)]
    [InlineData(6_000, 3, 300)]
    [InlineData(8_000, 4, 400)]
    [InlineData(9_999, 4, 400)]
    [InlineData(10_000, 5, 500)]
    public void Levels_come_every_twenty_points_and_each_adds_one_per_cent(int experienceBp, int level, int bonusBp)
    {
        PlayerExperience.LevelOf(experienceBp).Should().Be(level);
        PlayerExperience.SkillBonusBasisPointsOf(experienceBp).Should().Be(bonusBp);
    }

    [Fact]
    public void The_maximum_is_two_hundred_qualifying_matches()
    {
        (PlayerExperience.MaximumBasisPoints / PlayerExperience.PerMatchBasisPoints).Should().Be(200);
        PlayerExperience.MaximumLevel.Should().Be(5);
    }
}
