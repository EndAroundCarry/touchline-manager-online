using FluentAssertions;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.Squad.Training;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>
/// The limit a player's potential and position set on his development (`TRN-4`): which attributes grow, each
/// attribute's own limit, and the overall sum.
/// </summary>
public sealed class DevelopmentLimitTests
{
    [Fact]
    public void An_outfielder_develops_the_24_outfield_attributes_and_a_keeper_all_28()
    {
        var outfield = DevelopmentLimit.DevelopingAttributes(PlayerPosition.Striker);

        outfield.Should().HaveCount(24);
        outfield.Should().NotContain(name => AttributeNames.FamilyOf(name) == AttributeFamily.Goalkeeping);
        DevelopmentLimit.DevelopingAttributes(PlayerPosition.Goalkeeper).Should().HaveCount(AttributeNames.Count);

        foreach (var name in AttributeNames.All)
        {
            DevelopmentLimit.Develops(PlayerPosition.Goalkeeper, name).Should().BeTrue();
            DevelopmentLimit.Develops(PlayerPosition.Striker, name).Should().Be(outfield.Contains(name));
        }
    }

    [Theory]
    [InlineData(PlayerPosition.Striker, 15)]
    [InlineData(PlayerPosition.CentreBack, 9)]
    [InlineData(PlayerPosition.Goalkeeper, 20)]
    [InlineData(PlayerPosition.RightWinger, 1)]
    public void An_attributes_limit_is_the_generators_formula_plus_the_headroom_on_the_scale(PlayerPosition position, int potential)
    {
        foreach (var name in DevelopmentLimit.DevelopingAttributes(position))
        {
            var bonus = PlayerAttributeProfiles.BonusFor(position, name);

            DevelopmentLimit.FormulaValue(position, potential, name).Should().Be(
                Math.Clamp(potential + bonus, WorldRuleSet.AttributeMin, WorldRuleSet.AttributeMax));
            DevelopmentLimit.LimitOf(position, potential, name).Should().Be(
                Math.Clamp(
                    potential + bonus + DevelopmentLimit.CeilingHeadroom,
                    WorldRuleSet.AttributeMin,
                    WorldRuleSet.AttributeMax));
        }
    }

    [Fact]
    public void The_cap_is_the_sum_of_the_formula_values_over_the_developing_attributes()
    {
        var position = PlayerPosition.CentralMidfielder;
        var expected = DevelopmentLimit.DevelopingAttributes(position)
            .Sum(name => DevelopmentLimit.FormulaValue(position, 14, name));

        DevelopmentLimit.CapSum(position, 14).Should().Be(expected);
        DevelopmentLimit.CapSum(position, 15).Should().BeGreaterThan(DevelopmentLimit.CapSum(position, 14), "a higher potential is a higher cap");
    }

    [Fact]
    public void The_limits_array_is_zero_for_an_attribute_the_player_never_develops()
    {
        var limits = DevelopmentLimit.Limits(PlayerPosition.Striker, 15);

        limits.Should().HaveCount(AttributeNames.Count);

        foreach (var name in AttributeNames.All)
        {
            if (DevelopmentLimit.Develops(PlayerPosition.Striker, name))
            {
                limits[(int)name].Should().Be(DevelopmentLimit.LimitOf(PlayerPosition.Striker, 15, name));
            }
            else
            {
                limits[(int)name].Should().Be(0, $"{name} never grows for an outfielder");
            }
        }
    }
}
