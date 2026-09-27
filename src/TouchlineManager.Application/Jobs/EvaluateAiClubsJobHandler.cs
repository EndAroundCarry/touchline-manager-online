using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Squad;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Gives every AI-controlled club the side, tactics, and training a manager would have set
/// (`INS-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="EvaluateAiClubs"/>. The job is world-scoped and carries no payload: it
/// considers every club nobody holds, and the use case fills only what is missing, so the queue's
/// at-least-once delivery cannot overwrite a decision already made.
/// </remarks>
public sealed partial class EvaluateAiClubsJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = "world.evaluate-ai-clubs";

    private readonly EvaluateAiClubs _evaluate;
    private readonly ILogger<EvaluateAiClubsJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public EvaluateAiClubsJobHandler(EvaluateAiClubs evaluate, ILogger<EvaluateAiClubsJobHandler> logger)
    {
        _evaluate = evaluate;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var result = await _evaluate.ExecuteAsync(cancellationToken);

        LogEvaluated(result.Clubs, result.TacticalPlansCreated, result.TrainingPlansCreated);

        if (result.Skipped > 0)
        {
            // The policy cannot produce an invalid plan by construction, so a skip is a defect worth
            // seeing rather than routine (INS-12).
            LogSkipped(result.Skipped);
        }
    }

    [LoggerMessage(
        EventId = 3400,
        Level = LogLevel.Information,
        Message = "Evaluated {ClubCount} AI club(s): {TacticalPlans} tactical plan(s) and "
            + "{TrainingPlans} training plan(s) created (INS-12).")]
    private partial void LogEvaluated(int clubCount, int tacticalPlans, int trainingPlans);

    [LoggerMessage(
        EventId = 3401,
        Level = LogLevel.Warning,
        Message = "{Skipped} AI club(s) produced a plan the validator refused; their side is left to the "
            + "snapshot builder (INS-12).")]
    private partial void LogSkipped(int skipped);
}
