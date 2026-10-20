using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Fans;

namespace Tactical11Manager.Infrastructure.Fans;

/// <summary>
/// Materialises the weekly fan review (`FAN-8`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The materialiser's job is to make the week's row exist, so the row's <c>due_at</c> is the deadline and the worker holds no deadline
/// authority itself (ADR-0003). If the worker is down at the Sunday boundary the job waits and runs late rather than being skipped, and a
/// restart re-derives the same week from the clock instead of remembering where it was.
/// </para>
/// <para>
/// Enqueue is idempotent on the business key, so running this every few minutes inserts nothing after the first pass. It ensures the most
/// recent boundary's row and the next one, the pattern the weekly finance run uses, and the boundary is half an hour after the finance run
/// so both follow the Sunday round.
/// </para>
/// </remarks>
internal sealed partial class WeeklyFanReviewScheduler : BackgroundService, IJobMaterializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly FanOptions _options;
    private readonly ILogger<WeeklyFanReviewScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public WeeklyFanReviewScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<FanOptions> options,
        ILogger<WeeklyFanReviewScheduler> logger)
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
        if (!_options.EnableWeeklyReview)
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
                // A failed materialisation must not kill the loop: the next pass re-derives the same week.
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
        if (!_options.EnableWeeklyReview)
        {
            return;
        }

        var due = FanReviewWeeks.BoundaryAtOrBefore(now);
        var next = due.AddDays(7);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var inserted = await EnsureAsync(queue, due, cancellationToken);
        inserted |= await EnsureAsync(queue, next, cancellationToken);

        if (inserted)
        {
            LogScheduled(due, next);
        }
    }

    private static async Task<bool> EnsureAsync(
        IJobQueue queue,
        DateTimeOffset boundary,
        CancellationToken cancellationToken)
    {
        var week = DateOnly.FromDateTime(boundary.UtcDateTime);

        return await queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = FanReviewWeeks.JobType,
                BusinessKey = FanReviewWeeks.WeekKey(week),
                DueAt = boundary,
                PayloadJson = FanReviewWeeks.PayloadFor(week),
            },
            cancellationToken);
    }

    [LoggerMessage(
        EventId = 3820,
        Level = LogLevel.Information,
        Message = "The weekly fan review is disabled; the scheduler is idle (FAN-8).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3821,
        Level = LogLevel.Information,
        Message = "The weekly fan review scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3822,
        Level = LogLevel.Information,
        Message = "Materialised the weekly fan review; the most recent boundary was {Due} and the next is {Next}.")]
    private partial void LogScheduled(DateTimeOffset due, DateTimeOffset next);

    [LoggerMessage(
        EventId = 3823,
        Level = LogLevel.Error,
        Message = "The weekly fan review scheduler failed to materialise the week's job.")]
    private partial void LogFailed(Exception exception);
}
