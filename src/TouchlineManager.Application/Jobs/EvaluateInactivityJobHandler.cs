using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.World;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Runs the inactivity ladder (`OCC-1`–`OCC-3`).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="EvaluateInactivity"/>, like the AI evaluation's handler. It carries no payload:
/// the evaluation walks the whole membership, so its business key is the day rather than an aggregate
/// identity.
/// </remarks>
public sealed partial class EvaluateInactivityJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = InactivityJobTypes.Evaluate;

    private readonly EvaluateInactivity _evaluate;
    private readonly ILogger<EvaluateInactivityJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public EvaluateInactivityJobHandler(EvaluateInactivity evaluate, ILogger<EvaluateInactivityJobHandler> logger)
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

        LogEvaluated(result.Evaluated, result.Warned, result.MarkedInactive, result.Closed);
    }

    [LoggerMessage(
        EventId = 5121,
        Level = LogLevel.Information,
        Message = "Inactivity job evaluated {Evaluated} tenures: {Warned} warned, {MarkedInactive} inactive, "
            + "{Closed} closed.")]
    private partial void LogEvaluated(int evaluated, int warned, int markedInactive, int closed);
}
