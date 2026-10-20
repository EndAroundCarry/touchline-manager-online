using FluentAssertions;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Tests.Fans;

/// <summary>
/// The weekly fan review's schedule: which Sunday an instant belongs to, and the job's key and payload (`FAN-8`).
/// </summary>
public sealed class FanReviewWeeksTests
{
    // 1 November 2026 is a Sunday.
    private static readonly DateTimeOffset Sunday = new(2026, 11, 1, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_boundary_is_sunday_at_the_reviews_time()
    {
        FanRuleSet.WeeklyReviewUtc.Should().Be(new TimeOnly(23, 30));
        Sunday.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        FanReviewWeeks.BoundaryAtOrBefore(Sunday).Should().Be(Sunday);
    }

    [Theory]
    [InlineData(2026, 11, 1, 23, 29, 2026, 10, 25)]
    [InlineData(2026, 11, 1, 23, 30, 2026, 11, 1)]
    [InlineData(2026, 11, 2, 0, 0, 2026, 11, 1)]
    [InlineData(2026, 11, 4, 12, 0, 2026, 11, 1)]
    [InlineData(2026, 11, 8, 23, 29, 2026, 11, 1)]
    [InlineData(2026, 11, 8, 23, 31, 2026, 11, 8)]
    public void The_most_recent_boundary_is_the_last_sunday_at_or_before_the_instant(
        int year, int month, int day, int hour, int minute, int boundaryYear, int boundaryMonth, int boundaryDay)
    {
        var now = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        FanReviewWeeks.BoundaryAtOrBefore(now)
            .Should().Be(new DateTimeOffset(boundaryYear, boundaryMonth, boundaryDay, 23, 30, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(2026, 11, 1, 23, 29, 2026, 11, 1)]
    [InlineData(2026, 11, 1, 23, 30, 2026, 11, 1)]
    [InlineData(2026, 11, 1, 23, 31, 2026, 11, 8)]
    [InlineData(2026, 11, 4, 12, 0, 2026, 11, 8)]
    public void The_coming_boundary_is_the_first_sunday_at_or_after_the_instant(
        int year, int month, int day, int hour, int minute, int boundaryYear, int boundaryMonth, int boundaryDay)
    {
        var now = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        FanReviewWeeks.BoundaryAtOrAfter(now)
            .Should().Be(new DateTimeOffset(boundaryYear, boundaryMonth, boundaryDay, 23, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_weeks_key_and_payload_name_the_sunday_and_read_back()
    {
        var week = new DateOnly(2026, 11, 1);

        FanReviewWeeks.WeekKey(week).Should().Be("fans.weekly-review:2026-11-01");
        FanReviewWeeks.TryReadWeek(FanReviewWeeks.PayloadFor(week), out var read).Should().BeTrue();
        read.Should().Be(week);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"date":"soon"}""")]
    public void A_payload_that_names_no_week_reads_as_none(string payload)
    {
        FanReviewWeeks.TryReadWeek(payload, out _).Should().BeFalse();
    }
}
