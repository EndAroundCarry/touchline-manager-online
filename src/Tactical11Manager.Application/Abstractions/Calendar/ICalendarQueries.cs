using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Abstractions.Calendar;

/// <summary>One cash movement in a club's ledger, as the calendar needs it.</summary>
/// <param name="Id">The ledger entry identity.</param>
/// <param name="CorrelationId">The key the whole operation shares, so one operation's lines group into one event.</param>
/// <param name="Category">What the entry is.</param>
/// <param name="CashDeltaMinor">The signed change to the club's cash.</param>
/// <param name="CreatedAt">When the entry was written.</param>
/// <param name="DescriptionTemplate">The stable template key.</param>
/// <param name="DescriptionParametersJson">The template's stored parameters.</param>
public sealed record CalendarLedgerRow(
    Guid Id,
    string CorrelationId,
    LedgerCategory Category,
    long CashDeltaMinor,
    DateTimeOffset CreatedAt,
    string DescriptionTemplate,
    string DescriptionParametersJson);

/// <summary>One injury that started or cleared in a window, as the calendar needs it.</summary>
/// <param name="Id">The absence record identity.</param>
/// <param name="PlayerId">The injured player.</param>
/// <param name="PlayerName">The player's full name.</param>
/// <param name="Severity">How serious the injury is.</param>
/// <param name="StartedAt">When the injury began.</param>
/// <param name="ResolvedAt">When it cleared, or null while the player is still out.</param>
public sealed record CalendarInjuryRow(
    Guid Id,
    Guid PlayerId,
    string PlayerName,
    InjurySeverity Severity,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>
/// The narrow read projections the calendar's sources share (`MOD-3`).
/// </summary>
/// <remarks>
/// A cross-module read, so it only reads: each method returns the rows of one other module that overlap the
/// window, shaped for the calendar and nothing else. A source that can be built from a module's existing
/// query port, like the fixtures, does not need an entry here.
/// </remarks>
public interface ICalendarQueries
{
    /// <summary>Reads the club's cash movements written inside the window, oldest first.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="since">The start of the window, inclusive.</param>
    /// <param name="until">The end of the window, exclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CalendarLedgerRow>> GetLedgerRowsAsync(
        Guid clubId,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken);

    /// <summary>Reads the club's injuries that started or cleared inside the window.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="since">The start of the window, inclusive.</param>
    /// <param name="until">The end of the window, exclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CalendarInjuryRow>> GetInjuryRowsAsync(
        Guid clubId,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken);
}
