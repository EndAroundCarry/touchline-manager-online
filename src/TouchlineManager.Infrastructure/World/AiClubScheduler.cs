using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Infrastructure.World;

/// <summary>
/// Materialises the daily AI club evaluation job (`INS-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// One row per UTC day, keyed on the day, so the row is the deadline and a worker that was down when the
/// day opened runs the evaluation late rather than skipping it (ADR-0003). The work itself is a no-op once
/// every club has its plans, so the daily cadence is what catches a club that has just come under AI
/// control — a takeover released, or a tier provisioned — without the evaluation having to be told.
/// </para>
/// <para>
/// Enqueue is idempotent on the business key, so running this every few minutes inserts nothing after the
/// day's first pass. Nothing here is reachable from a command: the AI's plans are written by the worker
/// alone (`MAT-2`'s principle, applied to the squad), and the scheduler exists only to place the row.
/// </para>
/// </remarks>
internal sealed partial class AiClubScheduler : BackgroundService, IJobMaterializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly AiClubOptions _options;
    private readonly ILogger<AiClubScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public AiClubScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<AiClubOptions> options,
        ILogger<AiClubScheduler> logger)
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
                await MaterializeAsync(_clock.UtcNow, stoppingToken);
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

    /// <inheritdoc />
    public async Task MaterializeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_options.EnableEvaluation)
        {
            return;
        }

        var day = DateOnly.FromDateTime(now.UtcDateTime);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var inserted = await queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = EvaluateAiClubsJobHandler.TypeName,
                BusinessKey = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{EvaluateAiClubsJobHandler.TypeName}:{day:yyyy-MM-dd}"),
                DueAt = now,
            },
            cancellationToken);

        if (inserted)
        {
            LogScheduled(day);
        }
    }

    [LoggerMessage(
        EventId = 3200,
        Level = LogLevel.Information,
        Message = "The AI club evaluation is disabled; no evaluation job will be materialised (INS-12).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Information,
        Message = "The AI club scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3202,
        Level = LogLevel.Information,
        Message = "Materialised the AI club evaluation for {Day}.")]
    private partial void LogScheduled(DateOnly day);

    [LoggerMessage(
        EventId = 3203,
        Level = LogLevel.Error,
        Message = "The AI club scheduler failed to materialise the day's job.")]
    private partial void LogFailed(Exception exception);
}
