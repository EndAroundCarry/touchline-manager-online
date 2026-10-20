using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Market;

namespace Tactical11Manager.Application.Jobs;

/// <summary>
/// Opens a week's free-agent pool (`FA-1`…`FA-6`, master plan §7.2).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="RunFreeAgentBatch"/>: it reads the week from the payload, falling back to the
/// current week for a job enqueued without one, and logs the counts. The use case is idempotent for a week, so
/// the queue's at-least-once delivery cannot open a week twice.
/// </remarks>
public sealed partial class RunFreeAgentBatchJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = FreeAgentJobTypes.Batch;

    private readonly RunFreeAgentBatch _run;
    private readonly IClock _clock;
    private readonly ILogger<RunFreeAgentBatchJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public RunFreeAgentBatchJobHandler(
        RunFreeAgentBatch run,
        IClock clock,
        ILogger<RunFreeAgentBatchJobHandler> logger)
    {
        _run = run;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var weekStart = FreeAgentJobTypes.TryReadWeek(job.PayloadJson, out var named)
            ? named
            : FreeAgentWeeks.WeekStartAtOrBefore(_clock.UtcNow);

        var result = await _run.ExecuteAsync(weekStart, cancellationToken);

        if (result.Created)
        {
            LogOpened(weekStart, result.Contracted, result.Generated, result.Released);
        }
        else
        {
            LogNothingToOpen(weekStart);
        }
    }

    [LoggerMessage(
        EventId = 3720,
        Level = LogLevel.Information,
        Message = "Opened the free-agent pool for the week of {WeekStart}: {Generated} generated for {Contracted} contracted players and {Released} released (FA-1).")]
    private partial void LogOpened(DateOnly weekStart, int contracted, int generated, int released);

    [LoggerMessage(
        EventId = 3721,
        Level = LogLevel.Information,
        Message = "The free-agent pool for the week of {WeekStart} was already open or is too late to open; nothing was added (FA-5).")]
    private partial void LogNothingToOpen(DateOnly weekStart);
}
