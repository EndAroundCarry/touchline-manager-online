using FluentAssertions;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// Verifies the per-role numbers of the tick engine (`wide-midfielders`, Milestone 3): what a wide midfielder and a winger do differently.
/// </summary>
public sealed class TickRoleProfileTests
{
    [Fact]
    public void Only_a_wide_midfielder_needs_more_than_a_plain_chance_to_shoot_and_never_shoots_from_long_range()
    {
        foreach (var role in Enum.GetValues<MatchRole>().Where(role => role != MatchRole.WideMidfielder))
        {
            TickRoleProfile.ShotMinimumChance(role, far: false).Should().Be(TickBallCarrierBrain.ShotMinimumChance, role.ToString());
            TickRoleProfile.ShotMinimumChance(role, far: true).Should().Be(TickBallCarrierBrain.LongShotMinimumChance, role.ToString());
        }

        TickRoleProfile.ShotMinimumChance(MatchRole.WideMidfielder, far: false).Should().Be(TickRoleProfile.WideMidfielderShotMinimumChance);
        TickRoleProfile.ShotMinimumChance(MatchRole.WideMidfielder, far: true).Should().BeGreaterThan(TickBallCarrierBrain.MaximumGoalChance);
    }

    [Fact]
    public void A_wide_midfielder_crosses_from_deeper_and_values_it_more_and_nobody_else_does()
    {
        foreach (var role in Enum.GetValues<MatchRole>().Where(role => role != MatchRole.WideMidfielder))
        {
            TickRoleProfile.CrossMinimumX(role).Should().Be(TickBallCarrierBrain.CrossMinimumX, role.ToString());
            TickRoleProfile.CrossBonus(role).Should().Be(0, role.ToString());
        }

        TickRoleProfile.CrossMinimumX(MatchRole.WideMidfielder).Should().Be(TickRoleProfile.WideMidfielderCrossMinimumX).And.BeLessThan(TickBallCarrierBrain.CrossMinimumX);
        TickRoleProfile.CrossBonus(MatchRole.WideMidfielder).Should().Be(TickRoleProfile.WideMidfielderCrossBonus).And.BePositive();
    }

    [Theory]
    [InlineData(MatchMentality.Cautious, false, true)]
    [InlineData(MatchMentality.Balanced, false, true)]
    [InlineData(MatchMentality.Positive, false, true)]
    [InlineData(MatchMentality.Defensive, false, false)]
    [InlineData(MatchMentality.Balanced, true, false)]
    public void A_wide_midfielder_runs_wide_at_every_mentality_but_defensive_and_not_while_his_side_keeps_men_back(MatchMentality mentality, bool holding, bool runs)
    {
        TickRoleProfile.RunsWide(MatchRole.WideMidfielder, mentality, holding).Should().Be(runs);

        foreach (var role in Enum.GetValues<MatchRole>().Where(role => role != MatchRole.WideMidfielder))
        {
            TickRoleProfile.RunsWide(role, mentality, holding).Should().BeFalse(role.ToString());
        }
    }

    [Fact]
    public void Only_a_wide_midfielder_tracks_and_a_winger_is_the_only_one_who_never_screens()
    {
        foreach (var role in Enum.GetValues<MatchRole>())
        {
            TickRoleProfile.Tracks(role).Should().Be(role == MatchRole.WideMidfielder, role.ToString());
            TickRoleProfile.Screens(role).Should().Be(role != MatchRole.Winger, role.ToString());
        }
    }
}
