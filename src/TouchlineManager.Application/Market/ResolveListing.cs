using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Finance;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Resolves one listing at its window: picks the winning bid, revalidates it, and settles the transfer
/// (`TRF-8`…`TRF-11`).
/// </summary>
/// <remarks>
/// <para>
/// The whole workflow runs in one serializable transaction, so a bid cannot slip in after the winner is
/// chosen and a retried job cannot settle twice (`TRF-9`, `FIN-17`). The winner is the highest valid amount,
/// with equal amounts going to the lowest database-assigned bid sequence and then the immutable bid identity
/// (`TRF-8`).
/// </para>
/// <para>
/// The winner is revalidated against the accounts, the buyer's squad ceiling, the seller's squad floor, and
/// the player's still-active contract and registration (`TRF-9`). A leading bid that fails is marked invalid
/// and the listing expires, because bids are ascending and only the leader holds a reservation (`TRF-11`);
/// every outcome is audited and the buyer and seller are told (`TRF-10`).
/// </para>
/// </remarks>
public sealed class ResolveListing
{
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly ITransferOutcomeRepository _outcomes;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly ISquadRepository _squad;
    private readonly ISquadQueries _squadQueries;
    private readonly IRosterQueries _roster;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditWriter _audit;
    private readonly MarketNotifications _notifications;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public ResolveListing(
        IListingRepository listings,
        IBidRepository bids,
        ITransferOutcomeRepository outcomes,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        ISquadRepository squad,
        ISquadQueries squadQueries,
        IRosterQueries roster,
        IUnitOfWork unitOfWork,
        IAuditWriter audit,
        MarketNotifications notifications,
        IClock clock)
    {
        _listings = listings;
        _bids = bids;
        _outcomes = outcomes;
        _accounts = accounts;
        _ledger = ledger;
        _squad = squad;
        _squadQueries = squadQueries;
        _roster = roster;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
    }

