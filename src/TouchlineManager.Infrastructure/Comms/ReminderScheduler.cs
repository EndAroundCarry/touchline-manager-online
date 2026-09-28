using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Infrastructure.Comms;

/// <summary>
/// Materialises a reminder job for every round about to lock (`COM-3`, ADR-0029).
/// </summary>
/// <remarks>
/// <para>
/// The round's lock instant is the deadline, and <see cref="WorldRuleSet.DeadlineReminderLead"/> is how far
/// ahead the reminder is sent. The business key is the round, so a round is reminded once however many times
/// the materialiser ticks, and a completed row being terminal is exactly right here: the reminder is sent once,
/// not once per tick.
/// </para>
/// <para>
/// Worker-only, like the other schedulers: a reminder is a consequence of the calendar, never of a client
/// command, and only the worker may send one (ADR-0001, ADR-0008).
/// </para>
/// </remarks>
internal sealed partial class ReminderScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ReminderOptions _options;
    private readonly ILogger<ReminderScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public ReminderScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<ReminderOptions> options,
        ILogger<ReminderScheduler> logger)
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
        if (!_options.EnableReminders)
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
        var horizon = now.Add(WorldRuleSet.DeadlineReminderLead);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var matchdays = scope.ServiceProvider.GetRequiredService<IMatchdayRepository>();

        foreach (var matchday in await matchdays.ListPendingLockingBetweenAsync(now, horizon, cancellationToken))
        {
            var inserted = await queue.EnqueueAsync(
                new JobEnqueueRequest
                {
                    JobType = SendDeadlineRemindersJobHandler.TypeName,
                    BusinessKey = ReminderJobTypes.MatchdayKey(matchday.Id),
                    PayloadJson = ReminderJobPayload.For(matchday.Id),
                    DueAt = now,
                },
                cancellationToken);

            if (inserted)
            {
                LogScheduled(matchday.Id, matchday.RoundNumber);
            }
        }
    }

    [LoggerMessage(
        EventId = 5340,
        Level = LogLevel.Information,
        Message = "Deadline reminders are disabled; no reminder job will be materialised (COM-3).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 5341,
        Level = LogLevel.Information,
        Message = "The reminder scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 5342,
        Level = LogLevel.Information,
        Message = "Materialised the deadline reminder for round {RoundNumber} of matchday {MatchdayId}.")]
    private partial void LogScheduled(Guid matchdayId, int roundNumber);

    [LoggerMessage(
        EventId = 5343,
        Level = LogLevel.Error,
        Message = "The reminder scheduler failed to materialise upcoming reminders.")]
    private partial void LogFailed(Exception exception);
}
