using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Calendar;
using Tactical11Manager.Application.Calendar;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Contracts.Calendar;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Tests.Calendar;

/// <summary>
/// What the finance and injury sources put on the calendar, and the window rules every source is asked for.
/// </summary>
public sealed class CalendarSourcesTests
{
    // A Monday, so the Sunday boundaries fall at known offsets.
    private static readonly DateTimeOffset Monday = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid ClubId = Guid.NewGuid();

    [Fact]
    public void With_no_bounds_the_window_is_the_clock_month_with_padding()
    {
        CalendarWindow.TryCreate(null, null, Monday, out var window).Should().BeTrue();

        window!.From.Should().Be(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
        window.To.Should().Be(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero));
        window.Now.Should().Be(Monday);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_window_with_only_one_bound_is_refused(bool hasFrom, bool hasTo)
    {
        var from = hasFrom ? Monday : (DateTimeOffset?)null;
        var to = hasTo ? Monday.AddDays(7) : (DateTimeOffset?)null;

        CalendarWindow.TryCreate(from, to, Monday, out _).Should().BeFalse();
    }

    [Fact]
    public void A_reversed_empty_or_overlong_window_is_refused()
    {
        CalendarWindow.TryCreate(Monday, Monday, Monday, out _).Should().BeFalse();
        CalendarWindow.TryCreate(Monday.AddDays(1), Monday, Monday, out _).Should().BeFalse();
        CalendarWindow.TryCreate(Monday, Monday + CalendarWindow.MaxSpan + TimeSpan.FromSeconds(1), Monday, out _)
            .Should().BeFalse();
        CalendarWindow.TryCreate(Monday, Monday + CalendarWindow.MaxSpan, Monday, out _).Should().BeTrue();
    }

    [Fact]
    public async Task One_operations_ledger_lines_are_one_weekly_event_with_the_net_amount()
    {
        var sunday = new DateTimeOffset(2026, 10, 11, 23, 0, 0, TimeSpan.Zero);
        var queries = new FakeQueries
        {
            Ledger =
            [
                Line("week-41", LedgerCategory.Sponsorship, LedgerPostings.WeeklySponsorshipTemplate, 500_000, sunday, "{\"tier\":2}"),
                Line("week-41", LedgerCategory.Wages, LedgerPostings.WagesTemplate, -800_000, sunday, "{\"playerCount\":22}"),
                Line("week-41", LedgerCategory.OperatingCost, LedgerPostings.OperatingCostTemplate, -50_000, sunday, "{}"),
            ],
        };

        var events = await new FinanceCalendarSource(queries, new Settings(false))
            .GetEventsAsync(ClubId, Window(sunday.AddDays(-3), sunday.AddDays(3)), CancellationToken.None);

        var week = events.Should().ContainSingle().Which;

        week.Kind.Should().Be(CalendarEventKinds.Finance);
        week.Title.Should().Be("Weekly finances");
        week.AmountMinor.Should().Be(-350_000);
        week.At.Should().Be(sunday);
        week.Detail.Should().Contain("Weekly player wages (22 players)");
        week.Link.Should().Be("/finances");
    }

    [Fact]
    public async Task A_single_cash_movement_is_titled_by_what_it_is_and_keeps_its_sign()
    {
        var queries = new FakeQueries
        {
            Ledger =
            [
                Line("fixture-1", LedgerCategory.GateReceipt, LedgerPostings.GateReceiptTemplate, 120_000, Monday, "{}"),
                Line("opening", LedgerCategory.OpeningBalance, LedgerPostings.OpeningBalanceTemplate, 9_000_000, Monday, "{}"),
            ],
        };

        var events = await new FinanceCalendarSource(queries, new Settings(false))
            .GetEventsAsync(ClubId, Window(Monday.AddDays(-1), Monday.AddDays(1)), CancellationToken.None);

        var gate = events.Should().ContainSingle("the opening balance is not something that happened on a day").Which;

        gate.Title.Should().Be("Home-match gate revenue");
        gate.AmountMinor.Should().Be(120_000);
        gate.Detail.Should().BeNull();
    }

    [Fact]
    public async Task The_coming_weekly_run_is_projected_only_ahead_of_the_clock_and_only_when_enabled()
    {
        var window = Window(Monday.AddDays(-14), Monday.AddDays(25));

        var enabled = await new FinanceCalendarSource(new FakeQueries(), new Settings(true))
            .GetEventsAsync(ClubId, window, CancellationToken.None);
        var disabled = await new FinanceCalendarSource(new FakeQueries(), new Settings(false))
            .GetEventsAsync(ClubId, window, CancellationToken.None);

        enabled.Select(item => item.At.Date).Should().Equal(
            new DateTime(2026, 10, 18),
            new DateTime(2026, 10, 25),
            new DateTime(2026, 11, 1));
        enabled.Should().OnlyContain(item => item.At.Hour == 23 && item.At > Monday);
        enabled.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        disabled.Should().BeEmpty();
    }

    [Fact]
    public async Task An_injury_gives_a_start_event_and_a_recovery_event_inside_the_window()
    {
        var playerId = Guid.NewGuid();
        var queries = new FakeQueries
        {
            Injuries =
            [
                new CalendarInjuryRow(
                    Guid.NewGuid(), playerId, "Ola Hansen", InjurySeverity.Moderate, Monday.AddDays(-2), Monday.AddDays(1)),
                new CalendarInjuryRow(
                    Guid.NewGuid(), Guid.NewGuid(), "Ari Koch", InjurySeverity.Minor, Monday.AddDays(-30), null),
            ],
        };

        var events = await new InjuryCalendarSource(queries)
            .GetEventsAsync(ClubId, Window(Monday.AddDays(-7), Monday.AddDays(7)), CancellationToken.None);

        events.Select(item => (item.Kind, item.Title)).Should().BeEquivalentTo(
        [
            (CalendarEventKinds.Injury, "Ola Hansen injured"),
            (CalendarEventKinds.Recovery, "Ola Hansen is fit again"),
        ]);
        events.Should().OnlyContain(item => item.Link == $"/players/{playerId}");
        events.Single(item => item.Kind == CalendarEventKinds.Injury).Detail.Should().Be("Moderate injury");
    }

    [Fact]
    public async Task An_injury_that_started_before_the_window_and_is_still_open_adds_nothing()
    {
        var queries = new FakeQueries
        {
            Injuries =
            [
                new CalendarInjuryRow(
                    Guid.NewGuid(), Guid.NewGuid(), "Ari Koch", InjurySeverity.Major, Monday.AddDays(-30), null),
            ],
        };

        var events = await new InjuryCalendarSource(queries)
            .GetEventsAsync(ClubId, Window(Monday.AddDays(-7), Monday.AddDays(7)), CancellationToken.None);

        events.Should().BeEmpty();
    }

    private static CalendarWindow Window(DateTimeOffset from, DateTimeOffset to) => new(from, to, Monday);

    private static CalendarLedgerRow Line(
        string correlation,
        LedgerCategory category,
        string template,
        long cash,
        DateTimeOffset at,
        string parameters) =>
        new(Guid.NewGuid(), correlation, category, cash, at, template, parameters);

    private sealed class Settings(bool enabled) : IWeeklyFinanceRunSettings
    {
        public bool Enabled { get; } = enabled;
    }

    private sealed class FakeQueries : ICalendarQueries
    {
        public IReadOnlyList<CalendarLedgerRow> Ledger { get; init; } = [];

        public IReadOnlyList<CalendarInjuryRow> Injuries { get; init; } = [];

        public Task<IReadOnlyList<CalendarLedgerRow>> GetLedgerRowsAsync(
            Guid clubId,
            DateTimeOffset since,
            DateTimeOffset until,
            CancellationToken cancellationToken) =>
            Task.FromResult(Ledger);

        public Task<IReadOnlyList<CalendarInjuryRow>> GetInjuryRowsAsync(
            Guid clubId,
            DateTimeOffset since,
            DateTimeOffset until,
            CancellationToken cancellationToken) =>
            Task.FromResult(Injuries);
    }
}
