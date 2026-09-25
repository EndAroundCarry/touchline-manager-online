using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Simulates a division's round from its frozen snapshots and stages the results (`MAT-7`, §7.2, §7.4).
/// </summary>
/// <remarks>
/// A malformed snapshot is dead-lettered: it means the frozen input no longer reproduces the hash it was
/// stored with, or was frozen against rules this build does not implement, and neither is something a retry
/// improves. The attempt row it leaves behind names the fixture, the engine version, and the input hash, so
/// the repair starts from evidence rather than from a search (`MAT-9`, `MAT-10`).
/// </remarks>
public sealed partial class ResolveMatchdayJobHandler : IJobHandler
{
    private readonly ResolveMatchday _resolve;
    private readonly ILogger<ResolveMatchdayJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public ResolveMatchdayJobHandler(ResolveMatchday resolve, ILogger<ResolveMatchdayJobHandler> logger)
    {
        _resolve = resolve;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => MatchdayJobTypes.Resolve;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!MatchdayJobPayload.TryRead(job.PayloadJson, out var matchdayId))
        {
            throw new PermanentJobFailureException(
                $"The resolution job carried no matchday to resolve (payload: {job.PayloadJson}).");
        }

        ResolveMatchdayResult result;

        try
        {
            result = await _resolve.ExecuteAsync(matchdayId, job.Id, cancellationToken);
        }
        catch (UnplayableSquadException exception)
        {
            // A lock that never ran leaves the resolver to take the snapshot itself, so this is where an
            // unplayable club surfaces when the lock job was missed entirely.
            LogUnplayable(matchdayId, exception.Message);

            throw new PermanentJobFailureException(exception.Message, exception);
        }

        LogResolved(matchdayId, result.Outcome, result.Simulated, result.Staged);
    }

    [LoggerMessage(
        EventId = 3210,
        Level = LogLevel.Information,
        Message = "Resolved matchday {MatchdayId}: {Outcome}, {Simulated} fixture(s) simulated, {Staged} staged.")]
    private partial void LogResolved(
        Guid matchdayId,
        ResolveMatchdayOutcome outcome,
        int simulated,
        int staged);

    [LoggerMessage(
        EventId = 3211,
        Level = LogLevel.Error,
        Message = "Matchday {MatchdayId} cannot be resolved: {Reason}")]
    private partial void LogUnplayable(Guid matchdayId, string reason);
}
