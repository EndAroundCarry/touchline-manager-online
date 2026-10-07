using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The stadium read projection (`STAD-1`).
/// </summary>
internal sealed class StadiumQueries : IStadiumQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public StadiumQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<ClubStadiumContext?> GetContextAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.ClubAccounts
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new { candidate.CashMinor, candidate.ReservedMinor })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        // A club with no season yet prices as a tier-1 club, which is what the finance summary does too, so the
        // two screens never quote the same club in different scales.
        var tier = world is null
            ? (int?)null
            : await (
                from entry in _dbContext.ClubSeasonEntries
                join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
                join season in _dbContext.Seasons on divisionSeason.SeasonId equals season.Id
                where entry.ClubId == clubId
                    && season.WorldId == world.Id
                    && season.SequenceNumber == world.CurrentSeasonNumber
                select (int?)division.TierNumber)
                .FirstOrDefaultAsync(cancellationToken);

        var colours = await _dbContext.Clubs
            .Where(candidate => candidate.Id == clubId)
            .Select(candidate => new { candidate.PrimaryColour, candidate.SecondaryColour })
            .FirstOrDefaultAsync(cancellationToken);

        return new ClubStadiumContext(
            tier ?? 1,
            account.CashMinor,
            account.ReservedMinor,
            colours?.PrimaryColour,
            colours?.SecondaryColour);
    }
}
