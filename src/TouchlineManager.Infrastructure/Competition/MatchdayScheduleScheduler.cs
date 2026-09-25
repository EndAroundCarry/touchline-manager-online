using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Repositories;

namespace TouchlineManager.Infrastructure.Competition;

/// <summary>
/// Materialises the lock, resolution, and publication jobs every round needs (`EnsureScheduleJobs`, §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The materialiser is the one place that turns a calendar into work. It holds no deadlines of its own
/// (ADR-0003): each round's <c>lock_at</c> and <c>kickoff_at</c> are columns, and the job rows it inserts
/// carry them as their due times, so a worker that was down at kickoff runs the round late rather than
/// skipping it.
/// </para>
/// <para>
/// Every pass re-derives what should exist from the calendar and the clock, so there is no state to lose
/// and nothing to resume: a restart, a redeploy, and a scaling event all produce the same three keys per
/// round, and the queue's unique business key refuses the duplicates. That is what makes it safe to run
/// this every few minutes forever.
/// </para>
/// <para>
/// A round that has already staged but not published gets its publication job re-ensured, which is the
/// recovery path for a worker that died after staging the ninth fixture — the enqueue normally happens in
/// the resolution transaction, and this covers the case where the transaction committed but the job was
/// then lost to an operator action.
/// </para>
/// </remarks>
internal sealed partial class MatchdayScheduleScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly MatchdayOptions _options;
    private readonly ILogger<MatchdayScheduleScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public MatchdayScheduleScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<MatchdayOptions> options,
        ILogger<MatchdayScheduleScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableMatchdayWorker)
        {
            LogDisabled();

            return;
        }

        LogStarted(_options.CheckIntervalSeconds, _options.MaterializeHorizonDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed pass must not kill the loop: the next one re-derives the same work.
                LogFailed(exception);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var horizon = now.AddDays(_options.MaterializeHorizonDays);

        await using var scope = _scopeFactory.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var season = await CurrentSeasonQuery.ResolveAsync(dbContext, cancellationToken);

        if (season is null)
        {
            return;
        }

        // Every round of the season that is still open: one whose kickoff is inside the horizon, and one
        // whose kickoff has already passed — a matchday that was missed is caught up, never skipped.
        var rounds = await (
            from matchday in dbContext.Matchdays
            join divisionSeason in dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            where divisionSeason.SeasonId == season.SeasonId
                && matchday.KickoffAt <= horizon
                && matchday.PublicationStatus != MatchdayPublicationStatus.Published
            orderby matchday.KickoffAt
            select new
            {
                matchday.Id,
                matchday.LockAt,
                matchday.KickoffAt,
                matchday.PublicationStatus,
            })
            .ToListAsync(cancellationToken);

        var inserted = 0;

        foreach (var round in rounds)
        {
            if (round.PublicationStatus == MatchdayPublicationStatus.Pending)
            {
                inserted += await EnqueueAsync(
                    queue,
                    MatchdayJobTypes.Lock,
                    MatchdayJobTypes.LockKey(round.Id),
                    DueAt(round.LockAt, now),
                    round.Id,
                    cancellationToken) ? 1 : 0;

                inserted += await EnqueueAsync(
                    queue,
                    MatchdayJobTypes.Resolve,
                    MatchdayJobTypes.ResolveKey(round.Id),
                    DueAt(round.KickoffAt, now),
                    round.Id,
                    cancellationToken) ? 1 : 0;

                continue;
            }

            // Staged: resolution normally enqueues publication in its own transaction, and this re-ensures
            // it. A round that is staged with no job to publish it would otherwise sit invisible with nine
            // results nobody could see (MAT-7).
            inserted += await EnqueueAsync(
                queue,
                MatchdayJobTypes.Publish,
                MatchdayJobTypes.PublishKey(round.Id),
                now,
                round.Id,
                cancellationToken) ? 1 : 0;
        }

        if (inserted > 0)
        {
            LogMaterialised(rounds.Count, inserted);
        }
    }

    /// <summary>
    /// The instant a job becomes due: its deadline, or now when the deadline has already passed.
    /// </summary>
    /// <remarks>
    /// Clamping rather than scheduling in the past keeps the queue's ready-job query honest — a due time is
    /// when the work may start, and work that is already late may start at once.
    /// </remarks>
    private static DateTimeOffset DueAt(DateTimeOffset deadline, DateTimeOffset now) =>
        deadline < now ? now : deadline;

    private static Task<bool> EnqueueAsync(
        IJobQueue queue,
        string jobType,
        string businessKey,
        DateTimeOffset dueAt,
        Guid matchdayId,
        CancellationToken cancellationToken) =>
        queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = jobType,
                BusinessKey = businessKey,
                DueAt = dueAt,
                PayloadJson = MatchdayJobPayload.For(matchdayId),
            },
            cancellationToken);

    [LoggerMessage(
        EventId = 3300,
        Level = LogLevel.Information,
        Message = "The matchday worker is disabled; no lock, resolution, or publication job will be materialised (ADR-0003).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3301,
        Level = LogLevel.Information,
        Message = "Matchday scheduler started, checking every {IntervalSeconds} seconds for rounds within {HorizonDays} days.")]
    private partial void LogStarted(int intervalSeconds, int horizonDays);

    [LoggerMessage(
        EventId = 3302,
        Level = LogLevel.Information,
        Message = "Materialised {Inserted} job(s) for {RoundCount} open round(s).")]
    private partial void LogMaterialised(int roundCount, int inserted);

    [LoggerMessage(
        EventId = 3303,
        Level = LogLevel.Error,
        Message = "The matchday scheduler failed to materialise the calendar's jobs.")]
    private partial void LogFailed(Exception exception);
}
