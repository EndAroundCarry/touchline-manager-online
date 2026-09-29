using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Competition;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Rolls one season over (`PR-4`, master plan §7.2).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="RunSeasonRollover"/>: it reads the season from the job's payload and calls
/// the use case, which is idempotent on the rollover's checkpoints, so the queue's at-least-once delivery
/// cannot move a club twice, advance the game year twice, or generate a second schedule. A malformed payload
/// is a programming error in the enqueuer rather than a transient fault, so it is dead-lettered rather than
/// retried.
/// </remarks>
public sealed partial class RunSeasonRolloverJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = SeasonRolloverJobTypes.Rollover;

    private readonly RunSeasonRollover _rollover;
    private readonly ILogger<RunSeasonRolloverJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public RunSeasonRolloverJobHandler(
        RunSeasonRollover rollover,
        ILogger<RunSeasonRolloverJobHandler> logger)
    {
        _rollover = rollover;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!SeasonRolloverJobPayload.TryRead(job.PayloadJson, out var seasonId))
        {
            throw new PermanentJobFailureException(
                $"The season-rollover job '{job.Id}' carried no readable season (PR-4).");
        }

        var result = await _rollover.ExecuteAsync(seasonId, cancellationToken);

        LogHandled(result.SeasonId, result.Outcome, result.NextSeasonId);
    }

    [LoggerMessage(
        EventId = 5210,
        Level = LogLevel.Information,
        Message = "Season rollover for {SeasonId} finished as {Outcome}; next season {NextSeasonId} (PR-4).")]
    private partial void LogHandled(Guid seasonId, SeasonRolloverOutcome outcome, Guid? nextSeasonId);
}
