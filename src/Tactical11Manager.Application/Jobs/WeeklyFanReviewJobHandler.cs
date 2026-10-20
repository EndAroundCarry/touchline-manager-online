using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Fans;

namespace Tactical11Manager.Application.Jobs;

/// <summary>
/// Runs the weekly fan review (`FAN-8`, master plan §7.2).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="RunWeeklyFanReview"/>: it reads the week from the payload, falling back to the boundary at or before the
/// current instant for a job enqueued without one. The use case reviews a club once a week, so the queue's at-least-once delivery cannot
/// judge a club twice. A materialiser enqueues the week's job; the handler never schedules its successor (ADR-0003).
/// </remarks>
public sealed class WeeklyFanReviewJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = FanReviewWeeks.JobType;

    private readonly RunWeeklyFanReview _run;
    private readonly IClock _clock;

    /// <summary>Initializes the handler.</summary>
    public WeeklyFanReviewJobHandler(RunWeeklyFanReview run, IClock clock)
    {
        _run = run;
        _clock = clock;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var weekOf = FanReviewWeeks.TryReadWeek(job.PayloadJson, out var named)
            ? named
            : DateOnly.FromDateTime(FanReviewWeeks.BoundaryAtOrBefore(_clock.UtcNow).UtcDateTime);

        await _run.ExecuteAsync(weekOf, cancellationToken);
    }
}
