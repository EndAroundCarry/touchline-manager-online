using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Market;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Resolves one transfer listing at its daily window (`TRF-2`, `TRF-9`, `TRF-10`).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="ResolveListing"/>: it reads the listing from the job's payload and calls the
/// use case, which is idempotent on the listing's identity, so the queue's at-least-once delivery cannot settle
/// a transfer twice. A malformed payload is a programming error in the enqueuer rather than a transient fault,
/// so it is dead-lettered rather than retried.
/// </remarks>
public sealed partial class ResolveAuctionJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = AuctionJobTypes.Resolve;

    private readonly ResolveListing _resolve;
    private readonly ILogger<ResolveAuctionJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public ResolveAuctionJobHandler(ResolveListing resolve, ILogger<ResolveAuctionJobHandler> logger)
    {
        _resolve = resolve;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!AuctionJobPayload.TryRead(job.PayloadJson, out var listingId))
        {
            throw new PermanentJobFailureException(
                $"The auction resolution job '{job.Id}' carried no readable listing (TRF-2).");
        }

        await _resolve.ExecuteAsync(listingId, cancellationToken);

        LogResolved(listingId);
    }

    [LoggerMessage(
        EventId = 3500,
        Level = LogLevel.Information,
        Message = "Resolved the transfer listing {ListingId} (TRF-2).")]
    private partial void LogResolved(Guid listingId);
}
