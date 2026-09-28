using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Application.World;

/// <summary>The result of a diagnostics inactivity trigger (`OCC-1`–`OCC-3`).</summary>
/// <param name="BusinessKey">The business key of the enqueued job.</param>
/// <param name="Enqueued">Whether the job row was newly inserted.</param>
public sealed record TriggerInactivityResult(string BusinessKey, bool Enqueued);

/// <summary>
/// Enqueues today's inactivity-ladder job, due now, so a non-production environment can run the ladder without
/// waiting for the day's schedule (`OCC-1`–`OCC-3`).
/// </summary>
/// <remarks>
/// It does what the worker-only scheduler does and nothing more: the worker still runs the ladder exactly as it
/// would on its daily tick. The business key is the UTC day, so a repeated trigger on the same day is a no-op
/// once the row exists. It has no production surface.
/// </remarks>
public sealed class TriggerInactivity
{
    private readonly IJobQueue _jobs;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public TriggerInactivity(IJobQueue jobs, IClock clock)
    {
        _jobs = jobs;
        _clock = clock;
    }

    /// <summary>Enqueues today's ladder job, due now.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TriggerInactivityResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var key = InactivityJobTypes.DayKey(DateOnly.FromDateTime(now.UtcDateTime));

        var enqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = EvaluateInactivityJobHandler.TypeName,
                BusinessKey = key,
                DueAt = now,
            },
            cancellationToken);

        return new TriggerInactivityResult(key, enqueued);
    }
}
