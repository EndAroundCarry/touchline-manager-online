using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The structures read projection: a building's recent weeks and home matches and its season totals
/// (`STRC-4`, `STRC-6`, `STRC-8`).
/// </summary>
internal sealed class StructureQueries : IStructureQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public StructureQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<StructureHistory> GetHistoryAsync(
        Guid clubId,
        StructureKind kind,
        int weeks,
        int matches,
        CancellationToken cancellationToken)
    {
        var rows = _dbContext.ClubStructureTakings
            .AsNoTracking()
            .Where(row => row.ClubId == clubId && row.Kind == kind);

        // The newest weeks first. A week's row set is read whole and folded in memory, because "takings" and
        // "upkeep" are the positive and the negative rows of the same week and a few dozen rows is all there is.
        var weekDates = await rows
            .Select(row => row.WeekOf)
            .Distinct()
            .OrderByDescending(week => week)
            .Take(weeks)
            .ToListAsync(cancellationToken);

        var weekRows = await rows
            .Where(row => weekDates.Contains(row.WeekOf))
            .Select(row => new { row.WeekOf, row.AmountMinor })
            .ToListAsync(cancellationToken);

        var weekLines = weekRows
            .GroupBy(row => row.WeekOf)
            .Select(group => new StructureWeekLine(
                group.Key,
                group.Where(row => row.AmountMinor > 0).Sum(row => row.AmountMinor),
                -group.Where(row => row.AmountMinor < 0).Sum(row => row.AmountMinor)))
            .OrderByDescending(line => line.WeekOf)
            .ToList();

        var matchLines = kind == StructureKind.SnackBar
            ? await ReadMatchesAsync(clubId, matches, cancellationToken)
            : [];

        var seasonStart = await SeasonStartAsync(cancellationToken);
        var season = seasonStart is null ? rows : rows.Where(row => row.CreatedAt >= seasonStart.Value);

        var seasonAmounts = await season
            .Select(row => row.AmountMinor)
            .ToListAsync(cancellationToken);

        return new StructureHistory(
            weekLines,
            matchLines,
            seasonAmounts.Where(amount => amount > 0).Sum(),
            -seasonAmounts.Where(amount => amount < 0).Sum());
    }

    private async Task<IReadOnlyList<StructureMatchLine>> ReadMatchesAsync(
        Guid clubId,
        int matches,
        CancellationToken cancellationToken)
    {
        var lines = await (
            from row in _dbContext.ClubStructureTakings.AsNoTracking()
            join fixture in _dbContext.Fixtures on row.FixtureId equals fixture.Id
            join opponent in _dbContext.Clubs on fixture.AwayClubId equals opponent.Id
            where row.ClubId == clubId && row.RowKind == StructureTakingsKind.MatchSale
            orderby fixture.KickoffAt descending
            select new
            {
                FixtureId = fixture.Id,
                Opponent = opponent.Name,
                Spectators = row.Customers,
                fixture.HomeScore,
                fixture.AwayScore,
                row.BonusBp,
                row.BonusReason,
                row.AmountMinor,
            })
            .Take(matches)
            .ToListAsync(cancellationToken);

        // A sale is booked when the round is published, which is after the score is set, so a sale without
        // a score is a defect elsewhere; it is shown as 0-0 rather than hiding the takings it records.
        return lines
            .Select(line => new StructureMatchLine(
                line.FixtureId,
                line.Opponent,
                line.Spectators,
                line.HomeScore ?? 0,
                line.AwayScore ?? 0,
                line.BonusBp,
                line.BonusReason,
                line.AmountMinor))
            .ToList();
    }

    private async Task<DateTimeOffset?> SeasonStartAsync(CancellationToken cancellationToken)
    {
        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (world is null)
        {
            return null;
        }

        return await _dbContext.Seasons
            .Where(season => season.WorldId == world.Id && season.SequenceNumber == world.CurrentSeasonNumber)
            .Select(season => (DateTimeOffset?)season.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
