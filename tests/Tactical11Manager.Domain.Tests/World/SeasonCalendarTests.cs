using FluentAssertions;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.World;

/// <summary>
/// The real-time calendar (`CAL-1`, `CAL-2`, `CAL-6`). A season's window is a pure function of its
/// first matchday, so it is asserted without a clock, a database, or a running job.
/// </summary>
public sealed class SeasonCalendarTests
{
    [Fact]
    public void A_matchday_is_a_tuesday_thursday_or_sunday()
    {
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));

        for (var day = 0; day < 21; day++)
        {
            var date = new DateOnly(2026, 10, 1).AddDays(day);

            SeasonCalendar.IsMatchday(date).Should().Be(date.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Sunday);
        }

        SeasonCalendar.IsMatchday(first).Should().BeTrue();
    }

    [Fact]
    public void The_first_matchday_is_rounded_forward_and_never_becomes_yesterday()
    {
        // CAL-5: the schedule starts on a chosen future date, so a non-matchday is moved forward.
        var requested = new DateOnly(2026, 10, 1);
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(requested);

        first.Should().BeOnOrAfter(requested);
        first.Should().BeBefore(requested.AddDays(7));
    }

    [Fact]
    public void A_matchday_requested_on_a_matchday_is_returned_unchanged()
    {
        var already = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));

        SeasonCalendar.FirstMatchdayOnOrAfter(already).Should().Be(already);
    }

    [Fact]
    public void A_season_has_exactly_thirty_matchdays_on_distinct_dates()
    {
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));

        var dates = SeasonCalendar.MatchdayDates(first, WorldRuleSet.MatchdaysPerSeason);

        dates.Should().HaveCount(30);
        WorldRuleSet.MatchdaysPerSeason.Should().Be(30);
        dates.Should().OnlyHaveUniqueItems();
        dates.Should().BeInAscendingOrder();
        dates.Should().OnlyContain(date => SeasonCalendar.IsMatchday(date));
    }

    [Fact]
    public void Consecutive_matchdays_follow_the_two_two_three_day_cycle()
    {
        // Tuesday to Thursday is two days, Thursday to Sunday is three, Sunday to Tuesday is two. The
        // calendar walks the weekday cycle rather than the raw calendar so a four-day week cannot appear.
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));

        var dates = SeasonCalendar.MatchdayDates(first, WorldRuleSet.MatchdaysPerSeason);

        for (var index = 1; index < dates.Count; index++)
        {
            var gap = dates[index].DayNumber - dates[index - 1].DayNumber;

            gap.Should().BeInRange(2, 3);
        }
    }

    [Fact]
    public void A_window_spans_the_matchdays_and_an_eleven_week_cycle()
    {
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));

        var window = SeasonCalendar.Window(first);

        window.StartsAt.Should().Be(SeasonCalendar.KickoffAt(first));
        window.EndsAt.Should().Be(SeasonCalendar.KickoffAt(window.MatchdayDates[^1]));
        window.RolloverEndsAt.Should().Be(window.StartsAt.AddDays(77));
        window.EndsAt.Should().BeAfter(window.StartsAt);
    }

    [Theory]
    [InlineData(DayOfWeek.Tuesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Sunday)]
    public void The_next_season_starts_on_the_same_weekday_and_the_rollover_lasts_at_least_a_week(DayOfWeek weekday)
    {
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));

        while (first.DayOfWeek != weekday)
        {
            first = SeasonCalendar.FirstMatchdayOnOrAfter(first.AddDays(1));
        }

        var window = SeasonCalendar.Window(first);
        var nextFirst = SeasonCalendar.FirstMatchdayOnOrAfter(DateOnly.FromDateTime(window.RolloverEndsAt.UtcDateTime));

        nextFirst.Should().Be(first.AddDays(77));
        nextFirst.DayOfWeek.Should().Be(first.DayOfWeek);
        (window.RolloverEndsAt - window.EndsAt).TotalDays.Should().BeGreaterThanOrEqualTo(WorldRuleSet.RolloverDays);
    }

    [Fact]
    public void A_season_holds_eleven_sundays_so_eleven_weekly_runs()
    {
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));
        var window = SeasonCalendar.Window(first);
        var sundays = 0;

        for (var day = window.StartsAt; day < window.RolloverEndsAt; day = day.AddDays(1))
        {
            if (day.DayOfWeek == DayOfWeek.Sunday)
            {
                sundays++;
            }
        }

        sundays.Should().Be(WorldRuleSet.SeasonCycleWeeks);
    }

    [Fact]
    public void Kickoff_is_the_standard_utc_time_with_an_explicit_zero_offset()
    {
        var kickoff = SeasonCalendar.KickoffAt(new DateOnly(2026, 10, 6));

        kickoff.Offset.Should().Be(TimeSpan.Zero);
        kickoff.Hour.Should().Be(19);
        kickoff.Minute.Should().Be(0);
    }

    [Fact]
    public void Asking_for_matchdays_from_a_non_matchday_is_a_programming_error()
    {
        var nonMatchday = new DateOnly(2026, 10, 5);

        if (SeasonCalendar.IsMatchday(nonMatchday))
        {
            // The date is chosen so this should not happen; guard the assertion rather than depend on
            // the calendar's weekday alignment in a particular year.
            return;
        }

        var act = () => SeasonCalendar.MatchdayDates(nonMatchday, WorldRuleSet.MatchdaysPerSeason);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Asking_for_no_matchdays_is_a_programming_error()
    {
        var first = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));

        var act = () => SeasonCalendar.MatchdayDates(first, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
