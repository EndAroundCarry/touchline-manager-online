using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Comms;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Writes one round's deadline reminder (`COM-3`).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="SendDeadlineReminders"/>. A payload it cannot read is refused permanently,
/// because a reminder job with no round is a defect rather than a transient fault.
/// </remarks>
public sealed partial class SendDeadlineRemindersJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = ReminderJobTypes.Deadline;

    private readonly SendDeadlineReminders _reminders;
    private readonly ILogger<SendDeadlineRemindersJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public SendDeadlineRemindersJobHandler(
        SendDeadlineReminders reminders,
        ILogger<SendDeadlineRemindersJobHandler> logger)
    {
        _reminders = reminders;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!ReminderJobPayload.TryRead(job.PayloadJson, out var matchdayId))
        {
            throw new PermanentJobFailureException(
                $"The deadline-reminder job '{job.Id}' carried no readable matchday (COM-3).");
        }

        var result = await _reminders.ExecuteAsync(matchdayId, cancellationToken);

        LogReminded(matchdayId, result.Reminded, result.Skipped);
    }

    [LoggerMessage(
        EventId = 5330,
        Level = LogLevel.Information,
        Message = "Deadline reminder for matchday {MatchdayId} reminded {Reminded} managers (skipped: {Skipped}).")]
    private partial void LogReminded(Guid matchdayId, int reminded, bool skipped);
}
