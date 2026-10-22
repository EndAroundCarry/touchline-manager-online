using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Market;

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
    private readonly IInboxRepository _inbox;
    private readonly IWorldRepository _world;
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
        IInboxRepository inbox,
        IWorldRepository world,
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
        _inbox = inbox;
        _world = world;
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

            // Nobody bought a player the board of a club in administration listed, so the board releases him and his agent pays the
            // club a share of his value (DEBT-4).
            if (listing.ListedByBoard && !listing.IsFreeAgent)
            {
                await ReleaseUnsoldAsync(listing, now, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        // A free agent signs into the season being played, so the window where no season is playing waits
        // for the next one. Checked before anything is staged, so the throw leaves nothing behind (FA-5).
        var signingSeason = listing.IsFreeAgent ? await RequireSigningSeasonAsync(listing, cancellationToken) : null;

        var reason = signingSeason is null
            ? await RejectAsync(listing, leading, cancellationToken)
            : await RejectFreeAgentAsync(leading, listing.PlayerId, cancellationToken);

        if (reason is not null)
        {
            leading.Invalidate(now);
            listing.Expire(now);

            // The failed leader still holds a reservation, and nothing else will ever release it (FIN-10).
            var bidderAccount = await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);

            if (bidderAccount is not null)
            {
                _ledger.Add(bidderAccount.Post(
                    LedgerPostings.ReservationRelease(
                        Guid.CreateVersion7(),
                        leading.BidderClubId,
                        leading.Id,
                        leading.AmountMinor),
                    now));
            }

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

        if (signingSeason is null)
        {
            await SettleAsync(listing, leading, now, cancellationToken);
        }
        else
        {
            await SettleFreeAgentAsync(listing, leading, signingSeason, now, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Revalidates a leading bid, returning a reason code when it must be skipped (`TRF-9`, `TRF-11`).</summary>
    private async Task<string?> RejectAsync(
        TransferListing listing,
        TransferBid leading,
        CancellationToken cancellationToken)
    {
        var sellerClubId = SellerOf(listing);

        if (leading.BidderClubId == sellerClubId)
        {
            return TransferOutcomeReasons.PlayerIneligible;
        }

        var eligibility = await _roster.GetListingEligibilityAsync(listing.PlayerId, cancellationToken);

        if (eligibility is null
            || eligibility.ClubId != sellerClubId
            || !eligibility.HasActiveRegistration)
        {
            return TransferOutcomeReasons.PlayerIneligible;
        }

        var seller = await _roster.GetCompositionAsync(sellerClubId, cancellationToken);
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
        var sellerClubId = SellerOf(listing);
        var buyerAccount = await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);
        var sellerAccount = await _accounts.FindByClubAsync(sellerClubId, cancellationToken);
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
            LedgerPostings.TransferProceeds(Guid.CreateVersion7(), sellerClubId, listing.Id, fee),
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
            listing.GeneratedBuyerSeasonWageMinor,
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
            (await _world.FindWorldAsync(cancellationToken))?.Id,
            leading.BidderClubId,
            sellerClubId,
            listing.PlayerId,
            fee,
            now,
            cancellationToken);
    }

    /// <summary>
    /// The season a free agent signs into. While the season is closing no contract may start, so the
    /// resolution throws and the job queue retries it after the next season is active (`CON-8`, `FA-5`).
    /// </summary>
    private async Task<CurrentSeasonFacts> RequireSigningSeasonAsync(
        TransferListing listing,
        CancellationToken cancellationToken)
    {
        var season = await _roster.GetCurrentSeasonAsync(cancellationToken);

        if (season is null || season.IsRollover)
        {
            throw new InvalidOperationException(
                $"Listing {listing.Id} cannot sign a free agent while the season rolls over; it is retried "
                + "once the next season is active (FA-5).");
        }

        return season;
    }

    /// <summary>
    /// Revalidates a free agent's leading bid: he is still unsigned, the buyer has a place for him and the
    /// cash (`FA-5`). There is no seller to check.
    /// </summary>
    private async Task<string?> RejectFreeAgentAsync(
        TransferBid leading,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var players = await _squad.LoadPlayersAsync([playerId], cancellationToken);

        if (players.Count != 1
            || players[0].Status != PlayerStatus.FreeAgent
            || await _squad.FindActiveContractAsync(playerId, cancellationToken) is not null)
        {
            return TransferOutcomeReasons.PlayerIneligible;
        }

        var buyer = await _roster.GetCompositionAsync(leading.BidderClubId, cancellationToken);

        if (buyer is null)
        {
            return TransferOutcomeReasons.PlayerIneligible;
        }

        if (buyer.RegisteredCount + 1 > WorldRuleSet.SquadMaximumRegistered)
        {
            return TransferOutcomeReasons.BuyerAboveMaximum;
        }

        var account = await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);

        return account is null || account.CashMinor < leading.AmountMinor
            ? TransferOutcomeReasons.FundsUnavailable
            : null;
    }

    /// <summary>
    /// Settles a free agent's winning bid: the buyer pays and nobody is credited, because the fee leaves the
    /// game, and the player gets a contract and a registration (`FA-5`).
    /// </summary>
    private async Task SettleFreeAgentAsync(
        TransferListing listing,
        TransferBid leading,
        CurrentSeasonFacts season,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var buyerAccount = await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);
        var player = (await _squad.LoadPlayersAsync([listing.PlayerId], cancellationToken)).SingleOrDefault();

        // Rechecked after RejectFreeAgentAsync, so a missing row here is a defect, not a refusal.
        if (buyerAccount is null || player is null)
        {
            throw new InvalidOperationException(
                "A free-agent listing that passed revalidation lost its account or player before settlement (FA-5).");
        }

        var fee = leading.AmountMinor;

        _ledger.Add(buyerAccount.Post(
            LedgerPostings.TransferPayment(Guid.CreateVersion7(), leading.BidderClubId, listing.Id, fee),
            now));

        var age = season.GameYear - player.BirthGameYear;
        var squadStatus = FreeAgentTerms.SquadStatusFor(age);

        player.SignFromFreeAgency(now);

        var newContract = PlayerContract.Sign(
            Guid.CreateVersion7(),
            listing.PlayerId,
            leading.BidderClubId,
            season.SequenceNumber,
            season.SequenceNumber + listing.GeneratedContractSeasons - 1,
            listing.GeneratedBuyerSeasonWageMinor,
            squadStatus,
            now);

        _squad.AddPlayerContract(newContract);
        _squad.AddPlayerRegistration(PlayerRegistration.Register(
            Guid.CreateVersion7(),
            listing.PlayerId,
            leading.BidderClubId,
            season.SeasonId,
            effectiveFixtureBoundaryRound: 0,
            now));

        _outcomes.Add(TransferOutcome.Record(
            Guid.CreateVersion7(),
            listing.Id,
            leading.Id,
            listing.PlayerId,
            sellerClubId: null,
            leading.BidderClubId,
            fee,
            oldContractId: null,
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

        await _notifications.NotifyFreeAgentSignedAsync(
            (await _world.FindWorldAsync(cancellationToken))?.Id,
            leading.BidderClubId,
            listing.PlayerId,
            fee,
            now,
            cancellationToken);
    }

    /// <summary>
    /// The selling club of a club listing. A free-agent listing has none and settles on its own path, so
    /// reaching it here is a defect rather than a case to handle.
    /// </summary>
    private static Guid SellerOf(TransferListing listing) =>
        listing.SellerClubId
        ?? throw new InvalidOperationException(
            $"Listing {listing.Id} is a free-agent listing and has no seller club to settle against.");

    /// <summary>
    /// Releases a player the board listed and nobody bought, and credits the club with the fee his agent pays (`DEBT-4`).
    /// </summary>
    /// <remarks>
    /// Nothing is owed to the player: the release is the board's, not the club's choice, so it carries no payout. A release that would leave
    /// the club below the minimum squad or without two goalkeepers is not made, and the board lists him again at the next reading.
    /// </remarks>
    private async Task ReleaseUnsoldAsync(TransferListing listing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var clubId = SellerOf(listing);
        var eligibility = await _roster.GetListingEligibilityAsync(listing.PlayerId, cancellationToken);

        if (eligibility is null || eligibility.ClubId != clubId || !eligibility.HasActiveRegistration)
        {
            return;
        }

        var keeper = eligibility.PrimaryPosition == PlayerPosition.Goalkeeper;

        if (!SquadLegality.MeetsMinimum(eligibility.RegisteredCount - 1)
            || eligibility.GoalkeeperCount - (keeper ? 1 : 0) < WorldRuleSet.MinimumGoalkeepers)
        {
            return;
        }

        var contract = await _squad.FindContractAsync(eligibility.ContractId, cancellationToken);
        var registration = await _squad.FindActiveRegistrationAsync(listing.PlayerId, cancellationToken);
        var player = (await _squad.LoadPlayersAsync([listing.PlayerId], cancellationToken)).SingleOrDefault();
        var account = await _accounts.FindByClubAsync(clubId, cancellationToken);

        // A club that has climbed out of administration keeps the player nobody bought: the board's reason is gone.
        if (contract is null
            || !contract.IsActive
            || player is null
            || account is not { FinancialState: FinancialStateKind.Administration })
        {
            return;
        }

        contract.Close(PlayerContractCloseReasons.Released, now);
        registration?.End(now);
        player.ReleaseToFreeAgency(now);

        // The listing asked 80% of his value, so his value is read back from the asking price.
        var valueMinor = listing.MinimumFeeMinor * EconomyRuleSet.OneHundredPercentBp / EconomyRuleSet.AdministrationAskingBp;
        var fee = EconomyRuleSet.AdministrationReleaseFeeMinor(valueMinor);

        if (fee > 0)
        {
            _ledger.Add(account.Post(
                LedgerPostings.ReleaseFee(Guid.CreateVersion7(), clubId, listing.Id, fee),
                now));
        }

        _audit.Record(new AuditEntry(
            FinanceAuditActions.BoardReleasedPlayer,
            AuditActorTypes.Service,
            ActorUserId: null,
            AuditTargetTypes.PlayerContract,
            contract.Id,
            LedgerPostings.TransferCorrelationId(listing.Id),
            IpHash: null,
            Reason: $"Nobody bought the player the board listed; his agent paid {fee} (DEBT-4)."));

        var target = (await _inbox.FindClubTargetsAsync([clubId], cancellationToken))
            .FirstOrDefault(candidate => candidate.ClubId == clubId);

        if (target?.ManagerId is { } managerId && managerId != Guid.Empty)
        {
            var names = await _inbox.FindPlayerNamesAsync([listing.PlayerId], cancellationToken);
            var draft = FinanceInboxTemplates.BoardReleasedMessage(
                names.TryGetValue(listing.PlayerId, out var name) ? name : "A player",
                fee,
                listing.PlayerId);

            _inbox.Add(InboxMessage.Record(
                Guid.CreateVersion7(),
                managerId,
                draft.Category,
                draft.TemplateKey,
                draft.ParametersJson,
                draft.RelatedEntityId,
                now));
        }
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
