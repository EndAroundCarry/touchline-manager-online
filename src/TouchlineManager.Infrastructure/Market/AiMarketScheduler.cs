using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Infrastructure.Market;

/// <summary>
/// Materialises the daily AI transfer-market evaluation job (`TRF-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// One row per UTC day, keyed on the day, so the row is the deadline and a worker that was down when the day
/// opened runs the evaluation late rather than skipping it (ADR-0003). The work itself is idempotent once a
/// club's decisions are written, so the daily cadence is what keeps the AI market reacting to a world that
/// changes — a listing that appeared, a squad that thinned — without the evaluation having to be told.
/// </para>
/// <para>
/// Enqueue is idempotent on the business key, so running this every few minutes inserts nothing after the
/// day's first pass. Nothing here is reachable from a command: the AI's listings and bids are written by the
/// worker alone (`MAT-2`'s principle, applied to the market), and the scheduler exists only to place the row.
/// </para>
/// </remarks>
internal sealed partial class AiMarketScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly AiMarketOptions _options;
    private readonly ILogger<AiMarketScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public AiMarketScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<AiMarketOptions> options,
        ILogger<AiMarketScheduler> logger)
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
        if (!_options.EnableEvaluation)
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
                // A failed materialisation must not kill the loop: the next pass re-derives the same day.
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
        var day = DateOnly.FromDateTime(now.UtcDateTime);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var inserted = await queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = EvaluateAiMarketJobHandler.TypeName,
                BusinessKey = AiMarketJobTypes.DayKey(day),
                DueAt = now,
            },
            cancellationToken);

        if (inserted)
        {
            LogScheduled(day);
        }
    }

    [LoggerMessage(
        EventId = 3710,
        Level = LogLevel.Information,
        Message = "The AI market evaluation is disabled; no evaluation job will be materialised (TRF-12).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3711,
        Level = LogLevel.Information,
        Message = "The AI market scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3712,
        Level = LogLevel.Information,
        Message = "Materialised the AI market evaluation for {Day} (TRF-12).")]
    private partial void LogScheduled(DateOnly day);

    [LoggerMessage(
        EventId = 3713,
        Level = LogLevel.Error,
        Message = "The AI market scheduler failed to materialise the day's job.")]
    private partial void LogFailed(Exception exception);
}
