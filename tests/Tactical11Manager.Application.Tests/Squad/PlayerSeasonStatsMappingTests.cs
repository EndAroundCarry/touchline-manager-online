using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Squad;

namespace Tactical11Manager.Application.Tests.Squad;

/// <summary>
/// The player profile's season line: the same projection the division leaderboard reads, narrowed to one
/// player (`STA-2`, `STA-5`).
/// </summary>
/// <remarks>
/// The one conversion with real behaviour is the average rating: it is stored in basis points and read on a
/// 0.0–10.0 scale, and a player who has not been rated has none rather than a zero (`TRN-8`).
/// </remarks>
public sealed class PlayerSeasonStatsMappingTests
{
    [Fact]
    public void A_season_line_converts_its_average_rating_to_the_display_scale()
    {
        var stat = new SquadSeasonStatRow(
            Appearances: 3,
            Starts: 2,
            MinutesPlayed: 200,
            Goals: 2,
            Assists: 1,
            Shots: 6,
            ShotsOnTarget: 3,
            Saves: 0,
            PassesAttempted: 120,
            PassesCompleted: 96,
            DribblesAttempted: 14,
            DribblesCompleted: 7,
            TacklesAttempted: 31,
            TacklesWon: 17,
            CrossesAttempted: 22,
            CrossesCompleted: 9,
            PenaltiesTaken: 3,
            PenaltiesScored: 2,
            PenaltiesFaced: 4,
            PenaltiesSaved: 1,
            FreeKicksTaken: 8,
            FreeKicksScored: 2,
            ShotsOnTargetFaced: 19,
            YellowCards: 1,
            RedCards: 0,
            AverageRatingBasisPoints: 7_500);

        var response = stat.ToResponse();

        response.Appearances.Should().Be(3);
        response.Goals.Should().Be(2);
        response.YellowCards.Should().Be(1);
        response.PassesAttempted.Should().Be(120);
        response.PassesCompleted.Should().Be(96);
        response.DribblesAttempted.Should().Be(14);
        response.DribblesCompleted.Should().Be(7);
        response.TacklesAttempted.Should().Be(31);
        response.TacklesWon.Should().Be(17);
        response.CrossesAttempted.Should().Be(22);
        response.CrossesCompleted.Should().Be(9);
        response.PenaltiesTaken.Should().Be(3);
        response.PenaltiesScored.Should().Be(2);
        response.PenaltiesFaced.Should().Be(4);
        response.PenaltiesSaved.Should().Be(1);
        response.FreeKicksTaken.Should().Be(8);
        response.FreeKicksScored.Should().Be(2);
        response.ShotsOnTargetFaced.Should().Be(19);
        response.AverageRating.Should().Be(7.5m, "TRN-8: the rating crosses the wire on its display scale");
    }

    [Fact]
    public void An_unrated_player_has_no_average_rather_than_a_zero()
    {
        var stat = new SquadSeasonStatRow(
            1, 1, 90, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, AverageRatingBasisPoints: null);

        stat.ToResponse().AverageRating.Should().BeNull("STA-5: a player with no rated appearance has no average");
    }
}