    /// <summary>Resolves one listing, if it is still open.</summary>
    /// <param name="listingId">The listing to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteAsync(Guid listingId, CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.Serializable,
            cancellationToken);

        var listing = await _listings.FindForResolutionAsync(listingId, cancellationToken);

        // Already resolved, cancelled, or never existed: a retried job is a no-op (idempotent on the key).
        if (listing is null || !listing.IsOpen)
        {
            await transaction.RollbackAsync(cancellationToken);

            return;
        }

        var now = _clock.UtcNow;
        var bids = await _bids.ListForListingAsync(listing.Id, cancellationToken);
        var leading = bids.FirstOrDefault(bid => bid.IsLeading);

        if (leading is null)
        {
            listing.Expire(now);
            AuditResolution(listing, TransferOutcomeReasons.NoValidBid, now);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        var reason = await RejectAsync(listing, leading, cancellationToken);

        if (reason is not null)
        {
            leading.Invalidate(now);
            listing.Expire(now);

            _audit.Record(new AuditEntry(
                MarketAuditActions.BidInvalidated,
                AuditActorTypes.Service,
                ActorUserId: null,
                AuditTargetTypes.TransferBid,
                leading.Id,
                LedgerPostings.TransferCorrelationId(listing.Id),
                IpHash: null,
                Reason: reason));

            AuditResolution(listing, reason, now);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        await SettleAsync(listing, leading, now, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Revalidates a leading bid, returning a reason code when it must be skipped (`TRF-9`, `TRF-11`).</summary>
    private async Task<string?> RejectAsync(
        TransferListing listing,
        TransferBid leading,
        CancellationToken cancellationToken)
    {
        if (leading.BidderClubId == listing.SellerClubId)
        {
            return TransferOutcomeReasons.PlayerIneligible;
        }

        var eligibility = await _roster.GetListingEligibilityAsync(listing.PlayerId, cancellationToken);

        if (eligibility is null
            || eligibility.ClubId != listing.SellerClubId
            || !eligibility.HasActiveRegistration)
        {
            return TransferOutcomeReasons.PlayerIneligible;
        }

        var seller = await _roster.GetCompositionAsync(listing.SellerClubId, cancellationToken);
        var buyer = await _roster.GetCompositionAsync(leading.BidderClubId, cancellationToken);

        if (seller is null || buyer is null)
        {
            return TransferOutcomeReasons.PlayerIneligible;
        }

        var remainingSquad = seller.RegisteredCount - 1;
        var remainingGoalkeepers = seller.GoalkeeperCount
            - (eligibility.PrimaryPosition == PlayerPosition.Goalkeeper ? 1 : 0);

        if (!SquadLegality.MeetsMinimum(remainingSquad)
            || remainingGoalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            return TransferOutcomeReasons.SellerBelowMinimum;
        }

        if (buyer.RegisteredCount + 1 > WorldRuleSet.SquadMaximumRegistered)
        {
            return TransferOutcomeReasons.BuyerAboveMaximum;
        }

        var account = await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);

        if (account is null || account.CashMinor < leading.AmountMinor)
        {
            return TransferOutcomeReasons.FundsUnavailable;
        }

        return null;
    }

    /// <summary>Settles a valid winning bid as one transfer (`TRF-10`).</summary>
    private async Task SettleAsync(
        TransferListing listing,
        TransferBid leading,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var buyerAccount = await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);
        var sellerAccount = await _accounts.FindByClubAsync(listing.SellerClubId, cancellationToken);
        var sellerContract = await _squad.FindActiveContractAsync(listing.PlayerId, cancellationToken);
        var sellerRegistration = await _squad.FindActiveRegistrationAsync(listing.PlayerId, cancellationToken);

        // Rechecked after RejectAsync, so a missing row here is a defect rather than an ordinary refusal; the
        // serializable transaction guarantees nothing changed between the two reads.
        if (buyerAccount is null || sellerAccount is null
            || sellerContract is null || sellerRegistration is null)
        {
            throw new InvalidOperationException(
                "A listing that passed revalidation lost its account or contract before settlement (TRF-9).");
        }

        var fee = leading.AmountMinor;

        _ledger.Add(buyerAccount.Post(
            LedgerPostings.TransferPayment(Guid.CreateVersion7(), leading.BidderClubId, listing.Id, fee),
            now));
        _ledger.Add(sellerAccount.Post(
            LedgerPostings.TransferProceeds(Guid.CreateVersion7(), listing.SellerClubId, listing.Id, fee),
            now));

        var renewal = await _squadQueries.GetRenewalContextAsync(sellerContract.Id, cancellationToken);
        var startSeason = renewal?.CurrentSeasonNumber ?? 1;
        var oldContractId = sellerContract.Id;
        var squadStatus = sellerContract.SquadStatus;

        sellerContract.Close(PlayerContractCloseReasons.Transferred, now);
        sellerRegistration.End(now);

        var newContract = PlayerContract.Sign(
            Guid.CreateVersion7(),
            listing.PlayerId,
            leading.BidderClubId,
            startSeason,
            startSeason + listing.GeneratedContractSeasons - 1,
            listing.GeneratedBuyerWageMinor,
            squadStatus,
            now);

        _squad.AddPlayerContract(newContract);
        _squad.AddPlayerRegistration(PlayerRegistration.Register(
            Guid.CreateVersion7(),
            listing.PlayerId,
            leading.BidderClubId,
            sellerRegistration.EffectiveSeasonId,
            // Effective from the next fixture onwards (SQ-7): snapshots already frozen are unaffected, and the
            // player is available to any fixture that locks after this transfer.
            effectiveFixtureBoundaryRound: 0,
            now));

        _outcomes.Add(TransferOutcome.Record(
            Guid.CreateVersion7(),
            listing.Id,
            leading.Id,
            listing.PlayerId,
            listing.SellerClubId,
            leading.BidderClubId,
            fee,
            oldContractId,
            newContract.Id,
            LedgerPostings.TransferCorrelationId(listing.Id),
            now));

        leading.Win(now);
        listing.MarkSold(now);

        _audit.Record(new AuditEntry(
            MarketAuditActions.ListingSold,
            AuditActorTypes.Service,
            ActorUserId: null,
            AuditTargetTypes.TransferOutcome,
            listing.Id,
            LedgerPostings.TransferCorrelationId(listing.Id),
            IpHash: null,
            Reason: null));

        await _notifications.NotifyTransferAsync(
            leading.BidderClubId,
            listing.SellerClubId,
            listing.PlayerId,
            fee,
            now,
            cancellationToken);
    }

    private void AuditResolution(TransferListing listing, string reason, DateTimeOffset now)
    {
        _audit.Record(new AuditEntry(
            MarketAuditActions.ListingExpired,
            AuditActorTypes.Service,
            ActorUserId: null,
            AuditTargetTypes.TransferListing,
            listing.Id,
            LedgerPostings.TransferCorrelationId(listing.Id),
            IpHash: null,
            Reason: reason));
    }
}
