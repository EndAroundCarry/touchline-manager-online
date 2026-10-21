using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.World;

namespace Tactical11Manager.Application.Jobs;

/// <summary>
/// Finishes one building's upgrade when its build time is up (`STRC-3`).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="CompleteStructureUpgrade"/>: it reads the building and the level from the
/// job's payload and calls the use case, which is idempotent on the building's state, so the queue's
/// at-least-once delivery cannot raise a level twice. A malformed payload is a programming error in the
/// enqueuer rather than a transient fault, so it is dead-lettered rather than retried.
/// </remarks>
public sealed partial class CompleteStructureUpgradeJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = StructureJobTypes.CompleteUpgrade;

    private readonly CompleteStructureUpgrade _complete;
    private readonly ILogger<CompleteStructureUpgradeJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public CompleteStructureUpgradeJobHandler(
        CompleteStructureUpgrade complete,
        ILogger<CompleteStructureUpgradeJobHandler> logger)
    {
        _complete = complete;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!StructureJobPayload.TryRead(job.PayloadJson, out var structureId, out var targetLevel))
        {
            throw new PermanentJobFailureException(
                $"The structure upgrade job '{job.Id}' carried no readable building and level (STRC-3).");
        }

        var outcome = await _complete.ExecuteAsync(structureId, targetLevel, cancellationToken);

        LogCompleted(structureId, targetLevel, outcome);
    }

    [LoggerMessage(
        EventId = 3510,
        Level = LogLevel.Information,
        Message = "Structure {StructureId} upgrade to level {TargetLevel}: {Outcome} (STRC-3).")]
    private partial void LogCompleted(Guid structureId, int targetLevel, CompleteStructureUpgradeOutcome outcome);
}
