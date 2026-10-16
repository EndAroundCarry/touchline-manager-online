using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Calendar;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Calendar;

namespace Tactical11Manager.Application.Calendar;

/// <summary>The result of reading the manager's calendar.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="InvalidRange">Whether the requested window was refused as malformed, reversed, or too long.</param>
/// <param name="Calendar">The calendar, when the read succeeded.</param>
public sealed record GetCalendarResult(SquadReadOutcome Outcome, bool InvalidRange, CalendarResponse? Calendar);

/// <summary>
/// Reads what is happening on each day of the club the caller manages' calendar.
/// </summary>
/// <remarks>
/// The use case knows no kind of event. It resolves the club from the tenure (`WORLD-9`), asks every
/// registered <see cref="ICalendarEventSource"/> for the window, marks each event done or upcoming against
/// the one game clock, and merges them in time order. That is the whole of the calendar's flexibility: a new
/// source registered in the composition root appears here with no change.
/// </remarks>
public sealed class GetCalendar
{
    private readonly ResolveOwnedClub _access;
    private readonly IEnumerable<ICalendarEventSource> _sources;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetCalendar(ResolveOwnedClub access, IEnumerable<ICalendarEventSource> sources, IClock clock)
    {
        _access = access;
        _sources = sources;
        _clock = clock;
    }

    /// <summary>Reads the window of the manager's calendar, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="from">The requested start, or null for the default window.</param>
    /// <param name="to">The requested end, or null for the default window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetCalendarResult> ExecuteAsync(
        Guid userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (!CalendarWindow.TryCreate(from, to, now, out var window))
        {
            return new GetCalendarResult(SquadReadOutcome.Found, true, null);
        }

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetCalendarResult(access.Outcome.ToReadOutcome(), false, null);
        }

        // Sources are independent reads over the same scoped unit of work, so they run one after another.
        var events = new List<CalendarEvent>();

        foreach (var source in _sources)
        {
            events.AddRange(await source.GetEventsAsync(access.ClubId, window, cancellationToken));
        }

        var ordered = events
            .Where(candidate => window.Contains(candidate.At))
            .OrderBy(candidate => candidate.At)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Select(candidate => new CalendarEventResponse(
                candidate.Id,
                candidate.Kind,
                candidate.At,
                candidate.At <= now ? CalendarEventStates.Done : CalendarEventStates.Upcoming,
                candidate.Title,
                candidate.Detail,
                candidate.Link,
                candidate.AmountMinor,
                candidate.ResultMatchId))
            .ToList();

        return new GetCalendarResult(
            SquadReadOutcome.Found,
            false,
            new CalendarResponse(window.From, window.To, ordered, now));
    }
}
