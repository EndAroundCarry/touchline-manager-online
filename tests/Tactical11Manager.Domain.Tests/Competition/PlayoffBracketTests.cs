using FluentAssertions;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.Competition;

/// <summary>The promotion play-off bracket the final tables draw (`PR-4`).</summary>
public sealed class PlayoffBracketTests
{
    [Fact]
    public void One_feeder_gives_a_semi_final_and_a_final()
    {
        var top = League(1, null);
        var second = League(2, top.DivisionId);

        var pairings = PlayoffBracket.Draw([top, second]);

        pairings.Should().HaveCount(2);

        var semi = pairings[0];
        semi.Stage.Should().Be(PlayoffStage.SemiFinal);
        semi.Slot.Should().Be(WorldRuleSet.PlayoffSemiSlot);
        semi.HostDivisionId.Should().Be(second.DivisionId, "the semi-final is hosted in the lower league");
        semi.HomeClubId.Should().Be(second.RankedClubIds[2], "the 3rd is at home");
        semi.HomePlace.Should().Be(3);
        semi.AwayClubId.Should().Be(second.RankedClubIds[3]);
        semi.AwayPlace.Should().Be(4);
        semi.ParentDivisionId.Should().Be(top.DivisionId);
        semi.FeederDivisionId.Should().Be(second.DivisionId);

        var final = pairings[1];
        final.Stage.Should().Be(PlayoffStage.Final);
        final.Slot.Should().Be(WorldRuleSet.PlayoffFinalSlot);
        final.HostDivisionId.Should().Be(top.DivisionId, "the final is hosted in the higher league");
        final.HomeClubId.Should().Be(top.RankedClubIds[13], "the 14th is at home");
        final.HomePlace.Should().Be(14);
        final.AwayClubId.Should().BeNull("the away side is the semi-final winner, not known yet");
        final.AwayDivisionId.Should().BeNull();
        final.AwayPlace.Should().BeNull();
    }

    [Fact]
    public void A_one_tier_country_draws_nothing()
    {
        PlayoffBracket.Draw([League(1, null)]).Should().BeEmpty();
    }

    [Fact]
    public void Two_feeders_draw_nothing_and_validate_reports_it()
    {
        var top = League(1, null);
        var secondA = League(2, top.DivisionId, number: 1);
        var secondB = League(2, top.DivisionId, number: 2);

        PlayoffBracket.Draw([top, secondA, secondB]).Should().BeEmpty();
        PromotionRelegation.Validate([top, secondA, secondB]).Should().NotBeEmpty();
    }

    [Fact]
    public void A_three_tier_pyramid_draws_a_bracket_for_each_pair_in_tier_order()
    {
        var top = League(1, null);
        var second = League(2, top.DivisionId);
        var third = League(3, second.DivisionId);

        var pairings = PlayoffBracket.Draw([third, top, second]);

        pairings.Select(pairing => (pairing.ParentDivisionId, pairing.Stage)).Should().Equal(
            (top.DivisionId, PlayoffStage.SemiFinal),
            (top.DivisionId, PlayoffStage.Final),
            (second.DivisionId, PlayoffStage.SemiFinal),
            (second.DivisionId, PlayoffStage.Final));
    }

    [Fact]
    public void A_league_too_small_for_a_play_off_is_a_programming_error()
    {
        var top = League(1, null);
        var tiny = League(2, top.DivisionId, count: 3);

        var act = () => PlayoffBracket.Draw([top, tiny]);

        act.Should().Throw<ArgumentException>();
    }

    private static LeagueStandings League(int tier, Guid? parent, int number = 1, int count = 16)
    {
        var divisionId = new Guid(tier, (short)number, 0, new byte[8]);
        var clubs = Enumerable.Range(0, count)
            .Select(index => new Guid(0, (short)tier, (short)number, [(byte)index, 0, 0, 0, 0, 0, 0, 0]))
            .ToList();

        return new LeagueStandings(divisionId, tier, number, parent, clubs);
    }
}
