using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Repositories;

namespace TouchlineManager.Infrastructure.Competition;

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
/// Whether the season was actually <em>finished</em> — every matchday published and every projection
/// reconciled — is preflight's business, in the worker, not this materialiser's: a rollover enqueued a few
/// seconds before the last publication completes is a transient failure the job retries, which is a cheaper
/// arrangement than a materialiser that polls a whole season's publication state every pass.
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

        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
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
        EventId = 3403,
        Level = LogLevel.Error,
        Message = "The season-rollover scheduler failed to materialise the due season.")]
    private partial void LogFailed(Exception exception);
}
