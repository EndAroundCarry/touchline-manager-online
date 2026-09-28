using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The finance module's read projections (master plan §6.8, `FIN-3`, `FIN-7`).
/// </summary>
/// <remarks>
/// Every query here is one round trip shaped for one caller, and none of them loads an aggregate graph.
/// Gate revenue is posted inside a matchday publication, so its basis read is one query for the whole
/// division; the weekly run touches every club, so its obligations read is two queries for the world.
/// </remarks>
internal sealed class FinanceQueries : IFinanceQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public FinanceQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubRevenueBasis>> GetMatchdayRevenueBasisAsync(
        Guid matchdayId,
        CancellationToken cancellationToken)
    {
        var divisionSeasonId = await _dbContext.Matchdays
            .Where(matchday => matchday.Id == matchdayId)
            .Select(matchday => (Guid?)matchday.DivisionSeasonId)
            .FirstOrDefaultAsync(cancellationToken);

        if (divisionSeasonId is null)
        {
            return [];
        }

        return await (
            from entry in _dbContext.ClubSeasonEntries
            join club in _dbContext.Clubs on entry.ClubId equals club.Id
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where entry.DivisionSeasonId == divisionSeasonId.Value
            select new ClubRevenueBasis(club.Id, club.StadiumBaseline, division.TierNumber))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubWeeklyObligations>> GetWeeklyObligationsAsync(
        CancellationToken cancellationToken)
    {
        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (world is null)
        {
            return [];
        }

        var seasonId = await _dbContext.Seasons
            .Where(season => season.WorldId == world.Id && season.SequenceNumber == world.CurrentSeasonNumber)
            .Select(season => (Guid?)season.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (seasonId is null)
        {
            return [];
        }

        var tiers = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where divisionSeason.SeasonId == seasonId.Value
            select new { entry.ClubId, division.TierNumber })
            .ToListAsync(cancellationToken);

        // One row per club with a payroll; a club whose squad is between contracts simply has none here and
        // is treated as owing nothing (which the squad legality rules make a temporary state, not a normal one).
        var wages = await _dbContext.PlayerContracts
            .Where(contract => contract.Status == ContractStatus.Active)
            .GroupBy(contract => contract.ClubId)
            .Select(group => new
            {
                ClubId = group.Key,
                Total = group.Sum(contract => contract.WeeklyWageMinor),
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        var wageByClub = wages.ToDictionary(row => row.ClubId);

        return
        [
            .. tiers
                .OrderBy(row => row.ClubId)
                .Select(row => new ClubWeeklyObligations(
                    row.ClubId,
                    row.TierNumber,
                    wageByClub.TryGetValue(row.ClubId, out var wage) ? wage.Total : 0,
                    wageByClub.TryGetValue(row.ClubId, out var counted) ? counted.Count : 0)),
        ];
    }

    /// <inheritdoc />
    public async Task<FinanceSummarySnapshot?> GetFinanceSummaryAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.ClubAccounts
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new { candidate.CashMinor, candidate.ReservedMinor })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var wage = await _dbContext.PlayerContracts
            .Where(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active)
            .GroupBy(contract => contract.ClubId)
            .Select(group => new
            {
                Total = group.Sum(contract => contract.WeeklyWageMinor),
                Count = group.Count(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        var placement = world is null
            ? null
            : await (
                from entry in _dbContext.ClubSeasonEntries
                join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
                join season in _dbContext.Seasons on divisionSeason.SeasonId equals season.Id
                where entry.ClubId == clubId
                    && season.WorldId == world.Id
                    && season.SequenceNumber == world.CurrentSeasonNumber
                select new { division.TierNumber, season.StartsAt, season.DisplayLabel, season.SequenceNumber })
                .FirstOrDefaultAsync(cancellationToken);

        // The two squad risks the summary warns about: how many goalkeepers the club holds, and how many
        // contracts run out at the end of the season it is playing (`SQ-2`, `CON-6`).
        var goalkeepers = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            where contract.ClubId == clubId
                && contract.Status == ContractStatus.Active
                && player.PrimaryPosition == PlayerPosition.Goalkeeper
            select contract.Id)
            .CountAsync(cancellationToken);

        var expiring = placement is null
            ? 0
            : await _dbContext.PlayerContracts.CountAsync(
                contract => contract.ClubId == clubId
                    && contract.Status == ContractStatus.Active
                    && contract.EndSeasonNumber == placement.SequenceNumber,
                cancellationToken);

        // The season's totals are the entries written since the season opened, so the opening balance and any
        // earlier season's moves are outside them. A club with no season yet sees its whole ledger.
        var totalsQuery = _dbContext.LedgerEntries.Where(entry => entry.ClubId == clubId);

        if (placement is not null)
        {
            totalsQuery = totalsQuery.Where(entry => entry.CreatedAt >= placement.StartsAt);
        }

        var totals = await totalsQuery
            .GroupBy(entry => entry.Category)
            .Select(group => new { Category = group.Key, Amount = group.Sum(entry => entry.CashDeltaMinor) })
            .ToListAsync(cancellationToken);

        return new FinanceSummarySnapshot(
            account.CashMinor,
            account.ReservedMinor,
            wage?.Total ?? 0,
            wage?.Count ?? 0,
            placement?.TierNumber ?? 1,
            placement?.DisplayLabel,
            goalkeepers,
            expiring,
            [.. totals
                .OrderBy(total => total.Category)
                .Select(total => new FinanceCategoryTotal(total.Category, total.Amount))]);
    }

    /// <inheritdoc />
    public async Task<FinanceLedgerPage> GetFinanceLedgerAsync(
        Guid clubId,
        long? beforeSequence,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.LedgerEntries.Where(entry => entry.ClubId == clubId);

        if (beforeSequence is { } before)
        {
            query = query.Where(entry => entry.Sequence < before);
        }

        // One extra row decides whether a next page exists, so the client never has to ask for an empty page
        // just to learn the ledger ended.
        var entries = await query
            .OrderByDescending(entry => entry.Sequence)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = entries.Count > pageSize;

        if (hasMore)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        return new FinanceLedgerPage(entries, hasMore);
    }
}
