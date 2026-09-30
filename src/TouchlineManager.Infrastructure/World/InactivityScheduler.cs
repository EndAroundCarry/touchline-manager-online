using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Infrastructure.World;

/// <summary>
/// Materialises one inactivity-ladder job per UTC day (`OCC-1`–`OCC-3`, ADR-0027).
/// </summary>
/// <remarks>
/// <para>
/// The ladder advances tenure state as real time passes, so it runs on a clock rather than on an event. The
/// business key is the UTC day, so the pass happens at most once a day; a worker that was down at the day's
/// boundary still materialises the row on its next tick, because the row it derives is still that day's.
/// </para>
/// <para>
/// Worker-only, like the other schedulers: a tenure ages as a consequence of time, never of a client command,
/// and only the worker may end one (ADR-0001, ADR-0008).
/// </para>
/// </remarks>
internal sealed partial class InactivityScheduler : BackgroundService, IJobMaterializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly InactivityOptions _options;
    private readonly ILogger<InactivityScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public InactivityScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<InactivityOptions> options,
        ILogger<InactivityScheduler> logger)
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
                JobType = EvaluateInactivityJobHandler.TypeName,
                BusinessKey = InactivityJobTypes.DayKey(day),
                DueAt = now,
            },
            cancellationToken);

        if (inserted)
        {
            LogScheduled(day);
        }
    }

    [LoggerMessage(
        EventId = 5320,
        Level = LogLevel.Information,
        Message = "The inactivity ladder is disabled; no evaluation job will be materialised (OCC-1).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 5321,
        Level = LogLevel.Information,
        Message = "The inactivity scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 5322,
        Level = LogLevel.Information,
        Message = "Materialised the inactivity evaluation for {Day}.")]
    private partial void LogScheduled(DateOnly day);

    [LoggerMessage(
        EventId = 5323,
        Level = LogLevel.Error,
        Message = "The inactivity scheduler failed to materialise today's evaluation.")]
    private partial void LogFailed(Exception exception);
}
