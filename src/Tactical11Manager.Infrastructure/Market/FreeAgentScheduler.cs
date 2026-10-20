using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Domain.Market;

namespace Tactical11Manager.Infrastructure.Market;

/// <summary>
/// Materialises the weekly free-agent batch (`FA-5`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The materialiser makes the week's row exist, so the row's <c>due_at</c> (Monday at the daily auction window)
/// is the deadline and the worker holds no deadline authority itself (ADR-0003). If the worker is down at the
/// boundary the job waits and runs late rather than being skipped, and a restart re-derives the same week from
/// the clock instead of remembering where it was.
/// </para>
/// <para>
/// Enqueue is idempotent on the business key, so running this every few minutes inserts nothing after the
/// first pass. It ensures this week's row and next week's, the pattern the weekly finance run uses.
/// </para>
/// </remarks>
internal sealed partial class FreeAgentScheduler : BackgroundService, IJobMaterializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly FreeAgentOptions _options;
    private readonly ILogger<FreeAgentScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public FreeAgentScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<FreeAgentOptions> options,
        ILogger<FreeAgentScheduler> logger)
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
        if (!_options.EnableBatches)
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
        if (!_options.EnableBatches)
        {
            return;
        }

        var current = FreeAgentWeeks.WeekStartAtOrBefore(now);
        var next = current.AddDays(7);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var inserted = await EnsureAsync(queue, current, cancellationToken);
        inserted |= await EnsureAsync(queue, next, cancellationToken);

        if (inserted)
        {
            LogScheduled(current, next);
        }
    }

    private static async Task<bool> EnsureAsync(
        IJobQueue queue,
        DateOnly weekStart,
        CancellationToken cancellationToken) =>
        await queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = FreeAgentJobTypes.Batch,
                BusinessKey = FreeAgentJobTypes.WeekKey(weekStart),
                DueAt = FreeAgentWeeks.OpensAt(weekStart),
                PayloadJson = FreeAgentJobTypes.PayloadFor(weekStart),
            },
            cancellationToken);

    [LoggerMessage(
        EventId = 3610,
        Level = LogLevel.Information,
        Message = "The weekly free-agent pool is disabled; the scheduler is idle (FA-5).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3611,
        Level = LogLevel.Information,
        Message = "The free-agent scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3612,
        Level = LogLevel.Information,
        Message = "Materialised the free-agent batch jobs; the current week is {Current} and the next is {Next}.")]
    private partial void LogScheduled(DateOnly current, DateOnly next);

    [LoggerMessage(
        EventId = 3613,
        Level = LogLevel.Error,
        Message = "The free-agent scheduler failed to materialise the week's job.")]
    private partial void LogFailed(Exception exception);
}
