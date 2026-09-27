using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Application.Competition;

/// <summary>What triggering a round did.</summary>
public enum TriggerMatchdayOutcome
{
    /// <summary>The round's lock and resolution jobs were enqueued, due now.</summary>
    Enqueued = 0,

    /// <summary>The matchday does not exist.</summary>
    MatchdayNotFound = 1,
}

/// <summary>The result of triggering a round.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="MatchdayId">The round that was triggered.</param>
/// <param name="LockKey">The business key of the enqueued lock job.</param>
/// <param name="ResolveKey">The business key of the enqueued resolution job.</param>
/// <param name="LockEnqueued"><see langword="true"/> when the lock row was newly inserted.</param>
/// <param name="ResolveEnqueued"><see langword="true"/> when the resolution row was newly inserted.</param>
public sealed record TriggerMatchdayResult(
    TriggerMatchdayOutcome Outcome,
    Guid MatchdayId,
    string LockKey,
    string ResolveKey,
    bool LockEnqueued,
    bool ResolveEnqueued);

/// <summary>
/// Enqueues a round's lock and resolution jobs, due immediately, so a non-production environment can
/// watch a round play without waiting for its calendar deadline.
/// </summary>
/// <remarks>
/// <para>
/// This is the scheduler's own materialisation (<c>EnsureScheduleJobs</c>, §7.2) exposed off the worker so
/// the end-to-end journey can drive a real matchday through the real queue, handlers, engine, and
/// publication. It deliberately reuses <see cref="MatchdayJobTypes"/>' business keys and
/// <see cref="MatchdayJobPayload"/> and the same due-time semantics, so a triggered round is
/// indistinguishable from one the calendar materialised: the worker does every part of the work and the
/// unique business key keeps a repeated trigger a no-op (ADR-0003).
/// </para>
/// <para>
/// It enqueues only the lock and the resolution. Publication is enqueued by the resolution itself, in the
/// transaction that marks the round staged, and enqueuing it here would let it run before there is
/// anything to publish.
/// </para>
/// <para>
/// It is reachable only from a development-flagged endpoint and is never mapped in production (§17.12).
/// The compressed clock (ADR-0015) remains the mechanism for watching a season; this exists because an
/// automated "prepare, then watch" ordering cannot tolerate a clock that races real-time boot delays.
/// </para>
/// </remarks>
public sealed class TriggerMatchday
{
    private readonly IMatchdayRepository _matchdays;
    private readonly IJobQueue _jobs;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public TriggerMatchday(IMatchdayRepository matchdays, IJobQueue jobs, IClock clock)
    {
        _matchdays = matchdays;
        _jobs = jobs;
        _clock = clock;
    }

    /// <summary>Enqueues the round's lock and resolution jobs, due now.</summary>
    /// <param name="matchdayId">The round to trigger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TriggerMatchdayResult> ExecuteAsync(Guid matchdayId, CancellationToken cancellationToken)
    {
        var workload = await _matchdays.LoadMatchdayAsync(matchdayId, cancellationToken);

        if (workload is null)
        {
            return new TriggerMatchdayResult(
                TriggerMatchdayOutcome.MatchdayNotFound,
                matchdayId,
                string.Empty,
                string.Empty,
                LockEnqueued: false,
                ResolveEnqueued: false);
        }

        var now = _clock.UtcNow;
        var lockKey = MatchdayJobTypes.LockKey(matchdayId);
        var resolveKey = MatchdayJobTypes.ResolveKey(matchdayId);
        var payload = MatchdayJobPayload.For(matchdayId);

        var lockEnqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = MatchdayJobTypes.Lock,
                BusinessKey = lockKey,
                DueAt = now,
                PayloadJson = payload,
            },
            cancellationToken);

        var resolveEnqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = MatchdayJobTypes.Resolve,
                BusinessKey = resolveKey,
                DueAt = now,
                PayloadJson = payload,
            },
            cancellationToken);

        return new TriggerMatchdayResult(
            TriggerMatchdayOutcome.Enqueued,
            matchdayId,
            lockKey,
            resolveKey,
            lockEnqueued,
            resolveEnqueued);
    }
}
