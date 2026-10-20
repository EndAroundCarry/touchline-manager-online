using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Jobs;

namespace Tactical11Manager.Application.Fans;

/// <summary>The result of a diagnostics trigger for the weekly fan review (`FAN-8`).</summary>
/// <param name="WeekOf">The Sunday the triggered review is for.</param>
/// <param name="ReviewKey">The business key of the review job.</param>
/// <param name="Enqueued">Whether the job row was newly inserted.</param>
public sealed record TriggerFanReviewResult(DateOnly WeekOf, string ReviewKey, bool Enqueued);

/// <summary>
/// Enqueues the coming Sunday's fan review due now, so a non-production journey can watch the fans react without waiting for the weekend
/// (ADR-0016's pattern, applied to the fans).
/// </summary>
/// <remarks>
/// It does what the worker-only scheduler normally does and nothing more: the worker still reviews exactly as it would on the calendar. It
/// has no production surface, because the endpoint that reaches it is mapped only when the diagnostics flag is on. A week that is already
/// queued is left alone, and a club already reviewed for the week is skipped by the review itself, so a repeated call is a no-op.
/// </remarks>
public sealed class TriggerFanReview
{
    private readonly IJobQueue _jobs;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public TriggerFanReview(IJobQueue jobs, IClock clock)
    {
        _jobs = jobs;
        _clock = clock;
    }

    /// <summary>Enqueues the review of the week in progress.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TriggerFanReviewResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var weekOf = DateOnly.FromDateTime(FanReviewWeeks.BoundaryAtOrAfter(now).UtcDateTime);
        var key = FanReviewWeeks.WeekKey(weekOf);

        var enqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = FanReviewWeeks.JobType,
                BusinessKey = key,
                DueAt = now,
                PayloadJson = FanReviewWeeks.PayloadFor(weekOf),
            },
            cancellationToken);

        return new TriggerFanReviewResult(weekOf, key, enqueued);
    }
}
