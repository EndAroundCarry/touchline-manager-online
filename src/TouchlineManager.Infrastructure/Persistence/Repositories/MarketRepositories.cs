using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Domain.Market;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>The market module's write-side persistence (`TRF-1`…`TRF-15`).</summary>
/// <remarks>
/// Staging ports, like the rest of the product's repositories: they add and load tracked rows for the current
/// unit of work and never save, so a workflow that spans the market, finance, and squad modules commits once
/// (`MOD-2`).
/// </remarks>
internal sealed class ShortlistRepository : IShortlistRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public ShortlistRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(ShortlistEntry entry) => _dbContext.ShortlistEntries.Add(entry);

    /// <inheritdoc />
    public Task<ShortlistEntry?> FindAsync(Guid managerId, Guid playerId, CancellationToken cancellationToken) =>
        _dbContext.ShortlistEntries.SingleOrDefaultAsync(
            entry => entry.ManagerId == managerId && entry.PlayerId == playerId,
            cancellationToken);

    /// <inheritdoc />
    public void Remove(ShortlistEntry entry) => _dbContext.ShortlistEntries.Remove(entry);
}

/// <summary>The market module's listing persistence (`TRF-1`, `TRF-2`, `TRF-14`).</summary>
internal sealed class ListingRepository : IListingRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public ListingRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(TransferListing listing) => _dbContext.TransferListings.Add(listing);

    /// <inheritdoc />
    public Task<TransferListing?> FindAsync(Guid listingId, CancellationToken cancellationToken) =>
        _dbContext.TransferListings.SingleOrDefaultAsync(
            listing => listing.Id == listingId,
            cancellationToken);

    /// <inheritdoc />
    public Task<TransferListing?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _dbContext.TransferListings.SingleOrDefaultAsync(
            listing => listing.IdempotencyKey == idempotencyKey,
            cancellationToken);

    /// <inheritdoc />
    public Task<TransferListing?> FindForResolutionAsync(Guid listingId, CancellationToken cancellationToken) =>
        _dbContext.TransferListings.SingleOrDefaultAsync(
            listing => listing.Id == listingId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransferListing>> ListDueAsync(
        DateTimeOffset dueAt,
        CancellationToken cancellationToken) =>
        await _dbContext.TransferListings
            .Where(listing => listing.Status == ListingStatus.Open && listing.EndsAt <= dueAt)
            .OrderBy(listing => listing.EndsAt)
            .Take(500)
            .ToListAsync(cancellationToken);
}

/// <summary>The market module's bid persistence (`TRF-4`, `TRF-6`, `TRF-8`).</summary>
internal sealed class BidRepository : IBidRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public BidRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(TransferBid bid) => _dbContext.TransferBids.Add(bid);

    /// <inheritdoc />
    public Task<TransferBid?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _dbContext.TransferBids.SingleOrDefaultAsync(
            bid => bid.IdempotencyKey == idempotencyKey,
            cancellationToken);

    /// <inheritdoc />
    public Task<TransferBid?> FindActiveBidAsync(
        Guid listingId,
        Guid bidderClubId,
        CancellationToken cancellationToken) =>
        _dbContext.TransferBids.SingleOrDefaultAsync(
            bid => bid.ListingId == listingId
                && bid.BidderClubId == bidderClubId
                && bid.Status == BidStatus.Leading,
            cancellationToken);

    /// <inheritdoc />
    public Task<TransferBid?> FindLeadingBidAsync(Guid listingId, CancellationToken cancellationToken) =>
        _dbContext.TransferBids.SingleOrDefaultAsync(
            bid => bid.ListingId == listingId && bid.Status == BidStatus.Leading,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransferBid>> ListForListingAsync(
        Guid listingId,
        CancellationToken cancellationToken) =>
        await _dbContext.TransferBids
            .Where(bid => bid.ListingId == listingId)
            // The winning order (TRF-8): highest amount, then earliest database-assigned sequence, then id.
            .OrderByDescending(bid => bid.AmountMinor)
            .ThenBy(bid => bid.BidSequence)
            .ThenBy(bid => bid.Id)
            .ToListAsync(cancellationToken);
}

/// <summary>The market module's resolved-transfer persistence (`TRF-10`).</summary>
internal sealed class TransferOutcomeRepository : ITransferOutcomeRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public TransferOutcomeRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(TransferOutcome outcome) => _dbContext.TransferOutcomes.Add(outcome);
}
