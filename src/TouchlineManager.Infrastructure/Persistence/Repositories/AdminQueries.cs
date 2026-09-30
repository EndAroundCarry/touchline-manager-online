using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the operator's game-health snapshot (master plan §13, `F-46`).
/// </summary>
/// <remarks>
/// Every figure is a count or a status over rows the game already holds, so the read adds no table and
/// exposes no manager's private data. It is the first thing an on-call operator opens.
/// </remarks>
internal sealed class AdminQueries : IAdminQueries
{
    // Mirrors the states PostgresJobQueue writes to ops.jobs.
    private const string PendingStatus = "pending";
    private const string LeasedStatus = "leased";
    private const string DeadLetterStatus = "dead_letter";

    private readonly TouchlineManagerDbContext _dbContext;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public AdminQueries(TouchlineManagerDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AdminGameHealth?> GetGameHealthAsync(CancellationToken cancellationToken)
    {
        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (world is null)
        {
            return null;
        }

        var season = await _dbContext.Seasons
            .Where(candidate => candidate.WorldId == world.Id)
            .OrderByDescending(candidate => candidate.SequenceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var now = _clock.UtcNow;

        var nextKickoff = await _dbContext.Matchdays
            .Where(matchday => matchday.KickoffAt > now)
            .OrderBy(matchday => matchday.KickoffAt)
            .Select(matchday => (DateTimeOffset?)matchday.KickoffAt)
            .FirstOrDefaultAsync(cancellationToken);

        var pendingJobs = await _dbContext.Jobs
            .CountAsync(job => job.Status == PendingStatus || job.Status == LeasedStatus, cancellationToken);

        var deadLetterJobs = await _dbContext.Jobs
            .CountAsync(job => job.Status == DeadLetterStatus, cancellationToken);

        var oldestOverdueJob = await _dbContext.Jobs
            .Where(job => job.Status == PendingStatus && job.DueAt < now)
            .OrderBy(job => job.DueAt)
            .Select(job => (DateTimeOffset?)job.DueAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new AdminGameHealth(
            world.Id,
            world.Status.ToCode(),
            world.CurrentSeasonNumber,
            season?.DisplayLabel,
            season?.Status.ToCode(),
            nextKickoff,
            pendingJobs,
            deadLetterJobs,
            oldestOverdueJob,
            now);
    }
}
