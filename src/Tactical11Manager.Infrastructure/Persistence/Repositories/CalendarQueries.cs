using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Calendar;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The calendar's cross-module read projections (`MOD-3`).
/// </summary>
/// <remarks>
/// Each query is one round trip bounded by the window, so a club with years of ledger and injury history
/// costs the same to read as a new one.
/// </remarks>
internal sealed class CalendarQueries : ICalendarQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public CalendarQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarLedgerRow>> GetLedgerRowsAsync(
        Guid clubId,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken) =>
        await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ClubId == clubId
                && entry.CashDeltaMinor != 0
                && entry.CreatedAt >= since
                && entry.CreatedAt < until)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Sequence)
            .Select(entry => new CalendarLedgerRow(
                entry.Id,
                entry.CorrelationId,
                entry.Category,
                entry.CashDeltaMinor,
                entry.CreatedAt,
                entry.DescriptionTemplate,
                entry.DescriptionParametersJson))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarInjuryRow>> GetInjuryRowsAsync(
        Guid clubId,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken) =>
        await (
            from record in _dbContext.PlayerUnavailabilities.AsNoTracking()
            join player in _dbContext.Players.AsNoTracking() on record.PlayerId equals player.Id
            where record.ClubId == clubId
                && record.Type == UnavailabilityType.Injury
                && ((record.StartedAt >= since && record.StartedAt < until)
                    || (record.ResolvedAt != null && record.ResolvedAt >= since && record.ResolvedAt < until))
            orderby record.StartedAt
            select new CalendarInjuryRow(
                record.Id,
                record.PlayerId,
                player.FullName,
                record.Severity,
                record.StartedAt,
                record.ResolvedAt))
            .ToListAsync(cancellationToken);
}
