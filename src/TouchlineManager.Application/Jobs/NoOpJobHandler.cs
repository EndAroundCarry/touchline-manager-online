using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// The Stage 1 walking-skeleton handler. It exists to prove that the API, the database, and the
/// worker compose into one durable pipeline; it has no business behaviour and is replaced by
/// real handlers from Stage 2 onward.
/// </summary>
public sealed partial class NoOpJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = "ops.noop";

    private readonly IClock _clock;
    private readonly ILogger<NoOpJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public NoOpJobHandler(IClock clock, ILogger<NoOpJobHandler> logger)
    {
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        LogHandled(job.Id, job.BusinessKey, job.AttemptCount, _clock.UtcNow);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Source-generated logging so arguments are not evaluated or formatted when the level is
    /// disabled.
    /// </summary>
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Handled no-op job {JobId} ({BusinessKey}) on attempt {AttemptCount} at {HandledAt}.")]
    private partial void LogHandled(Guid jobId, string businessKey, int attemptCount, DateTimeOffset handledAt);
}
