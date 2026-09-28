using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Comms;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Infrastructure.Comms;

/// <summary>
/// Materialises one outbox dispatch job per minute (`MOD-4`, ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// The outbox row is written in a domain transaction; this materialiser turns "there is something to send"
/// into work. The business key is the minute, because a completed dispatch job is terminal and the next pass
/// must be a new row rather than a resurrection of the last one — which is the one place an outbox business
/// key cannot be derived from an aggregate.
/// </para>
/// <para>
/// Worker-only, like the other schedulers: mail leaves the game as a consequence of play, never of a client
/// command, and only the worker may dispatch it (ADR-0001, ADR-0008).
/// </para>
/// </remarks>
internal sealed partial class OutboxScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public OutboxScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<OutboxOptions> options,
        ILogger<OutboxScheduler> logger)
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
        if (!_options.EnableDispatch)
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
        var businessKey = OutboxJobTypes.MinuteKey(now);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var inserted = await queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = DispatchOutboxJobHandler.TypeName,
                BusinessKey = businessKey,
                DueAt = now,
            },
            cancellationToken);

        if (inserted)
        {
            LogScheduled(businessKey);
        }
    }

    [LoggerMessage(
        EventId = 5310,
        Level = LogLevel.Information,
        Message = "Outbox dispatch is disabled; no dispatch job will be materialised (MOD-4).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 5311,
        Level = LogLevel.Information,
        Message = "The outbox scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 5312,
        Level = LogLevel.Information,
        Message = "Materialised outbox dispatch {BusinessKey}.")]
    private partial void LogScheduled(string businessKey);

    [LoggerMessage(
        EventId = 5313,
        Level = LogLevel.Error,
        Message = "The outbox scheduler failed to materialise a dispatch job.")]
    private partial void LogFailed(Exception exception);
}
