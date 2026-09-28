using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Application.Market;

/// <summary>The result of a diagnostics trigger for one listing's resolution (`TRF-2`).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="ListingId">The listing that was triggered.</param>
/// <param name="ResolveKey">The business key of the resolution job.</param>
/// <param name="Enqueued">Whether the job row was newly inserted.</param>
public sealed record TriggerAuctionsResult(
    MarketOutcome Outcome,
    Guid ListingId,
    string ResolveKey,
    bool Enqueued);

/// <summary>
/// Enqueues a listing's resolution job due now, so a non-production journey can watch an auction settle
/// without waiting for its window (ADR-0016's pattern, applied to the market).
/// </summary>
/// <remarks>
/// It does what the worker-only scheduler normally does and nothing more: the worker still resolves the
/// listing exactly as it would on the calendar. It has no production surface — the endpoint that reaches it
/// is mapped only when the diagnostics flag is on.
/// </remarks>
public sealed class TriggerAuctions
{
    private readonly IListingRepository _listings;
    private readonly IJobQueue _jobs;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public TriggerAuctions(IListingRepository listings, IJobQueue jobs, IClock clock)
    {
        _listings = listings;
        _jobs = jobs;
        _clock = clock;
    }

    /// <summary>Enqueues the listing's resolution job, or reports that it does not exist.</summary>
    /// <param name="listingId">The listing to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TriggerAuctionsResult> ExecuteAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await _listings.FindAsync(listingId, cancellationToken);
        var key = AuctionJobTypes.ResolveKey(listingId);

        if (listing is null)
        {
            return new TriggerAuctionsResult(MarketOutcome.NotFound, listingId, key, false);
        }

        var enqueued = await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = AuctionJobTypes.Resolve,
                BusinessKey = key,
                DueAt = _clock.UtcNow,
                PayloadJson = AuctionJobPayload.For(listing.Id),
            },
            cancellationToken);

        return new TriggerAuctionsResult(MarketOutcome.Found, listing.Id, key, enqueued);
    }
}
