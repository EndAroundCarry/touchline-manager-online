using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Application.Ops;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.Ops;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the operator's console: the game-health snapshot and the job, matchday, and audit reads
/// (master plan §10.8, §13, `F-46`, `F-47`).
/// </summary>
/// <remarks>
/// Every figure is a status, an instant, or a projection over rows the game already holds, so the console
/// adds no table and exposes no manager's private data. It is the surface an on-call operator opens to see
/// the live game and act on it.
/// </remarks>
internal sealed class AdminQueries : IAdminQueries
{
    private readonly TouchlineManagerDbContext _dbContext;
    private readonly IReadOnlyMode _readOnlyMode;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public AdminQueries(
        TouchlineManagerDbContext dbContext,
        IReadOnlyMode readOnlyMode,
        IClock clock)
    {
        _dbContext = dbContext;
        _readOnlyMode = readOnlyMode;
        _clock = clock;
    }

    /// <inheritdoc />
    public int PageSize => 25;

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
            .CountAsync(
                job => job.Status == JobStatuses.PendingCode || job.Status == JobStatuses.LeasedCode,
                cancellationToken);

        var deadLetterJobs = await _dbContext.Jobs
            .CountAsync(job => job.Status == JobStatuses.DeadLetteredCode, cancellationToken);

        var oldestOverdueJob = await _dbContext.Jobs
            .Where(job => job.Status == JobStatuses.PendingCode && job.DueAt < now)
            .OrderBy(job => job.DueAt)
            .Select(job => (DateTimeOffset?)job.DueAt)
            .FirstOrDefaultAsync(cancellationToken);

