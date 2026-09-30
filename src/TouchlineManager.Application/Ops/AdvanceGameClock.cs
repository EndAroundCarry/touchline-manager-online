using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Application.Ops;

/// <summary>The result of asking to advance the stepped game clock.</summary>
/// <param name="Target">The step kind, <c>day</c> or <c>matchday</c>.</param>
/// <param name="TargetInstantUtc">The instant the step will land on.</param>
/// <param name="BusinessKey">The business key of the enqueued job.</param>
/// <param name="Enqueued"><see langword="true"/> when a new job row was inserted.</param>
public sealed record AdvanceGameClockResult(
    string Target,
    DateTimeOffset TargetInstantUtc,
    string BusinessKey,
    bool Enqueued);

/// <summary>
/// Enqueues one step of the stepped game clock, due now, so a non-production environment can play a season a
/// press at a time (ADR-0049).
/// </summary>
/// <remarks>
/// <para>
/// This is the operator's counterpart of a scheduler's materialisation: it resolves which instant to land on
/// — the start of the next game day, or the next round's kickoff — and enqueues the worker's real advance job.
/// The worker still sets the clock and materialises the day; the endpoint only does what an operator decision
/// does (ADR-0016's pattern, applied to time).
/// </para>
/// <para>
/// It reads the stored instant through <see cref="IGameClockStore"/> rather than <c>IClock</c>, so the target
/// is resolved against the same frozen "now" every host sees, not against a poller's copy.
/// </para>
/// <para>
/// It is reachable only from a development-flagged endpoint and is never mapped in production (§17.12).
/// </para>
/// </remarks>
public sealed class AdvanceGameClock
{
    /// <summary>How far ahead a round is looked for when stepping to the next matchday.</summary>
    private static readonly TimeSpan MatchdayHorizon = TimeSpan.FromDays(400);

    private readonly IGameClockStore _clockStore;
    private readonly IMatchdayRepository _matchdays;
    private readonly IJobQueue _jobs;

    /// <summary>Initializes the use case.</summary>
    public AdvanceGameClock(IGameClockStore clockStore, IMatchdayRepository matchdays, IJobQueue jobs)
    {
        _clockStore = clockStore;
        _matchdays = matchdays;
        _jobs = jobs;
    }

    /// <summary>Resolves the target instant and enqueues the advance job, due now.</summary>
    /// <param name="target">The requested kind, <c>day</c> or <c>matchday</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AdvanceGameClockResult> ExecuteAsync(string? target, CancellationToken cancellationToken)
    {
        var kind = string.Equals(target, ClockJobPayload.Matchday, StringComparison.OrdinalIgnoreCase)
            ? ClockJobPayload.Matchday
            : ClockJobPayload.Day;

        var now = await _clockStore.ReadAsync(cancellationToken);
        var next = await ResolveTargetAsync(kind, now, cancellationToken);

        if (next < now)
        {
            next = now;
        }

        var businessKey = ClockJobTypes.AdvanceKey(kind, next);

        var enqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = ClockJobTypes.Advance,
                BusinessKey = businessKey,
                DueAt = now,
                PayloadJson = ClockJobPayload.For(next),
            },
            cancellationToken);

        return new AdvanceGameClockResult(kind, next, businessKey, enqueued);
    }

    private async Task<DateTimeOffset> ResolveTargetAsync(
        string kind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (kind == ClockJobPayload.Matchday)
        {
            var upcoming = await _matchdays.ListPendingLockingBetweenAsync(
                now,
                now.Add(MatchdayHorizon),
                cancellationToken);

            if (upcoming.Count > 0)
            {
                return upcoming[0].KickoffAt;
            }
        }

        // The start of the next game day: the step that always makes progress when no round is upcoming.
        return new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
    }
}
