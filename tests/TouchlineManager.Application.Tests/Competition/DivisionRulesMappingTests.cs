using FluentAssertions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Competition;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Tests.Competition;

/// <summary>
/// The competition-rules projection: the points and the ordering the table ranks by, and the stored draw
/// (`TBL-1`…`TBL-11`).
/// </summary>
/// <remarks>
/// The mapping is where "the rules page describes the rules the table follows" is decided, so it is tested
/// against the domain's own constants rather than against literals: a page that listed a different order, or
/// a different number of points, would be describing a game nobody is playing.
/// </remarks>
public sealed class DivisionRulesMappingTests
{
    [Fact]
    public void The_rules_carry_the_domains_points_the_ordered_criteria_and_the_stored_draw()
    {
        var clubId = Guid.CreateVersion7();
        var serverTime = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        var snapshot = new DivisionRulesSnapshot(
            Guid.CreateVersion7(),
            "Northern Premier",
            1,
            Guid.CreateVersion7(),
            "ENG",
            "England",
            1,
            "Season 1",
            "the-stored-seed",
            "the-stored-hash",
            [new DivisionRulesClubRow(clubId, "Northhaven Athletic", "NOR", "the-draw-key")]);

        var response = snapshot.ToResponse(serverTime);

        response.Points.Win.Should().Be(StandingsCalculator.PointsForWin, "TBL-1: a win is worth three");
        response.Points.Draw.Should().Be(StandingsCalculator.PointsForDraw);
        response.Points.Loss.Should().Be(StandingsCalculator.PointsForLoss);

        response.TieBreakers.Select(tieBreaker => tieBreaker.Code)
            .Should()
            .Equal(TieBreakers.Ordered, "the page lists the order the table applies (TBL-2…TBL-10)");

        response.TieDrawSeed.Should().Be("the-stored-seed", "TBL-11: the draw is visible");
        response.TieDrawHash.Should().Be("the-stored-hash");

        response.Clubs.Should().ContainSingle(
            "every club in the division is listed with the key its ordering would use");
        response.Clubs[0].ClubId.Should().Be(clubId);
        response.Clubs[0].DrawKey.Should().Be("the-draw-key");

        response.ServerTime.Should().Be(serverTime, "TIME-5: the response carries the server's instant");
    }
}
