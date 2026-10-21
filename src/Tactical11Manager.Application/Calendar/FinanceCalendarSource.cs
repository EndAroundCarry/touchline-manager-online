using System.Globalization;
using Tactical11Manager.Application.Abstractions.Calendar;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Contracts.Calendar;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Calendar;

/// <summary>
/// Puts the club's money movements on the calendar, and the coming weekly settlement (`FIN-11`, `CON-2`).
/// </summary>
/// <remarks>
/// <para>
/// What happened comes from the ledger, grouped by the correlation key an operation shares: the weekly run
/// posts sponsorship, wages and running costs as three lines, but it is one thing that happened on one day,
/// so it is one event with the net movement. A gate receipt, a transfer, or a stadium works order is its
/// own event.
/// </para>
/// <para>
/// What is coming is the next weekly boundary, projected from the same rule the scheduler uses
/// (<see cref="WorldRuleSet.WeeklyFinanceUtc"/>, on a Sunday). It is projected only ahead of the game clock;
/// behind it, the ledger is the truth.
/// </para>
/// </remarks>
public sealed class FinanceCalendarSource : ICalendarEventSource
{
    private readonly ICalendarQueries _queries;
    private readonly IWeeklyFinanceRunSettings _settings;

    /// <summary>Initializes the source.</summary>
    public FinanceCalendarSource(ICalendarQueries queries, IWeeklyFinanceRunSettings settings)
    {
        _queries = queries;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        Guid clubId,
        CalendarWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        var rows = await _queries.GetLedgerRowsAsync(clubId, window.From, window.To, cancellationToken);
        var events = new List<CalendarEvent>();

        // An opening balance is the account being created, not something that happened to the club on a day.
        var movements = rows.Where(row =>
            row.CashDeltaMinor != 0 && row.Category != LedgerCategory.OpeningBalance);

        foreach (var group in movements.GroupBy(row => row.CorrelationId, StringComparer.Ordinal))
        {
            events.Add(ToEvent(group.OrderBy(row => row.CreatedAt).ToList()));
        }

        if (_settings.Enabled)
        {
            events.AddRange(UpcomingRuns(window));
        }

        return events;
    }

    private static CalendarEvent ToEvent(List<CalendarLedgerRow> lines)
    {
        var first = lines[0];
        var net = lines.Sum(line => line.CashDeltaMinor);
        var isWeekly = lines.Any(line => line.Category is
            LedgerCategory.Sponsorship or LedgerCategory.Wages or LedgerCategory.OperatingCost
                or LedgerCategory.Merchandise or LedgerCategory.StructureUpkeep);

        var title = isWeekly
            ? WeeklyTitle
            : LedgerEntryText.Render(first.DescriptionTemplate, first.DescriptionParametersJson);

        var detail = lines.Count > 1
            ? string.Join(
                " · ",
                lines.Select(line => LedgerEntryText.Render(line.DescriptionTemplate, line.DescriptionParametersJson)))
            : null;

        return new CalendarEvent(
            $"ledger:{first.Id}",
            CalendarEventKinds.Finance,
            first.CreatedAt,
            title,
            detail,
            "/finances",
            net);
    }

    private static IEnumerable<CalendarEvent> UpcomingRuns(CalendarWindow window)
    {
        // The first day that could hold a boundary inside the window, then every Sunday from there.
        var day = window.From.UtcDateTime.Date;

        for (; day < window.To.UtcDateTime; day = day.AddDays(1))
        {
            if (day.DayOfWeek != DayOfWeek.Sunday)
            {
                continue;
            }

            var boundary = new DateTimeOffset(day + WorldRuleSet.WeeklyFinanceUtc.ToTimeSpan(), TimeSpan.Zero);

            // Behind the clock the ledger already says what happened; an instant outside the window is not ours.
            if (boundary <= window.Now || !window.Contains(boundary))
            {
                continue;
            }

            yield return new CalendarEvent(
                string.Create(CultureInfo.InvariantCulture, $"finance-run:{day:yyyy-MM-dd}"),
                CalendarEventKinds.Finance,
                boundary,
                WeeklyTitle,
                "Sponsorship, player wages and operating costs are settled",
                "/finances");
        }
    }

    private const string WeeklyTitle = "Weekly finances";
}
