using FluentAssertions;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>What releasing a player now costs: half the wages left on the contract (`CON-12`).</summary>
public sealed class ReleaseCostTests
{
    private static readonly DateTimeOffset SeasonEnd = new(2026, 12, 29, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Half_of_ten_weeks_at_a_thousand_is_five_thousand()
    {
        var quote = ReleaseCost.Calculate(1_000, SeasonEnd.AddDays(-70), SeasonEnd, seasonsAfterCurrent: 0, weeksPerSeason: 12);

        quote.RemainingWeeks.Should().Be(10);
        quote.PayoutMinor.Should().Be(5_000);
    }

    [Fact]
    public void Nothing_left_costs_nothing()
    {
        ReleaseCost.Calculate(1_000, SeasonEnd, SeasonEnd, 0, 12).Should().Be(new ReleaseQuote(0, 0));
        ReleaseCost.Calculate(1_000, SeasonEnd.AddDays(3), SeasonEnd, 0, 12).Should().Be(new ReleaseQuote(0, 0));
    }

    [Fact]
    public void A_part_of_a_week_counts_as_a_whole_week()
    {
        var quote = ReleaseCost.Calculate(1_000, SeasonEnd.AddDays(-71), SeasonEnd, 0, 12);

        quote.RemainingWeeks.Should().Be(11);
        quote.PayoutMinor.Should().Be(5_500);

        ReleaseCost.Calculate(1_000, SeasonEnd.AddHours(-1), SeasonEnd, 0, 12).RemainingWeeks
            .Should().Be(1, "an hour left is still a week's wage");
    }

    [Fact]
    public void A_contract_into_later_seasons_adds_whole_seasons()
    {
        var quote = ReleaseCost.Calculate(1_000, SeasonEnd.AddDays(-70), SeasonEnd, seasonsAfterCurrent: 2, weeksPerSeason: 12);

        quote.RemainingWeeks.Should().Be(10 + 24);
        quote.PayoutMinor.Should().Be(17_000);
    }

    [Fact]
    public void A_season_is_twelve_weeks_when_it_starts_on_a_tuesday()
    {
        // 34 matchdays on Tuesday, Thursday and Sunday: eleven weeks and a day to the last, then seven days of rollover.
        var window = SeasonCalendar.Window(new DateOnly(2026, 9, 29));

        ReleaseCost.WeeksPerSeason(window).Should().Be(12);
        ((window.RolloverEndsAt - window.StartsAt).TotalDays / 7).Should().BeLessThanOrEqualTo(12);
    }

    [Fact]
    public void The_season_length_is_read_from_the_calendar_whatever_day_it_starts_on()
    {
        foreach (var start in new[] { new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4) })
        {
            var window = SeasonCalendar.Window(start);

            ReleaseCost.WeeksPerSeason(window).Should().BeInRange(12, 13);
        }
    }

    [Fact]
    public void Bad_inputs_are_refused()
    {
        FluentActions.Invoking(() => ReleaseCost.Calculate(-1, SeasonEnd, SeasonEnd, 0, 12))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ReleaseCost.Calculate(1, SeasonEnd, SeasonEnd, -1, 12))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ReleaseCost.Calculate(1, SeasonEnd, SeasonEnd, 0, 0))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