        // The incident switch belongs on the operator's first read: it changes what the game will accept
        // (master plan §13, F-51).
        var readOnly = await _readOnlyMode.GetStateAsync(cancellationToken);

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
            now,
            readOnly.Enabled,
            readOnly.Message);
    }

    /// <inheritdoc />
    public async Task<AdminJobPage> ListJobsAsync(AdminJobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var jobs = _dbContext.Jobs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            jobs = jobs.Where(job => job.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.JobType))
        {
            jobs = jobs.Where(job => job.JobType == query.JobType);
        }

        if (query.Before is { } before)
        {
            // The identity breaks a same-instant tie: a publication enqueues several jobs at one instant.
            jobs = jobs.Where(job =>
                job.CreatedAt < before.CreatedAt
                || (job.CreatedAt == before.CreatedAt && job.Id.CompareTo(before.Id) < 0));
        }

        var rows = await jobs
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .Take(PageSize + 1)
            .Select(job => new
            {
                job.Id,
                job.JobType,
                job.BusinessKey,
                job.Status,
                job.AttemptCount,
                job.MaxAttempts,
                job.DueAt,
                job.CreatedAt,
                job.UpdatedAt,
                job.CompletedAt,
                job.LeaseOwner,
                job.LeaseUntil,
                job.LastError,
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > PageSize;
        string? nextCursor = null;

        if (hasMore)
        {
            var last = rows[PageSize - 1];
            nextCursor = AdminJobCursor.Encode(new AdminJobCursorPosition(last.CreatedAt, last.Id));
            rows.RemoveAt(rows.Count - 1);
        }

        return new AdminJobPage(
            rows.Select(job => new AdminJobSummary(
                job.Id,
                job.JobType,
                job.BusinessKey,
                job.Status,
                job.AttemptCount,
                job.MaxAttempts,
                job.DueAt,
                job.CreatedAt,
                job.UpdatedAt,
                job.CompletedAt,
                job.LeaseOwner,
                job.LeaseUntil,
                job.LastError)).ToList(),
            nextCursor);
    }

    /// <inheritdoc />
    public async Task<AdminMatchdayDetail?> GetMatchdayAsync(
        Guid matchdayId,
        CancellationToken cancellationToken)
    {
        var header = await (
            from matchday in _dbContext.Matchdays
            join divisionSeason in _dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join country in _dbContext.Countries on division.CountryId equals country.Id
            join season in _dbContext.Seasons on divisionSeason.SeasonId equals season.Id
            where matchday.Id == matchdayId
            select new
            {
                Matchday = matchday,
                Division = division,
                Country = country,
                Season = season,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var fixtureRows = await (
            from fixture in _dbContext.Fixtures
            join home in _dbContext.Clubs on fixture.HomeClubId equals home.Id
            join away in _dbContext.Clubs on fixture.AwayClubId equals away.Id
            where fixture.MatchdayId == matchdayId
            orderby fixture.KickoffAt, fixture.Id
            select new
            {
                Fixture = fixture,
                Home = home,
                Away = away,
            })
            .ToListAsync(cancellationToken);

        var fixtureIds = fixtureRows.Select(row => row.Fixture.Id).ToList();

        // The latest attempt per fixture, which is the evidence an operator reads when a round is stuck.
        var attempts = await _dbContext.SimulationAttempts
            .AsNoTracking()
            .Where(attempt => fixtureIds.Contains(attempt.FixtureId))
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .Select(attempt => new
            {
                attempt.FixtureId,
                attempt.AttemptNumber,
                attempt.Status,
                attempt.ErrorCategory,
                attempt.ErrorMessage,
                attempt.CompletedAt,
            })
            .ToListAsync(cancellationToken);

        var latestAttempts = attempts
            .GroupBy(attempt => attempt.FixtureId)
            .ToDictionary(
                group => group.Key,
                group => new AdminSimulationAttempt(
                    group.First().AttemptNumber,
                    group.First().Status.ToCode(),
                    group.First().ErrorCategory,
                    group.First().ErrorMessage,
                    group.First().CompletedAt));

        var fixtures = fixtureRows
            .Select(row => new AdminFixtureStatus(
                row.Fixture.Id,
                row.Fixture.HomeClubId,
                row.Home.Name,
                row.Fixture.AwayClubId,
                row.Away.Name,
                row.Fixture.KickoffAt,
                row.Fixture.Status.ToCode(),
                row.Fixture.HomeScore,
                row.Fixture.AwayScore,
                row.Fixture.MatchId,
                latestAttempts.GetValueOrDefault(row.Fixture.Id)))
            .ToList();

        var jobKeys = new[]
        {
            MatchdayJobTypes.LockKey(matchdayId),
            MatchdayJobTypes.ResolveKey(matchdayId),
            MatchdayJobTypes.PublishKey(matchdayId),
        };

        var jobs = await ReadJobsByBusinessKeyAsync(jobKeys, cancellationToken);

        return new AdminMatchdayDetail(
            header.Matchday.Id,
            header.Matchday.DivisionSeasonId,
            header.Matchday.RoundNumber,
            header.Matchday.LockAt,
            header.Matchday.KickoffAt,
            header.Matchday.PublicationStatus.ToCode(),
            header.Division.Id,
            header.Division.DisplayName,
            header.Division.TierNumber,
            header.Country.Code,
            header.Country.DisplayName,
            header.Season.SequenceNumber,
            header.Season.DisplayLabel,
            fixtures,
            jobs);
    }

    /// <inheritdoc />
    public async Task<AdminAuditPage> ListAuditAsync(AdminAuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var entries = _dbContext.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.ActionPrefix))
        {
            var actionPrefix = query.ActionPrefix;
            entries = entries.Where(entry => entry.Action.StartsWith(actionPrefix));
        }

        if (query.ActorUserId is { } actorUserId)
        {
            entries = entries.Where(entry => entry.ActorUserId == actorUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.TargetType))
        {
            entries = entries.Where(entry => entry.TargetType == query.TargetType);
        }

        if (query.TargetId is { } targetId)
        {
            entries = entries.Where(entry => entry.TargetId == targetId);
        }

        if (query.Before is { } before)
        {
            entries = entries.Where(entry =>
                entry.OccurredAt < before.OccurredAt
                || (entry.OccurredAt == before.OccurredAt && entry.Id.CompareTo(before.Id) < 0));
        }

        var rows = await entries
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Take(PageSize + 1)
            .Select(entry => new
            {
                entry.Id,
                entry.ActorType,
                entry.ActorUserId,
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                entry.CorrelationId,
                entry.OccurredAt,
                entry.Reason,
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > PageSize;
        string? nextCursor = null;

        if (hasMore)
        {
            var last = rows[PageSize - 1];
            nextCursor = AdminAuditCursor.Encode(new AdminAuditCursorPosition(last.OccurredAt, last.Id));
            rows.RemoveAt(rows.Count - 1);
        }

        return new AdminAuditPage(
            rows.Select(entry => new AdminAuditEntry(
                entry.Id,
                entry.ActorType,
                entry.ActorUserId,
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                entry.CorrelationId,
                entry.OccurredAt,
                entry.Reason)).ToList(),
            nextCursor);
    }

    private async Task<IReadOnlyList<AdminJobSummary>> ReadJobsByBusinessKeyAsync(
        IReadOnlyCollection<string> businessKeys,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Jobs
            .AsNoTracking()
            .Where(job => businessKeys.Contains(job.BusinessKey))
            .OrderBy(job => job.CreatedAt)
            .ThenBy(job => job.Id)
            .Select(job => new
            {
                job.Id,
                job.JobType,
                job.BusinessKey,
                job.Status,
                job.AttemptCount,
                job.MaxAttempts,
                job.DueAt,
                job.CreatedAt,
                job.UpdatedAt,
                job.CompletedAt,
                job.LeaseOwner,
                job.LeaseUntil,
                job.LastError,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(job => new AdminJobSummary(
                job.Id,
                job.JobType,
                job.BusinessKey,
                job.Status,
                job.AttemptCount,
                job.MaxAttempts,
                job.DueAt,
                job.CreatedAt,
                job.UpdatedAt,
                job.CompletedAt,
                job.LeaseOwner,
                job.LeaseUntil,
                job.LastError))
            .ToList();
    }
}
