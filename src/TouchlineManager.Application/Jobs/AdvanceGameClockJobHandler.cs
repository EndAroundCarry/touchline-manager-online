using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Applies one step of the stepped game clock and materialises the day it lands on (ADR-0049).
/// </summary>
/// <remarks>
/// <para>
/// The handler is the worker's half of a step: the endpoint resolved the target instant and enqueued this
/// job; here the stored instant is written and every registered materialiser is asked for the deadline rows
/// that instant makes due. The rows — a round's lock and resolution, the day's progression, a week's
/// settlement — are ordinary jobs the queue then runs, so a step is indistinguishable from the calendar
/// reaching the same moment (ADR-0003, ADR-0016).
/// </para>
/// <para>
/// It is idempotent: the target instant and the business key both come from the payload, so a redelivered
/// step re-writes the same instant and re-enqueues the same rows, which the unique business keys refuse.
/// </para>
/// </remarks>
public sealed partial class AdvanceGameClockJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = ClockJobTypes.Advance;

    private readonly IGameClockStore _clockStore;
    private readonly IEnumerable<IJobMaterializer> _materializers;
    private readonly ILogger<AdvanceGameClockJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public AdvanceGameClockJobHandler(
        IGameClockStore clockStore,
        IEnumerable<IJobMaterializer> materializers,
        ILogger<AdvanceGameClockJobHandler> logger)
    {
        _clockStore = clockStore;
        _materializers = materializers;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!ClockJobPayload.TryReadTargetInstant(job.PayloadJson, out var target))
        {
            throw new PermanentJobFailureException(
                "The advance-game-clock job carried no target instant (ADR-0049).");
        }

        var current = await _clockStore.ReadAsync(cancellationToken);

        // A step never moves time backwards: a target already passed (a missed round, say) is applied at the
        // current instant, so its jobs are materialised due now rather than the clock going back.
        if (target < current)
        {
            target = current;
        }

        await _clockStore.WriteAsync(target, cancellationToken);

        var materialized = 0;

        foreach (var materializer in _materializers)
        {
            await materializer.MaterializeAsync(target, cancellationToken);
            materialized++;
        }

        LogAdvanced(current, target, materialized);
    }

    [LoggerMessage(
        EventId = 3920,
        Level = LogLevel.Information,
        Message = "Advanced the stepped game clock from {From} to {To} and materialised {Materializers} domain materialiser(s) (ADR-0049).")]
    private partial void LogAdvanced(DateTimeOffset from, DateTimeOffset to, int materializers);
}
