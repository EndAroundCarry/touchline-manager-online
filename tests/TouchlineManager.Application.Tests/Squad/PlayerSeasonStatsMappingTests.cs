using FluentAssertions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Squad;

namespace TouchlineManager.Application.Tests.Squad;

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
            YellowCards: 1,
            RedCards: 0,
            AverageRatingBasisPoints: 7_500);

        var response = stat.ToResponse();

        response.Appearances.Should().Be(3);
        response.Goals.Should().Be(2);
        response.YellowCards.Should().Be(1);
        response.AverageRating.Should().Be(7.5m, "TRN-8: the rating crosses the wire on its display scale");
    }

    [Fact]
    public void An_unrated_player_has_no_average_rather_than_a_zero()
    {
        var stat = new SquadSeasonStatRow(1, 1, 90, 0, 0, 0, 0, 0, 0, 0, AverageRatingBasisPoints: null);

        stat.ToResponse().AverageRating.Should().BeNull("STA-5: a player with no rated appearance has no average");
    }
}
