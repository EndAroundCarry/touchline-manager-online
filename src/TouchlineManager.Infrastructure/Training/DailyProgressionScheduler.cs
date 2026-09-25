using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Infrastructure.Training;

/// <summary>
/// Materialises the daily training progression job (`TRN-3`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The materializer's whole job is to make the two daily rows exist — the boundary that has just passed and
/// the next one. It holds no deadline authority itself (ADR-0003): the row's <c>due_at</c> is the deadline,
/// so if the worker is down at 02:00 the job waits and runs late rather than being skipped, and a restart
/// re-derives the same days from the clock instead of trying to remember where it was.
/// </para>
/// <para>
/// Enqueue is idempotent on the business key, so running this every few minutes inserts nothing after the
/// first pass. The rows are never deleted, so a day already progressed is simply not re-inserted, and the
/// handler's own per-player guard covers the rest.
/// </para>
/// </remarks>
internal sealed partial class DailyProgressionScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<DailyProgressionScheduler> _logger;
    private readonly TrainingOptions _options;

    /// <summary>Initializes the scheduler.</summary>
    public DailyProgressionScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<TrainingOptions> options,
        ILogger<DailyProgressionScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableDailyProgression)
        {
            LogDisabled();

            return;
        }

        LogStarted(_options.CheckIntervalSeconds);

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
                // A failed materialisation must not kill the loop: the next pass re-derives the same days.
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
        var due = MostRecentBoundary(_clock.UtcNow);
        var next = due.AddDays(1);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var inserted = await EnsureAsync(queue, due, cancellationToken);
        inserted |= await EnsureAsync(queue, next, cancellationToken);

        if (inserted)
        {
            LogScheduled(due, next);
        }
    }

    /// <summary>Gets the most recent progression boundary at or before now (`TRN-3`).</summary>
    private static DateTimeOffset MostRecentBoundary(DateTimeOffset now)
    {
        var boundary = new DateTimeOffset(
            now.UtcDateTime.Date + WorldRuleSet.DailyProgressionUtc.ToTimeSpan(),
            TimeSpan.Zero);

        return now >= boundary ? boundary : boundary.AddDays(-1);
    }

    private static async Task<bool> EnsureAsync(
        IJobQueue queue,
        DateTimeOffset boundary,
        CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(boundary.UtcDateTime);

        return await queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = DailyPlayerProgressionJobHandler.TypeName,
                BusinessKey = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DailyPlayerProgressionJobHandler.TypeName}:{day:yyyy-MM-dd}"),
                DueAt = boundary,
                PayloadJson = DailyPlayerProgressionJobHandler.PayloadFor(day),
            },
            cancellationToken);
    }

    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Information,
        Message = "Daily progression is disabled; the scheduler is idle (TRN-3).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Information,
        Message = "Daily progression scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3102,
        Level = LogLevel.Information,
        Message = "Materialised daily progression; the most recent boundary was {Due} and the next is {Next}.")]
    private partial void LogScheduled(DateTimeOffset due, DateTimeOffset next);

    [LoggerMessage(
        EventId = 3103,
        Level = LogLevel.Error,
        Message = "The daily progression scheduler failed to materialise the day's job.")]
    private partial void LogFailed(Exception exception);
}
