using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Competition;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Rebuilds a division's table and season statistics from its published results (`TBL-13`, §7.2).
/// </summary>
/// <remarks>
/// A repair job, and a thin shell over the use case. A payload with no division-season, or one that names a
/// division-season the world does not have, is a permanent failure: retrying cannot produce the missing row.
/// The rebuild itself is idempotent, so an at-least-once delivery that runs it twice writes nothing the
/// second time.
/// </remarks>
public sealed partial class RebuildDivisionProjectionsJobHandler : IJobHandler
{
    private readonly RebuildDivisionProjections _rebuild;
    private readonly ILogger<RebuildDivisionProjectionsJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public RebuildDivisionProjectionsJobHandler(
        RebuildDivisionProjections rebuild,
        ILogger<RebuildDivisionProjectionsJobHandler> logger)
    {
        _rebuild = rebuild;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => ProjectionJobTypes.RebuildDivisionProjections;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!ProjectionJobPayload.TryRead(job.PayloadJson, out var divisionSeasonId))
        {
            throw new PermanentJobFailureException(
                $"The projection rebuild job carried no division-season (payload: {job.PayloadJson}).");
        }

        var result = await _rebuild.ExecuteAsync(divisionSeasonId, apply: true, cancellationToken);

        if (result.Outcome == ProjectionRebuildOutcome.DivisionSeasonNotFound)
        {
            throw new PermanentJobFailureException(
                $"Division-season {divisionSeasonId:D} does not exist, so its projections cannot be rebuilt.");
        }

        LogRebuilt(divisionSeasonId, result.Outcome, result.StandingsDrifted, result.PlayerStatsDrifted);
    }

    [LoggerMessage(
        EventId = 3500,
        Level = LogLevel.Information,
        Message = "Rebuilt projections for division-season {DivisionSeasonId}: {Outcome}, "
        + "{StandingsDrifted} table row(s) and {PlayerStatsDrifted} player line(s) corrected.")]
    private partial void LogRebuilt(
        Guid divisionSeasonId,
        ProjectionRebuildOutcome outcome,
        int standingsDrifted,
        int playerStatsDrifted);
}
