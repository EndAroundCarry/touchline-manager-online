using Tactical11Manager.Application.Abstractions.Calendar;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Contracts.Calendar;
using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Application.Calendar;

/// <summary>
/// Puts the sponsor and objective deadline on the calendar of a club that has not yet signed (`SPN-4`).
/// </summary>
/// <remarks>
/// It adds one kind of event and changes nothing else, which is what the calendar's extension point is for. The deadline is a finance event: the
/// calendar has no kind of its own for it, and what the manager does about it is on the finance pages. Once the club has signed there is nothing
/// left to decide, so the event goes away; the money the deal pays is already on the calendar through the ledger.
/// </remarks>
public sealed class SponsorCalendarSource : ICalendarEventSource
{
    private readonly ISponsorQueries _queries;
    private readonly ISponsorRepository _sponsors;

    /// <summary>Initializes the source.</summary>
    public SponsorCalendarSource(ISponsorQueries queries, ISponsorRepository sponsors)
    {
        _queries = queries;
        _sponsors = sponsors;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        Guid clubId,
        CalendarWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        var context = await _queries.GetClubContextAsync(clubId, cancellationToken);

        if (context is null)
        {
            return [];
        }

        var deadline = SponsorDeadline.LockAtFor(context.FirstMatchday);

        if (!window.Contains(deadline))
        {
            return [];
        }

        if (await _sponsors.FindDealAsync(clubId, context.SeasonId, cancellationToken) is not null
            || (await _sponsors.ListOffersAsync(clubId, context.SeasonId, cancellationToken)).Count == 0)
        {
            return [];
        }

        return
        [
            new CalendarEvent(
                $"sponsor-deadline:{context.SeasonId:N}",
                CalendarEventKinds.Finance,
                deadline,
                "Sponsor and objective deadline",
                "Choose a sponsor offer and a season objective, or the board signs the balanced offer for you",
                "/finances"),
        ];
    }
}
