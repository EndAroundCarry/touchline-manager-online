using Microsoft.EntityFrameworkCore;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>The season a read is measured against.</summary>
/// <param name="SeasonId">The season's identity.</param>
/// <param name="SequenceNumber">The season's sequence number, which fixes remaining contract terms.</param>
/// <param name="GameYear">The season's game year, which fixes each player's age (`TIME-3`).</param>
internal sealed record CurrentSeason(Guid SeasonId, int SequenceNumber, int GameYear);

/// <summary>
/// Resolves the season being played, shared by every read that measures against it.
/// </summary>
/// <remarks>
/// Filtering on the world's current season number rather than on the latest season keeps the answer stable
/// during the rollover window, when next season's rows already exist (`CON-8`, `CAL-6`). It is one query
/// used by the squad, contract, and tactics reads, so a change to how the current season is decided is
/// made once.
/// </remarks>
internal static class CurrentSeasonQuery
{
    /// <summary>Finds the world's current season, or null when no world or season exists.</summary>
    /// <param name="dbContext">The context to read through.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<CurrentSeason?> ResolveAsync(
        TouchlineManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var world = await dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (world is null)
        {
            return null;
        }

        var season = await dbContext.Seasons
            .Where(candidate => candidate.WorldId == world.Id
                && candidate.SequenceNumber == world.CurrentSeasonNumber)
            .Select(candidate => new { candidate.Id, candidate.SequenceNumber, candidate.GameYear })
            .FirstOrDefaultAsync(cancellationToken);

        return season is null
            ? null
            : new CurrentSeason(season.Id, season.SequenceNumber, season.GameYear);
    }
}
