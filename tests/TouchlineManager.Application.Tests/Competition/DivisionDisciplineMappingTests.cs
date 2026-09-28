using FluentAssertions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Competition;

namespace TouchlineManager.Application.Tests.Competition;

/// <summary>
/// The division discipline projection: the cards and the suspension still owed (`DIS-2`…`DIS-5`).
/// </summary>
/// <remarks>
/// The mapping is where the server's row order reaches the screen, so it is tested that the rows cross in
/// the order the read ranked them and that the suspension figure is carried through unchanged — the page
/// must not re-rank a leaderboard it was handed (`TBL-12`'s principle, applied here).
/// </remarks>
public sealed class DivisionDisciplineMappingTests
{
    [Fact]
    public void The_discipline_carries_every_row_in_the_order_it_was_read()
    {
        var sentOff = Guid.CreateVersion7();
        var booked = Guid.CreateVersion7();
        var serverTime = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        var snapshot = new DivisionDisciplineSnapshot(
            Guid.CreateVersion7(),
            "Northern Premier",
            1,
            Guid.CreateVersion7(),
            "ENG",
            "England",
            1,
            "Season 1",
            [
                new DivisionDisciplineRow(sentOff, "Alaric Alderwick", Guid.CreateVersion7(), "Northhaven Athletic", "NOR", 0, 1, 1),
                new DivisionDisciplineRow(booked, "Bramwell Brambleby", Guid.CreateVersion7(), "Southfield Rovers", "SOU", 4, 0, 0),
            ]);

        var response = snapshot.ToResponse(serverTime);

        response.Rows.Should().HaveCount(2);
        response.Rows[0].PlayerId.Should().Be(sentOff, "the rows cross in the order the read ranked them");
        response.Rows[0].RedCards.Should().Be(1, "DIS-4");
        response.Rows[0].SuspensionFixturesRemaining.Should().Be(1, "DIS-5: the ban is carried unchanged");
        response.Rows[1].YellowCards.Should().Be(4, "DIS-2");
        response.Rows[1].SuspensionFixturesRemaining.Should().Be(0, "four bookings are not yet a ban");
        response.ServerTime.Should().Be(serverTime, "TIME-5: the response carries the server's instant");
    }
}
