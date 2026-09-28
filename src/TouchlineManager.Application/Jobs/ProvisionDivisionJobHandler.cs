using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.World;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Generates one country's next tier, backfills its passed matchdays, and activates it when it validates
/// (`PYR-4`…`PYR-8`).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="ProvisionDivision"/>: it reads the request from the job's payload and calls
/// the use case, which is idempotent on the request's identity, so the queue's at-least-once delivery cannot
/// create a second tier. A malformed payload is a programming error in the enqueuer rather than a transient
/// fault, so it is dead-lettered rather than retried.
/// </remarks>
public sealed partial class ProvisionDivisionJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = ProvisioningJobTypes.Provision;

    private readonly ProvisionDivision _provision;
    private readonly ILogger<ProvisionDivisionJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public ProvisionDivisionJobHandler(ProvisionDivision provision, ILogger<ProvisionDivisionJobHandler> logger)
    {
        _provision = provision;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!ProvisioningJobPayload.TryRead(job.PayloadJson, out var requestId))
        {
            throw new PermanentJobFailureException(
                $"The provisioning job '{job.Id}' carried no readable request (PYR-4).");
        }

        var result = await _provision.ExecuteAsync(requestId, cancellationToken);

        LogProvisioned(result.CountryId, result.TargetTier, result.Clubs, result.BackfilledRounds);
    }

    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Information,
        Message = "Provisioned tier {TargetTier} of country {CountryId}: {Clubs} clubs, {Rounds} rounds backfilled (PYR-4).")]
    private partial void LogProvisioned(Guid countryId, int targetTier, int clubs, int rounds);
}
