using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Application.Competition;

/// <summary>What triggering a rollover did.</summary>
public enum TriggerSeasonRolloverOutcome
{
    /// <summary>The season's real rollover job was enqueued, due now.</summary>
    Enqueued = 0,

    /// <summary>The world or the named season does not exist.</summary>
    SeasonNotFound = 1,
}

/// <summary>The result of triggering a rollover.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="SeasonId">The season that would close.</param>
/// <param name="BusinessKey">The business key of the enqueued job.</param>
/// <param name="Enqueued"><see langword="true"/> when a new row was inserted.</param>
public sealed record TriggerSeasonRolloverResult(
    TriggerSeasonRolloverOutcome Outcome,
    Guid SeasonId,
    string BusinessKey,
    bool Enqueued);

/// <summary>
/// Enqueues a season's real rollover job, due immediately, so a non-production environment can close a
/// season on demand (`PR-4`, ADR-0034).
/// </summary>
/// <remarks>
/// <para>
/// This is the worker-only scheduler's own materialisation exposed off the worker, the way
/// <see cref="TriggerMatchday"/> exposes the matchday scheduler. It reuses
/// <see cref="SeasonRolloverJobTypes"/>' business key and <see cref="SeasonRolloverJobPayload"/>, so a
/// triggered rollover is indistinguishable from one the calendar materialised: the worker does all of the
/// work, the state machine still runs its preflight, and the unique business key keeps a repeated trigger a
/// no-op. No client command closes a season — it only enqueues the job the calendar would have
/// (ADR-0031 §7).
/// </para>
/// <para>
/// Reachable only from a development-flagged endpoint and never mapped in production (§17.12).
/// </para>
/// </remarks>
public sealed class TriggerSeasonRollover
{
    private readonly IWorldRepository _world;
    private readonly IJobQueue _jobs;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public TriggerSeasonRollover(IWorldRepository world, IJobQueue jobs, IClock clock)
    {
        _world = world;
        _jobs = jobs;
        _clock = clock;
    }

    /// <summary>Enqueues a season's rollover job, due now.</summary>
    /// <param name="seasonId">The season to close, or null for the world's current season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TriggerSeasonRolloverResult> ExecuteAsync(
        Guid? seasonId,
        CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return NotFound(seasonId);
        }

        var season = seasonId is { } named
            ? await _world.FindSeasonByIdAsync(named, cancellationToken)
            : await _world.FindSeasonAsync(world.Id, world.CurrentSeasonNumber, cancellationToken);

        if (season is null || season.WorldId != world.Id)
        {
            return NotFound(seasonId);
        }

        var businessKey = SeasonRolloverJobTypes.RolloverKey(season.Id);

        var enqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = SeasonRolloverJobTypes.Rollover,
                BusinessKey = businessKey,
                PayloadJson = SeasonRolloverJobPayload.For(season.Id),
                DueAt = _clock.UtcNow,
            },
            cancellationToken);

        return new TriggerSeasonRolloverResult(
            TriggerSeasonRolloverOutcome.Enqueued,
            season.Id,
            businessKey,
            enqueued);
    }

    private static TriggerSeasonRolloverResult NotFound(Guid? seasonId) =>
        new(TriggerSeasonRolloverOutcome.SeasonNotFound, seasonId ?? Guid.Empty, string.Empty, Enqueued: false);
}
