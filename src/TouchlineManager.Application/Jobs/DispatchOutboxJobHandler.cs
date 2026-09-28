using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Comms;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Runs one outbox dispatch pass (`MOD-4`, `COM-4`).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="DispatchOutbox"/>, like the other job handlers: it carries no logic, so the
/// use case stays reachable from a test without a queue. It carries no payload — the pass drains whatever is
/// due — so its business key is a minute bucket rather than an aggregate identity.
/// </remarks>
public sealed partial class DispatchOutboxJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = OutboxJobTypes.Dispatch;

    private readonly DispatchOutbox _dispatch;
    private readonly ILogger<DispatchOutboxJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public DispatchOutboxJobHandler(DispatchOutbox dispatch, ILogger<DispatchOutboxJobHandler> logger)
    {
        _dispatch = dispatch;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var result = await _dispatch.ExecuteAsync(cancellationToken);

        LogDispatched(result.Drained, result.Published, result.Rescheduled, result.DeadLettered);
    }

    [LoggerMessage(
        EventId = 5302,
        Level = LogLevel.Information,
        Message = "Outbox dispatch drained {Drained} messages: {Published} published, {Rescheduled} rescheduled, "
            + "{DeadLettered} dead-lettered (MOD-4).")]
    private partial void LogDispatched(int drained, int published, int rescheduled, int deadLettered);
}
