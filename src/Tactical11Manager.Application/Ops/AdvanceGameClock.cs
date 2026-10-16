using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Application.Ops;

/// <summary>The result of asking to advance the stepped game clock.</summary>
/// <param name="Target">The step kind, <c>day</c>, <c>matchday</c> or <c>season-end</c>.</param>
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
/// A <c>matchday</c> step with no round left is the end of the season: it lands on the active season's end (never earlier than now)
/// and makes sure the season's rollover job is on the queue, reviving one that was dead-lettered, so the toolbar's last press closes the
/// season and opens the next. The worker still runs the rollover, preflight and all.
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
    private readonly IWorldRepository _world;

    /// <summary>Initializes the use case.</summary>
    public AdvanceGameClock(IGameClockStore clockStore, IMatchdayRepository matchdays, IJobQueue jobs, IWorldRepository world)
    {
        _clockStore = clockStore;
        _matchdays = matchdays;
        _jobs = jobs;
        _world = world;
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
        var (next, closing) = await ResolveTargetAsync(kind, now, cancellationToken);

        if (closing is not null)
        {
            kind = ClockJobPayload.SeasonEnd;
        }

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

        if (closing is not null)
        {
            await EnsureRolloverAsync(closing, next, cancellationToken);
        }

        return new AdvanceGameClockResult(kind, next, businessKey, enqueued);
    }

    private async Task<(DateTimeOffset Target, Season? Closing)> ResolveTargetAsync(
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
                return (upcoming[0].KickoffAt, null);
            }

            // No round left: the step that remains is the end of the season, which is what lets its rollover run.
            var season = await ClockSeason.FindActiveAsync(_world, cancellationToken);

            if (season is not null)
            {
                return (season.EndsAt > now ? season.EndsAt : now, season);
            }
        }

        // The start of the next game day: the step that always makes progress when no round is upcoming.
        return (new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1), null);
    }

    /// <summary>
    /// Puts the season's rollover job on the queue. A dead-lettered one (its retries ran out before the season's last round was
    /// published) goes back to the queue; otherwise one is enqueued, which the business key makes a no-op when it is already there.
    /// </summary>
    private async Task EnsureRolloverAsync(Season season, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var businessKey = SeasonRolloverJobTypes.RolloverKey(season.Id);

        if (await _jobs.RequeueAsync(SeasonRolloverJobTypes.Rollover, businessKey, cancellationToken))
        {
            return;
        }

        await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = SeasonRolloverJobTypes.Rollover,
                BusinessKey = businessKey,
                PayloadJson = SeasonRolloverJobPayload.For(season.Id),
                DueAt = dueAt,
            },
            cancellationToken);
    }
}
