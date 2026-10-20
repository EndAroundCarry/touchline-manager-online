using FluentAssertions;
using Tactical11Manager.Domain.Market;

namespace Tactical11Manager.Domain.Tests.Market;

/// <summary>When a free-agent week begins and ends (`FA-5`).</summary>
public sealed class FreeAgentWeeksTests
{
    private static readonly DateOnly Monday = new(2026, 9, 21);

    [Fact]
    public void A_week_opens_on_its_monday_at_the_auction_window_and_closes_a_week_later()
    {
        FreeAgentWeeks.OpensAt(Monday).Should().Be(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
        FreeAgentWeeks.EndsAt(Monday).Should().Be(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(2026, 9, 21, 12, 0, 21)]
    [InlineData(2026, 9, 21, 11, 59, 14)]
    [InlineData(2026, 9, 23, 12, 0, 21)]
    [InlineData(2026, 9, 27, 23, 59, 21)]
    [InlineData(2026, 9, 28, 12, 0, 28)]
    public void The_current_week_is_the_most_recent_monday_that_has_opened(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int expectedDay)
    {
        var instant = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        FreeAgentWeeks.WeekStartAtOrBefore(instant).Should().Be(new DateOnly(2026, 9, expectedDay));
    }
}
