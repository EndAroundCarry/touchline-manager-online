using Tactical11Manager.Application.Abstractions.Calendar;
using Tactical11Manager.Contracts.Calendar;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Calendar;

/// <summary>
/// Puts the day a player is injured, and the day the injury clears, on the calendar (`DIS-1`).
/// </summary>
/// <remarks>
/// An injury is measured in fixtures, not days, so it has a start and a recovery but no schedule: nothing is
/// projected ahead. Treatment, once the game has it, is a separate source of its own.
/// </remarks>
public sealed class InjuryCalendarSource : ICalendarEventSource
{
    private readonly ICalendarQueries _queries;

    /// <summary>Initializes the source.</summary>
    public InjuryCalendarSource(ICalendarQueries queries) => _queries = queries;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        Guid clubId,
        CalendarWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        var rows = await _queries.GetInjuryRowsAsync(clubId, window.From, window.To, cancellationToken);
        var events = new List<CalendarEvent>();

        foreach (var row in rows)
        {
            var link = $"/players/{row.PlayerId}";

            if (window.Contains(row.StartedAt))
            {
                events.Add(new CalendarEvent(
                    $"injury:{row.Id}",
                    CalendarEventKinds.Injury,
                    row.StartedAt,
                    $"{row.PlayerName} injured",
                    $"{Severity(row.Severity)} injury",
                    link));
            }

            if (row.ResolvedAt is { } resolvedAt && window.Contains(resolvedAt))
            {
                events.Add(new CalendarEvent(
                    $"recovery:{row.Id}",
                    CalendarEventKinds.Recovery,
                    resolvedAt,
                    $"{row.PlayerName} is fit again",
                    $"Back from a {Severity(row.Severity).ToLowerInvariant()} injury",
                    link));
            }
        }

        return events;
    }

    private static string Severity(InjurySeverity severity) => severity switch
    {
        InjurySeverity.Minor => "Minor",
        InjurySeverity.Moderate => "Moderate",
        InjurySeverity.Major => "Major",
        _ => "An",
    };
}
