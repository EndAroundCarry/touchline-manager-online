using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Competition.Playoffs;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Persistence.Repositories;

namespace Tactical11Manager.Infrastructure.Competition;

/// <summary>
/// Materialises the rollover job once a season is due to roll over (`PR-4`, master plan §7.2, ADR-0031).
/// </summary>
/// <remarks>
/// <para>
/// The season's own <c>ends_at</c> is the deadline: once the world's current season is active and that
/// instant has passed, the season is due to close and this enqueues the rollover. The business key
/// <c>season:{id}:rollover</c> makes a repeat free, so a materialiser that runs every few minutes inserts
/// once and a worker that was down across the deadline runs the rollover late rather than skipping it
/// (ADR-0003).
/// </para>
/// <para>
/// The season's end is matchday 30's kickoff, but the season is not finished then: the promotion play-off is played on the days after,
/// and the rollover must follow its last match (ADR-0087). The job is therefore queued only once the season is <em>settled</em> — every
/// matchday of every league published, play-off rounds included, and no tie without a winner. Projection reconciliation stays
/// preflight's business in the worker; a rollover that still meets an unplayed round there is a transient failure the job retries.
/// </para>
/// <para>
/// Worker-only, like the other schedulers: a season closes as a consequence of play, never of a client
/// command, and only the worker may run it (ADR-0001, ADR-0008).
/// </para>
/// </remarks>
internal sealed partial class SeasonRolloverScheduler : BackgroundService, IJobMaterializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly SeasonRolloverOptions _options;
    private readonly ILogger<SeasonRolloverScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public SeasonRolloverScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<SeasonRolloverOptions> options,
        ILogger<SeasonRolloverScheduler> logger)
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
        if (!_options.EnableRollover)
        {
            LogDisabled();

            return;
        }

        LogStarted(_options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaterializeAsync(_clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed pass must not kill the loop: the next one re-derives the same season.
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

    /// <inheritdoc />
    public async Task MaterializeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_options.EnableRollover)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var current = await CurrentSeasonQuery.ResolveAsync(dbContext, cancellationToken);

        if (current is null)
        {
            return;
        }

        var season = await dbContext.Seasons.SingleOrDefaultAsync(
            candidate => candidate.Id == current.SeasonId,
            cancellationToken);

        if (season is null || season.Status != SeasonStatus.Active || now < season.EndsAt)
        {
            return;
        }

        // A league that was activated after the last matchday is owed its play-off while the semi-final can still be scheduled. The pass
        // is idempotent and cheap once nothing is owed, and a failure to draw must not stop the rollover from being queued (PR-4).
        try
        {
            await scope.ServiceProvider.GetRequiredService<PlayoffDraw>().CatchUpAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDrawFailed(exception);
        }

        // The rollover follows the last play-off: it is not queued while a round is unpublished or a tie has no winner, so the job never
        // starts on a season that is still being played (PR-4). A later pass queues it once the season is settled.
        var settled = await scope.ServiceProvider.GetRequiredService<IPlayoffRepository>()
            .IsSeasonSettledAsync(season.Id, cancellationToken);

        if (!settled)
        {
            return;
        }

        var inserted = await queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = SeasonRolloverJobTypes.Rollover,
                BusinessKey = SeasonRolloverJobTypes.RolloverKey(season.Id),
                PayloadJson = SeasonRolloverJobPayload.For(season.Id),
                DueAt = now,
            },
            cancellationToken);

        if (inserted)
        {
            LogScheduled(season.Id, season.DisplayLabel);
        }
    }

    [LoggerMessage(
        EventId = 3400,
        Level = LogLevel.Information,
        Message = "Season rollover is disabled; no rollover job will be materialised (PR-4).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3401,
        Level = LogLevel.Information,
        Message = "The season-rollover scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3402,
        Level = LogLevel.Information,
        Message = "Materialised the rollover of season {SeasonId} ({Label}) (PR-4).")]
    private partial void LogScheduled(Guid seasonId, string label);

    [LoggerMessage(
        EventId = 3404,
        Level = LogLevel.Error,
        Message = "The season-rollover scheduler failed to catch up the promotion play-off draw (PR-4).")]
    private partial void LogDrawFailed(Exception exception);

    [LoggerMessage(
        EventId = 3403,
        Level = LogLevel.Error,
        Message = "The season-rollover scheduler failed to materialise the due season.")]
    private partial void LogFailed(Exception exception);
}
