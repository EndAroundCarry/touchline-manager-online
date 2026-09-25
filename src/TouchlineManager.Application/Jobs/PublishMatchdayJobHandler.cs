using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Competition;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Publishes a division's round and applies its projections (`MAT-7`, `TBL-13`, §7.2, §7.4).
/// </summary>
/// <remarks>
/// A round that is not fully staged is a transient failure, not a permanent one: the resolver may still be
/// working through the last fixtures, or may have been interrupted by a deploy. Retrying with backoff
/// publishes the round as soon as it can be, and the use case refuses to publish a partial one in the
/// meantime, which is the whole of `MAT-7`.
/// </remarks>
public sealed partial class PublishMatchdayJobHandler : IJobHandler
{
    private readonly PublishMatchday _publish;
    private readonly ILogger<PublishMatchdayJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public PublishMatchdayJobHandler(PublishMatchday publish, ILogger<PublishMatchdayJobHandler> logger)
    {
        _publish = publish;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => MatchdayJobTypes.Publish;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!MatchdayJobPayload.TryRead(job.PayloadJson, out var matchdayId))
        {
            throw new PermanentJobFailureException(
                $"The publication job carried no matchday to publish (payload: {job.PayloadJson}).");
        }

        var result = await _publish.ExecuteAsync(matchdayId, cancellationToken);

        if (result.Outcome == PublishMatchdayOutcome.NotFullyStaged)
        {
            LogNotFullyStaged(matchdayId);

            throw new InvalidOperationException(
                $"Matchday {matchdayId:D} is not fully staged, so it cannot be published yet (MAT-7).");
        }

        LogPublished(matchdayId, result.Outcome, result.Published, result.TableRows);
    }

    [LoggerMessage(
        EventId = 3220,
        Level = LogLevel.Information,
        Message = "Published matchday {MatchdayId}: {Outcome}, {Published} fixture(s), {TableRows} table row(s).")]
    private partial void LogPublished(
        Guid matchdayId,
        PublishMatchdayOutcome outcome,
        int published,
        int tableRows);

    [LoggerMessage(
        EventId = 3221,
        Level = LogLevel.Warning,
        Message = "Matchday {MatchdayId} is not fully staged; publication will retry (MAT-7).")]
    private partial void LogNotFullyStaged(Guid matchdayId);
}
