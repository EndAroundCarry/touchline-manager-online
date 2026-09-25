using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Locks a division's round at its deadline (`CAL-3`, §7.2, §7.3).
/// </summary>
/// <remarks>
/// <para>
/// A thin shell over <see cref="LockMatchday"/>, like every handler here: the queue owns when, the use case
/// owns what, and the handler's only decisions are which failures are permanent and what to log.
/// </para>
/// <para>
/// A club that cannot field a legal side dead-letters the job rather than retrying. It is not a transient
/// condition — nothing about it changes by waiting, and the daily progression that might have returned an
/// injured player only runs once the round has been played — so an operator has to see it, and the round
/// stays visibly locked-out instead of pretending to be playable (§7.4.9).
/// </para>
/// </remarks>
public sealed partial class LockMatchdayJobHandler : IJobHandler
{
    private readonly LockMatchday _lock;
    private readonly ILogger<LockMatchdayJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public LockMatchdayJobHandler(LockMatchday lockMatchday, ILogger<LockMatchdayJobHandler> logger)
    {
        _lock = lockMatchday;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => MatchdayJobTypes.Lock;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!MatchdayJobPayload.TryRead(job.PayloadJson, out var matchdayId))
        {
            throw new PermanentJobFailureException(
                $"The lock job carried no matchday to lock (payload: {job.PayloadJson}).");
        }

        LockMatchdayResult result;

        try
        {
            result = await _lock.ExecuteAsync(matchdayId, cancellationToken);
        }
        catch (UnplayableSquadException exception)
        {
            LogUnplayable(matchdayId, exception.Message);

            throw new PermanentJobFailureException(exception.Message, exception);
        }

        LogLocked(matchdayId, result.Outcome, result.FrozenFixtures, result.Repairs);
    }

    [LoggerMessage(
        EventId = 3200,
        Level = LogLevel.Information,
        Message = "Locked matchday {MatchdayId}: {Outcome}, {FrozenFixtures} fixture(s) frozen, {Repairs} repair(s) recorded.")]
    private partial void LogLocked(
        Guid matchdayId,
        LockMatchdayOutcome outcome,
        int frozenFixtures,
        int repairs);

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Error,
        Message = "Matchday {MatchdayId} cannot be locked: {Reason}")]
    private partial void LogUnplayable(Guid matchdayId, string reason);
}
