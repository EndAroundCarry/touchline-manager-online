using System.Diagnostics.CodeAnalysis;

namespace Tactical11Manager.Application.Abstractions.Calendar;

/// <summary>
/// The window of the calendar being read, and the game clock it is read against.
/// </summary>
/// <remarks>
/// The calendar is a rolling window rather than a season, because the things on it do not share a season:
/// a ledger entry, an injury and a fixture each live on their own timeline. A window is also what keeps
/// every source's query bounded.
/// </remarks>
/// <param name="From">The start of the window, inclusive, in UTC.</param>
/// <param name="To">The end of the window, exclusive, in UTC.</param>
/// <param name="Now">The game clock's current instant, which separates what is done from what is coming.</param>
public sealed record CalendarWindow(DateTimeOffset From, DateTimeOffset To, DateTimeOffset Now)
{
    /// <summary>The longest window one read serves. Six weeks of a month grid is the widest a screen needs.</summary>
    public static readonly TimeSpan MaxSpan = TimeSpan.FromDays(70);

    /// <summary>How far past the month each side the default window reaches, so a month grid is covered.</summary>
    private static readonly TimeSpan DefaultPadding = TimeSpan.FromDays(8);

    /// <summary>Gets whether an instant falls inside the window.</summary>
    /// <param name="instant">The instant.</param>
    public bool Contains(DateTimeOffset instant) => instant >= From && instant < To;

    /// <summary>
    /// Builds a window from the caller's bounds, or the default one when they gave none.
    /// </summary>
    /// <remarks>
    /// With no bounds the window is the game clock's UTC month with a week and a day either side, which is
    /// enough to fill a six-week month grid whatever time zone it is drawn in, so the page can learn what
    /// "today" is and draw the right month from one read.
    /// </remarks>
    /// <param name="from">The requested start, or null.</param>
    /// <param name="to">The requested end, or null.</param>
    /// <param name="now">The game clock's current instant.</param>
    /// <param name="window">The window, when the bounds are acceptable.</param>
    /// <returns>Whether the bounds make an acceptable window.</returns>
    public static bool TryCreate(
        DateTimeOffset? from,
        DateTimeOffset? to,
        DateTimeOffset now,
        [NotNullWhen(true)] out CalendarWindow? window)
    {
        window = null;

        if (from is null && to is null)
        {
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            window = new CalendarWindow(
                monthStart - DefaultPadding,
                monthStart.AddMonths(1) + DefaultPadding,
                now);

            return true;
        }

        if (from is not { } start || to is not { } end || end <= start || end - start > MaxSpan)
        {
            return false;
        }

        window = new CalendarWindow(start.ToUniversalTime(), end.ToUniversalTime(), now);

        return true;
    }
}

/// <summary>
/// One thing a source reports for the calendar.
/// </summary>
/// <remarks>
/// A source does not say whether the event is done: it happened or it did not against the same game clock
/// for every source, so the use case decides once and no two sources can disagree about "now".
/// </remarks>
/// <param name="Id">A stable identity, unique across sources, e.g. <c>fixture:{id}</c>.</param>
/// <param name="Kind">The kind code, see <c>CalendarEventKinds</c>. A new source may introduce its own.</param>
/// <param name="At">When the event happens, in UTC.</param>
/// <param name="Title">The one-line headline.</param>
/// <param name="Detail">A second line of context, or null.</param>
/// <param name="Link">An app route that opens the thing the event is about, or null.</param>
/// <param name="AmountMinor">A signed cash amount in minor units, or null.</param>
/// <param name="ResultMatchId">The match whose result the detail gives away, or null.</param>
public sealed record CalendarEvent(
    string Id,
    string Kind,
    DateTimeOffset At,
    string Title,
    string? Detail = null,
    string? Link = null,
    long? AmountMinor = null,
    Guid? ResultMatchId = null);

/// <summary>
/// Something that contributes events to a club's calendar.
/// </summary>
/// <remarks>
/// <para>
/// This is the calendar's extension point. The calendar knows no module: it asks every registered source for
/// the window and merges what they return. A new kind of event — a finished building, an injured player's
/// treatment, a contract about to expire — is one new class that implements this interface and one line in
/// the composition root. Neither the use case, the endpoint, nor the contract changes.
/// </para>
/// <para>
/// A source reads, it never writes, and it returns only events inside the window and only for the club it is
/// asked about. A source that fails fails the read: a calendar that silently drops a whole kind of event is
/// worse than one that says it could not load.
/// </para>
/// </remarks>
public interface ICalendarEventSource
{
    /// <summary>Reads this source's events for a club inside the window.</summary>
    /// <param name="clubId">The club whose calendar is read.</param>
    /// <param name="window">The window and the game clock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        Guid clubId,
        CalendarWindow window,
        CancellationToken cancellationToken);
}
