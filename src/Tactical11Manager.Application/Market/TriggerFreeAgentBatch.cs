using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Domain.Market;

namespace Tactical11Manager.Application.Market;

/// <summary>The result of a diagnostics trigger for the current week's free-agent batch (`FA-5`).</summary>
/// <param name="WeekStart">The Monday the triggered batch is for.</param>
/// <param name="BatchKey">The business key of the batch job.</param>
/// <param name="Enqueued">Whether the job row was newly inserted.</param>
public sealed record TriggerFreeAgentBatchResult(DateOnly WeekStart, string BatchKey, bool Enqueued);

/// <summary>
/// Enqueues the current week's free-agent batch job due now, so a non-production journey can watch the pool
/// open without waiting for Monday (ADR-0016's pattern, applied to the market).
/// </summary>
/// <remarks>
/// It does what the worker-only scheduler normally does and nothing more: the worker still generates and lists
/// exactly as it would on the calendar. It has no production surface — the endpoint that reaches it is mapped
/// only when the diagnostics flag is on. A week that is already queued or open is left alone, so a repeated call
/// is a no-op.
/// </remarks>
public sealed class TriggerFreeAgentBatch
{
    private readonly IJobQueue _jobs;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public TriggerFreeAgentBatch(IJobQueue jobs, IClock clock)
    {
        _jobs = jobs;
        _clock = clock;
    }

    /// <summary>Enqueues the current week's batch job.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TriggerFreeAgentBatchResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var weekStart = FreeAgentWeeks.WeekStartAtOrBefore(now);
        var key = FreeAgentJobTypes.WeekKey(weekStart);

        var enqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = FreeAgentJobTypes.Batch,
                BusinessKey = key,
                DueAt = now,
                PayloadJson = FreeAgentJobTypes.PayloadFor(weekStart),
            },
            cancellationToken);

        return new TriggerFreeAgentBatchResult(weekStart, key, enqueued);
    }
}
