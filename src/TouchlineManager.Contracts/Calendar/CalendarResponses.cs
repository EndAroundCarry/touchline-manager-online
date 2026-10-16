namespace TouchlineManager.Contracts.Calendar;

/// <summary>
/// Stable machine-readable error codes for the calendar read.
/// </summary>
public static class CalendarErrorCodes
{
    /// <summary>The requested window is malformed, reversed, or longer than the calendar serves.</summary>
    public const string InvalidRange = "CALENDAR_INVALID_RANGE";
}

/// <summary>
/// The stable codes for what kind of thing a calendar event is.
/// </summary>
/// <remarks>
/// The set is open: a client renders a kind it does not know with a generic marker and the event's own title,
/// so a new source can ship its events before any screen learns its name. These are the kinds the server
/// emits today.
/// </remarks>
public static class CalendarEventKinds
{
    /// <summary>One of the club's league matches.</summary>
    public const string Match = "match";

    /// <summary>Money moved in the club's ledger, or a finance run is due.</summary>
    public const string Finance = "finance";

    /// <summary>A player picked up an injury.</summary>
    public const string Injury = "injury";

    /// <summary>A player's injury cleared.</summary>
    public const string Recovery = "recovery";
}

/// <summary>
/// Whether a calendar event has already happened.
/// </summary>
public static class CalendarEventStates
{
    /// <summary>The event is still ahead of the game clock.</summary>
    public const string Upcoming = "upcoming";

    /// <summary>The event has happened.</summary>
    public const string Done = "done";
}

/// <summary>
/// One thing that happens, or happened, on a day in the manager's club's calendar.
/// </summary>
/// <remarks>
/// The text is finished prose from the server, so a new kind of event needs no client change to be read. The
/// amount is carried apart from the prose in minor units, because the client owns how a currency is shown.
/// </remarks>
/// <param name="Id">A stable identity for the event, unique within the response, e.g. <c>fixture:{id}</c>.</param>
/// <param name="Kind">What the event is, one of <see cref="CalendarEventKinds"/> or a kind added later.</param>
/// <param name="At">The instant it happens, in UTC. The client files it under its own local day.</param>
/// <param name="State">Whether it is <c>upcoming</c> or <c>done</c> against the game clock.</param>
/// <param name="Title">The one-line headline.</param>
/// <param name="Detail">A second line of context, or null.</param>
/// <param name="Link">An app route that opens the thing the event is about, or null.</param>
/// <param name="AmountMinor">A signed cash amount in minor units, or null when the event moves no money.</param>
/// <param name="ResultMatchId">
/// The match whose result the <paramref name="Detail"/> gives away, or null. The client holds the detail back
/// while the manager has not yet seen that result, as it does everywhere else a result appears.
/// </param>
public sealed record CalendarEventResponse(
    string Id,
    string Kind,
    DateTimeOffset At,
    string State,
    string Title,
    string? Detail,
    string? Link,
    long? AmountMinor,
    Guid? ResultMatchId);

/// <summary>
/// The events in a window of the manager's club's calendar.
/// </summary>
/// <param name="From">The start of the window, inclusive, in UTC.</param>
/// <param name="To">The end of the window, exclusive, in UTC.</param>
/// <param name="Events">The events inside the window, oldest first.</param>
/// <param name="ServerTime">The game clock's current instant, which is what "today" means on the page.</param>
public sealed record CalendarResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<CalendarEventResponse> Events,
    DateTimeOffset ServerTime);
