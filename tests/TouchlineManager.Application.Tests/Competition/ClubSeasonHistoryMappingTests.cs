using FluentAssertions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Competition;

namespace TouchlineManager.Application.Tests.Competition;

/// <summary>
/// The club season-history projection: the seasons the rollover closed, and the next season a club is already
/// placed in (`PR-4`, `PR-5`, `PR-6`).
/// </summary>
public sealed class ClubSeasonHistoryMappingTests
{
    [Fact]
    public void The_history_carries_each_finished_season_and_the_next_one()
    {
        var clubId = Guid.CreateVersion7();
        var divisionId = Guid.CreateVersion7();
        var serverTime = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        var snapshot = new ClubSeasonHistorySnapshot(
            clubId,
            "Northhaven Athletic",
            "NOR",
            [
                new ClubSeasonHistoryRow(2, "2027/28", 1, "Northern Premier", 3, true, false, 1_234_000, 72),
                new ClubSeasonHistoryRow(1, "2026/27", 2, "Northern First", 4, false, false, 900_000, 65),
            ],
            new NextSeasonSummary(3, "2028/29", serverTime, "National Division", 1, SeasonMovements.Promoted));

        var response = snapshot.ToResponse(serverTime);

        response.ClubId.Should().Be(clubId);
        response.ClubName.Should().Be("Northhaven Athletic");
        response.ClubShortName.Should().Be("NOR");

        response.Seasons.Should().HaveCount(2, "PR-6: every closed season is a row");
        response.Seasons[0].SeasonNumber.Should().Be(2, "the newest season is first");
        response.Seasons[0].FinalRank.Should().Be(3);
        response.Seasons[0].Promoted.Should().BeTrue();
        response.Seasons[0].ClosingCashMinor.Should().Be(1_234_000);
        response.Seasons[0].ClosingReputation.Should().Be(72);
        response.Seasons[1].SeasonNumber.Should().Be(1);
        response.Seasons[1].TierNumber.Should().Be(2);
        response.Seasons[1].DivisionName.Should().Be("Northern First");

        response.NextSeason.Should().NotBeNull("PR-5: the next season is visible once the club is placed");
        response.NextSeason!.Movement.Should().Be(SeasonMovements.Promoted);
        response.NextSeason.DivisionName.Should().Be("National Division");
        response.NextSeason.StartsAt.Should().Be(serverTime);

        response.ServerTime.Should().Be(serverTime, "TIME-5: the response carries the server's instant");
    }

    [Fact]
    public void The_next_season_is_absent_when_the_rollover_has_not_placed_the_club()
    {
        var snapshot = new ClubSeasonHistorySnapshot(
            Guid.CreateVersion7(),
            "Northhaven Athletic",
            "NOR",
            [new ClubSeasonHistoryRow(1, "2026/27", 1, "Northern Premier", 10, false, false, 500_000, 60)],
            NextSeason: null);

        var response = snapshot.ToResponse(DateTimeOffset.UtcNow);

        response.NextSeason.Should().BeNull("the next season does not exist outside the rollover window");
    }
}
