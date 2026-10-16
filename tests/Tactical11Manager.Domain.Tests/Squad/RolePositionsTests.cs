using FluentAssertions;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>
/// Which players a slot's role accepts and how well each suits it (`INS-10`, `TAC-8`).
/// </summary>
public sealed class RolePositionsTests
{
    [Fact]
    public void Every_role_names_at_least_one_natural_position()
    {
        foreach (var role in Enum.GetValues<PlayerRole>())
        {
            RolePositions.NaturalFor(role).Should().NotBeEmpty($"{role}");
        }
    }

    [Theory]
    [InlineData(PlayerPosition.RightMidfielder, PositionFamily.Midfield)]
    [InlineData(PlayerPosition.LeftMidfielder, PositionFamily.Midfield)]
    public void The_new_positions_are_midfielders(PlayerPosition position, PositionFamily family)
    {
        PlayerPositions.FamilyOf(position).Should().Be(family);
        PlayerPositions.FromCode(position.ToCode()).Should().Be(position);
        PlayerPositions.All.Should().Contain(position);
    }

    [Fact]
    public void The_wide_midfielder_role_is_a_midfield_role_with_its_own_code()
    {
        PlayerRoles.FamilyOf(PlayerRole.WideMidfielder).Should().Be(PositionFamily.Midfield);
        PlayerRoles.ToCode(PlayerRole.WideMidfielder).Should().Be("wide_midfielder");
        PlayerRoles.FromCode("wide_midfielder").Should().Be(PlayerRole.WideMidfielder);
        PlayerRoles.ToCode(PlayerRole.WideMidfielder).Length.Should().BeLessThanOrEqualTo(PlayerRoles.MaxCodeLength);
    }

    [Fact]
    public void The_values_already_stored_keep_their_numbers()
    {
        // The canonical serializer writes these as ints, so a renumbering would change every match's hash.
        ((int)PlayerPosition.Striker).Should().Be(9);
        ((int)PlayerPosition.RightMidfielder).Should().Be(10);
        ((int)PlayerPosition.LeftMidfielder).Should().Be(11);
        ((int)PlayerRole.Striker).Should().Be(8);
        ((int)PlayerRole.WideMidfielder).Should().Be(9);
    }

    [Fact]
    public void A_joined_code_list_orders_the_new_positions_after_the_old_ones()
    {
        PlayerPositions.JoinCodes([PlayerPosition.LeftMidfielder, PlayerPosition.Striker, PlayerPosition.RightBack])
            .Should().Be("rb,st,lm");
    }

    [Theory]
    [InlineData(PlayerPosition.RightMidfielder)]
    [InlineData(PlayerPosition.LeftMidfielder)]
    [InlineData(PlayerPosition.RightWinger)]
    [InlineData(PlayerPosition.LeftBack)]
    public void A_wide_midfielder_slot_accepts_a_midfielder_a_winger_and_a_full_back(PlayerPosition primary)
    {
        RolePositions.IsAtHome(primary, [], PositionFamily.Midfield, PlayerRole.WideMidfielder).Should().BeTrue();
    }

    [Theory]
    [InlineData(PlayerPosition.CentreBack)]
    [InlineData(PlayerPosition.Striker)]
    public void A_wide_midfielder_slot_flags_a_centre_back_and_a_striker(PlayerPosition primary)
    {
        RolePositions.IsAtHome(primary, [], PositionFamily.Midfield, PlayerRole.WideMidfielder).Should().BeFalse();
    }

    [Fact]
    public void A_secondary_position_in_the_band_or_the_role_list_also_counts()
    {
        RolePositions.IsAtHome(
                PlayerPosition.Striker,
                [PlayerPosition.RightMidfielder],
                PositionFamily.Midfield,
                PlayerRole.WideMidfielder)
            .Should().BeTrue();
        RolePositions.IsAtHome(
                PlayerPosition.Striker,
                [PlayerPosition.CentralMidfielder],
                PositionFamily.Midfield,
                PlayerRole.WideMidfielder)
            .Should().BeTrue();
    }

    [Fact]
    public void A_winger_at_wing_back_and_a_midfielder_at_winger_are_at_home()
    {
        RolePositions.IsAtHome(PlayerPosition.RightWinger, [], PositionFamily.Defence, PlayerRole.WingBack)
            .Should().BeTrue();
        RolePositions.IsAtHome(PlayerPosition.LeftMidfielder, [], PositionFamily.Defence, PlayerRole.WingBack)
            .Should().BeTrue();
        RolePositions.IsAtHome(PlayerPosition.RightMidfielder, [], PositionFamily.Attack, PlayerRole.Winger)
            .Should().BeTrue();
    }

    [Fact]
    public void Suitability_ranks_the_role_first_then_the_band_then_cover_then_nothing()
    {
        static int Suit(PlayerPosition primary, params PlayerPosition[] secondary) =>
            RolePositions.Suitability(primary, secondary, PositionFamily.Midfield, PlayerRole.WideMidfielder);

        Suit(PlayerPosition.LeftMidfielder).Should().Be(3);
        Suit(PlayerPosition.CentralMidfielder).Should().Be(2);
        Suit(PlayerPosition.RightWinger).Should().Be(1, "his position is on the role list though in another band");
        Suit(PlayerPosition.Striker, PlayerPosition.AttackingMidfielder).Should().Be(1);
        Suit(PlayerPosition.CentreBack).Should().Be(0);
    }

    [Fact]
    public void A_role_that_asked_for_the_band_alone_still_ranks_the_band_above_cover()
    {
        // A striker slot: the striker is natural, a winger is merely in the band, a full back is neither.
        RolePositions.Suitability(PlayerPosition.Striker, [], PositionFamily.Attack, PlayerRole.Striker).Should().Be(3);
        RolePositions.Suitability(PlayerPosition.RightWinger, [], PositionFamily.Attack, PlayerRole.Striker).Should().Be(2);
        RolePositions.Suitability(PlayerPosition.RightBack, [], PositionFamily.Attack, PlayerRole.Striker).Should().Be(0);
    }
}
