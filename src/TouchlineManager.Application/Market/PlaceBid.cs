using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Finance;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Market;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Places or raises a bid, reserving the amount and releasing the leader it displaces (`TRF-4`…`TRF-7`).
/// </summary>
/// <remarks>
/// <para>
/// A club has at most one active bid per listing and raises it rather than adding a second (`TRF-6`), and a
/// bid is only accepted when it clears the minimum fee or the minimum raise (`TRF-5`) and the club holds the
/// cash after its existing reservations (`FIN-10`). The amount is reserved through the ledger the moment the
/// bid leads (`TRF-7`); the club it displaces has its reservation released in the same transaction.
/// </para>
/// <para>
/// Nothing about the request is authoritative but the amount and the listing: the seller, the floor, and the
/// deadline all come from the stored listing (`INT-1`).
/// </para>
/// </remarks>
public sealed class PlaceBid
{
    private readonly ResolveOwnedClub _access;
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IMarketQueries _queries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditWriter _audit;
    private readonly MarketNotifications _notifications;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the use case.</summary>
    public PlaceBid(
        ResolveOwnedClub access,
        IListingRepository listings,
        IBidRepository bids,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IMarketQueries queries,
        IUnitOfWork unitOfWork,
        IAuditWriter audit,
        MarketNotifications notifications,
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
        _notifications = notifications;
        _clock = clock;
        _requestContext = requestContext;
        _secureTokens = secureTokens;
    }

    /// <summary>Places or raises a bid, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="listingId">The listing bid on.</param>
    /// <param name="amountMinor">The amount, in minor units.</param>
    /// <param name="idempotencyKey">The request's idempotency key, which a retry is matched against (`INT-2`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListingResult> ExecuteAsync(
        Guid userId,
        Guid listingId,
        long amountMinor,
        string? idempotencyKey,
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

        if (!listing.IsOpen)
        {
            return new ListingResult(MarketOutcome.NotOpen, null);
        }

        if (listing.SellerClubId == access.ClubId)
        {
            return new ListingResult(MarketOutcome.CannotBidOnOwnPlayer, null);
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replayed = await _bids.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

            if (replayed is not null)
            {
                var identical = replayed.ListingId == listing.Id
                    && replayed.BidderClubId == access.ClubId
                    && replayed.AmountMinor == amountMinor;

                return identical
                    ? new ListingResult(
                        MarketOutcome.Found,
                        await _queries.GetListingAsync(listing.Id, access.ClubId, cancellationToken))
                    : new ListingResult(MarketOutcome.IdempotencyKeyReused, null);
            }
        }

        var leading = await _bids.FindLeadingBidAsync(listing.Id, cancellationToken);
        var mine = leading is not null && leading.BidderClubId == access.ClubId ? leading : null;

        if (!AuctionRules.IsAcceptableBid(listing.MinimumFeeMinor, leading?.AmountMinor, amountMinor))
        {
            return new ListingResult(MarketOutcome.BidTooLow, null);
        }

        var account = await _accounts.FindByClubAsync(access.ClubId, cancellationToken);

        if (account is null)
        {
            return new ListingResult(MarketOutcome.Conflict, null);
        }

        // Raising replaces this club's own reservation: the amount already reserved for its leading bid is
        // free to count against the new one (TRF-7).
        var spendable = account.AvailableMinor + (mine?.AmountMinor ?? 0);

        if (amountMinor > spendable)
        {
            return new ListingResult(MarketOutcome.InsufficientFunds, null);
        }

        var now = _clock.UtcNow;
        Guid bidId;

        if (leading is not null)
        {
            // Release the displaced reservation before reserving the new one, so the account never has to hold
            // both at once (FIN-10).
            _ledger.Add(account.Post(
                LedgerPostings.ReservationRelease(
                    Guid.CreateVersion7(),
                    leading.BidderClubId,
                    leading.Id,
                    leading.AmountMinor),
                now));
        }

        if (mine is not null)
        {
            mine.Raise(amountMinor, idempotencyKey, now);
            bidId = mine.Id;
        }
        else
        {
            bidId = Guid.CreateVersion7();

            if (leading is not null)
            {
                leading.Outbid(now);
            }

            _bids.Add(TransferBid.Place(
                bidId,
                listing.Id,
                access.ClubId,
                amountMinor,
                LedgerPostings.ReservationCorrelationId(bidId, amountMinor),
                idempotencyKey,
                now));
        }

        _ledger.Add(account.Post(
            LedgerPostings.BidReservation(Guid.CreateVersion7(), access.ClubId, bidId, amountMinor),
            now));

        _audit.Record(new AuditEntry(
            mine is null ? MarketAuditActions.BidPlaced : MarketAuditActions.BidRaised,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.TransferBid,
            bidId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        if (leading is not null && mine is null)
        {
            await _notifications.NotifyOutbidAsync(
                leading.BidderClubId,
                listing.PlayerId,
                amountMinor,
                listing.Id,
                now,
                cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ListingResult(
            MarketOutcome.Found,
            await _queries.GetListingAsync(listing.Id, access.ClubId, cancellationToken));
    }
}
