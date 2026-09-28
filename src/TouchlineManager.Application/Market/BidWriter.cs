using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Finance;
using TouchlineManager.Domain.Market;

namespace TouchlineManager.Application.Market;

/// <summary>What a bid write did.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="ListingId">The listing bid on, when the write succeeded.</param>
/// <param name="BidId">The bid that now leads, when this call placed or raised one.</param>
/// <param name="Created">Whether this call placed or raised a bid, as opposed to replaying an existing one (`T-4`).</param>
public sealed record BidWriteResult(MarketOutcome Outcome, Guid ListingId, Guid BidId, bool Created);

/// <summary>The bid core shared by a manager's command and the AI's evaluation (`TRF-4`, `INS-12`).</summary>
public interface IBidWriter
{
    /// <summary>Places or raises a bid for a club, or refuses.</summary>
    /// <param name="clubId">The bidding club.</param>
    /// <param name="actor">Who is acting.</param>
    /// <param name="listingId">The listing bid on.</param>
    /// <param name="amountMinor">The amount, in minor units.</param>
    /// <param name="idempotencyKey">The request's idempotency key, which a retry is matched against (`T-4`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<BidWriteResult> BidAsync(
        Guid clubId,
        MarketActor actor,
        Guid listingId,
        long amountMinor,
        string? idempotencyKey,
        CancellationToken cancellationToken);
}

/// <summary>
/// Places or raises a bid, reserving the amount and releasing the leader it displaces (`TRF-4`…`TRF-7`).
/// </summary>
/// <remarks>
/// <para>
/// The bid core, shared by a manager's <see cref="PlaceBid"/> command and the AI's market evaluation
/// (`INS-12`). It stages the bid, the ledger postings, the outbid notification, and the audit row, and never
/// saves, so the caller commits them beside whatever else the operation changes.
/// </para>
/// <para>
/// A club has at most one active bid per listing and raises it rather than adding a second (`TRF-6`); a bid
/// is accepted only when it clears the floor or the minimum raise (`TRF-5`) and the club holds the cash
/// after its existing reservations (`FIN-10`). The amount is reserved through the ledger the moment the bid
/// leads, and the club it displaces has its reservation released in the same unit of work (`TRF-7`).
/// </para>
/// <para>
/// Bids on one listing are serialised with a transaction-scoped advisory lock, so the leader a bid displaces
/// is read under that lock rather than concurrently by two clubs. The caller must have begun a transaction,
/// which is what makes the lock live until the bid is committed (`ADR-0026`).
/// </para>
/// </remarks>
public sealed class BidWriter : IBidWriter
{
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IAuditWriter _audit;
    private readonly MarketNotifications _notifications;
    private readonly IAdvisoryLock _locks;
    private readonly IClock _clock;

    /// <summary>Initializes the writer.</summary>
    public BidWriter(
        IListingRepository listings,
        IBidRepository bids,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IAuditWriter audit,
        MarketNotifications notifications,
        IAdvisoryLock locks,
        IClock clock)
    {
        _listings = listings;
        _bids = bids;
        _accounts = accounts;
        _ledger = ledger;
        _audit = audit;
        _notifications = notifications;
        _locks = locks;
        _clock = clock;
    }

    /// <summary>Places or raises a bid for a club, or refuses.</summary>
    /// <param name="clubId">The bidding club.</param>
    /// <param name="actor">Who is acting.</param>
    /// <param name="listingId">The listing bid on.</param>
    /// <param name="amountMinor">The amount, in minor units.</param>
    /// <param name="idempotencyKey">The request's idempotency key, which a retry is matched against (`T-4`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BidWriteResult> BidAsync(
        Guid clubId,
        MarketActor actor,
        Guid listingId,
        long amountMinor,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var listing = await _listings.FindAsync(listingId, cancellationToken);

        if (listing is null)
        {
            return Refused(MarketOutcome.NotFound);
        }

        if (!listing.IsOpen)
        {
            return Refused(MarketOutcome.NotOpen);
        }

        if (listing.SellerClubId == clubId)
        {
            return Refused(MarketOutcome.CannotBidOnOwnPlayer);
        }

        // Serialise the bids on this listing, so "who leads now" is decided under the lock rather than read
        // concurrently by two clubs (TRF-7, ADR-0026). The lock is transaction-scoped: the caller must have
        // begun a transaction, which PlaceBid and the AI pass both do.
        await _locks.AcquireAsync(AdvisoryLockKey.Listing(listing.Id), cancellationToken);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replayed = await _bids.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

            if (replayed is not null)
            {
                var identical = replayed.ListingId == listing.Id
                    && replayed.BidderClubId == clubId
                    && replayed.AmountMinor == amountMinor;

                return identical
                    ? new BidWriteResult(MarketOutcome.Found, listing.Id, replayed.Id, Created: false)
                    : Refused(MarketOutcome.IdempotencyKeyReused);
            }
        }

        var leading = await _bids.FindLeadingBidAsync(listing.Id, cancellationToken);
        var mine = leading is not null && leading.BidderClubId == clubId ? leading : null;

        if (!AuctionRules.IsAcceptableBid(listing.MinimumFeeMinor, leading?.AmountMinor, amountMinor))
        {
            return Refused(MarketOutcome.BidTooLow);
        }

        var account = await _accounts.FindByClubAsync(clubId, cancellationToken);

        if (account is null)
        {
            return Refused(MarketOutcome.Conflict);
        }

        // Raising replaces this club's own reservation: the amount already reserved for its leading bid is
        // free to count against the new one (TRF-7).
        var spendable = account.AvailableMinor + (mine?.AmountMinor ?? 0);

        if (amountMinor > spendable)
        {
            return Refused(MarketOutcome.InsufficientFunds);
        }

        var now = _clock.UtcNow;
        Guid bidId;

        if (leading is not null)
        {
            // Release the displaced reservation before reserving the new one, so neither account has to hold
            // both at once (FIN-10). The leader is this club when it is raising its own bid, and another club
            // when it is outbidding — and the release must come off the account that actually holds it, or the
            // outbid bidder's reservation would be left standing and this club's would go negative.
            var displacedAccount = mine is not null
                ? account
                : await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);

            if (displacedAccount is null)
            {
                throw new InvalidOperationException(
                    "The bid being displaced lost its account before the release (FIN-10).");
            }

            _ledger.Add(displacedAccount.Post(
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
                clubId,
                amountMinor,
                LedgerPostings.ReservationCorrelationId(bidId, amountMinor),
                idempotencyKey,
                now));
        }

        _ledger.Add(account.Post(
            LedgerPostings.BidReservation(Guid.CreateVersion7(), clubId, bidId, amountMinor),
            now));

        _audit.Record(new AuditEntry(
            mine is null ? MarketAuditActions.BidPlaced : MarketAuditActions.BidRaised,
            actor.ActorType,
            actor.UserId,
            AuditTargetTypes.TransferBid,
            bidId,
            actor.CorrelationId,
            actor.IpHash,
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

        return new BidWriteResult(MarketOutcome.Found, listing.Id, bidId, Created: true);
    }

    private static BidWriteResult Refused(MarketOutcome outcome) =>
        new(outcome, Guid.Empty, Guid.Empty, Created: false);
}
