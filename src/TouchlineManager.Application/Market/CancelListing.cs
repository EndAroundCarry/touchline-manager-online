using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Withdraws an open listing, releasing every reservation and invalidating its bids (`TRF-15`).
/// </summary>
/// <remarks>
/// A listing has at most one active bid — the leader — because bids are ascending, so cancelling releases
/// exactly that reservation and marks the bid released. Every reservation is released before the listing
/// leaves <c>open</c>, so no club is left with money committed to an auction that no longer exists.
/// </remarks>
public sealed class CancelListing
{
    private readonly ResolveOwnedClub _access;
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IMarketQueries _queries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the use case.</summary>
    public CancelListing(
        ResolveOwnedClub access,
        IListingRepository listings,
        IBidRepository bids,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IMarketQueries queries,
        IUnitOfWork unitOfWork,
        IAuditWriter audit,
        IClock clock,
        IRequestContext requestContext,
        ISecureTokenService secureTokens)
    {
        _access = access;
        _listings = listings;
        _bids = bids;
        _accounts = accounts;
        _ledger = ledger;
        _queries = queries;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clock = clock;
        _requestContext = requestContext;
        _secureTokens = secureTokens;
    }

    /// <summary>Cancels a listing, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="listingId">The listing to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListingResult> ExecuteAsync(
        Guid userId,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new ListingResult(access.Outcome.FromAccess(), null);
        }

        var listing = await _listings.FindAsync(listingId, cancellationToken);

        if (listing is null)
        {
            return new ListingResult(MarketOutcome.NotFound, null);
        }

        if (listing.SellerClubId != access.ClubId)
        {
            return new ListingResult(MarketOutcome.ClubNotManaged, null);
        }

        // Cancelling an already-cancelled listing is a no-op rather than an error, so a retried cancel is safe.
        if (listing.Status == Domain.Market.ListingStatus.Cancelled)
        {
            return new ListingResult(
                MarketOutcome.Found,
                await _queries.GetListingAsync(listing.Id, access.ClubId, cancellationToken));
        }

        if (!listing.IsOpen)
        {
            return new ListingResult(MarketOutcome.NotOpen, null);
        }

        var now = _clock.UtcNow;
        var leading = await _bids.FindLeadingBidAsync(listing.Id, cancellationToken);

        if (leading is not null)
        {
            var account = await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);

            if (account is not null)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.ReservationRelease(
                        Guid.CreateVersion7(),
                        leading.BidderClubId,
                        leading.Id,
                        leading.AmountMinor),
                    now));
            }

            leading.Release(now);
        }

        listing.Cancel(now);

        _audit.Record(new AuditEntry(
            MarketAuditActions.ListingCancelled,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.TransferListing,
            listing.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ListingResult(
            MarketOutcome.Found,
            await _queries.GetListingAsync(listing.Id, access.ClubId, cancellationToken));
    }
}
