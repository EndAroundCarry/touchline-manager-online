using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Application.Finance;

/// <summary>What making room for a recurring cost did.</summary>
/// <param name="GrantedMinor">What the integrity net granted, 0 in the normal case (`FIN-16`).</param>
/// <param name="WithdrawnBids">How many leading bids were given up to free their reservations (`FIN-10`).</param>
public sealed record CostRoomResult(long GrantedMinor, int WithdrawnBids);

/// <summary>
/// Makes room in a club's account for a recurring cost the club owes whatever it holds: its wages, its operating cost and its upkeep
/// (`FIN-7`, `FIN-9`, `DEBT-1`).
/// </summary>
/// <remarks>
/// <para>
/// A recurring cost may take the cash below zero, down to the overdraft floor, so the cost itself is never refused. Two things can still stand
/// in its way, and this is where both are settled before the posting. The first is the integrity net: a cost that would take the cash past the
/// floor is met by a grant of exactly the difference, recorded as an audit action and raised as an operations alert (`FIN-16`). The second is
/// the reservations: a club's reserved funds are never more than its cash above zero, so a cost that would leave a leading bid reserved above
/// the cash has that bid withdrawn first, largest first, and the manager is told (`FIN-10`).
/// </para>
/// <para>
/// It stages ledger entries and rows and never saves, so the caller commits them with the cost.
/// </para>
/// </remarks>
public sealed partial class RecurringCostRoom
{
    private readonly IBidRepository _bids;
    private readonly IListingRepository _listings;
    private readonly ILedgerRepository _ledger;
    private readonly IInboxRepository _inbox;
    private readonly IAuditWriter _audit;
    private readonly ILogger<RecurringCostRoom> _logger;

    /// <summary>Initializes the collaborator.</summary>
    public RecurringCostRoom(
        IBidRepository bids,
        IListingRepository listings,
        ILedgerRepository ledger,
        IInboxRepository inbox,
        IAuditWriter audit,
        ILogger<RecurringCostRoom> logger)
    {
        _bids = bids;
        _listings = listings;
        _ledger = ledger;
        _inbox = inbox;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>Makes room for a cost, granting and withdrawing bids as needed.</summary>
    /// <param name="account">The club's account, tracked.</param>
    /// <param name="outflowMinor">What is about to be posted, as a positive amount.</param>
    /// <param name="grant">Builds the grant posting for an amount.</param>
    /// <param name="correlationId">The operation's correlation key, which the audit entry carries.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CostRoomResult> MakeRoomAsync(
        ClubAccount account,
        long outflowMinor,
        Func<long, LedgerPosting> grant,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentOutOfRangeException.ThrowIfNegative(outflowMinor);

        if (outflowMinor == 0)
        {
            return new CostRoomResult(0, 0);
        }

        var headroom = account.CashMinor + account.OverdraftFloorMinor;
        var granted = 0L;

        if (outflowMinor > headroom)
        {
            granted = outflowMinor - headroom;
            _ledger.Add(account.Post(grant(granted), now));

            _audit.Record(new AuditEntry(
                FinanceAuditActions.EmergencyGrant,
                AuditActorTypes.Service,
                ActorUserId: null,
                AuditTargetTypes.Club,
                account.ClubId,
                correlationId,
                IpHash: null,
                $"Emergency grant of {granted} minor units to keep the club above its overdraft floor (FIN-16)."));

            // An operations alert, not a quiet correction: a grant means the economy is not yet balanced for this
            // club, which is exactly what an operator needs to see (FIN-16).
            LogGranted(account.ClubId, correlationId, granted);
        }

        var cashAfter = Math.Max(0, account.CashMinor - outflowMinor);
        var withdrawn = 0;

        if (account.ReservedMinor > cashAfter)
        {
            foreach (var bid in await _bids.ListLeadingByClubAsync(account.ClubId, cancellationToken))
            {
                if (account.ReservedMinor <= cashAfter)
                {
                    break;
                }

                _ledger.Add(account.Post(
                    LedgerPostings.ReservationRelease(Guid.CreateVersion7(), account.ClubId, bid.Id, bid.AmountMinor),
                    now));

                bid.Release(now);
                withdrawn++;

                _audit.Record(new AuditEntry(
                    FinanceAuditActions.BidWithdrawn,
                    AuditActorTypes.Service,
                    ActorUserId: null,
                    AuditTargetTypes.TransferBid,
                    bid.Id,
                    correlationId,
                    IpHash: null,
                    "The club needed the reserved money to pay its recurring costs (FIN-10)."));

                await NotifyAsync(account.ClubId, bid, now, cancellationToken);
            }
        }

        return new CostRoomResult(granted, withdrawn);
    }

    private async Task NotifyAsync(
        Guid clubId,
        Domain.Market.TransferBid bid,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = (await _inbox.FindClubTargetsAsync([clubId], cancellationToken))
            .FirstOrDefault(candidate => candidate.ClubId == clubId);

        if (target?.ManagerId is not { } managerId || managerId == Guid.Empty)
        {
            return;
        }

        var listing = await _listings.FindAsync(bid.ListingId, cancellationToken);

        if (listing is null)
        {
            return;
        }

        var names = await _inbox.FindPlayerNamesAsync([listing.PlayerId], cancellationToken);
        var draft = FinanceInboxTemplates.BidWithdrawnMessage(
            names.TryGetValue(listing.PlayerId, out var name) ? name : "a player",
            bid.AmountMinor,
            listing.Id);

        _inbox.Add(InboxMessage.Record(
            Guid.CreateVersion7(),
            managerId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            draft.RelatedEntityId,
            now));
    }

    [LoggerMessage(
        EventId = 3302,
        Level = LogLevel.Warning,
        Message = "OPERATIONS ALERT: club {ClubId} could not stay above its overdraft floor ({CorrelationId}) and was granted {ShortfallMinor} minor units to preserve competition integrity (FIN-16).")]
    private partial void LogGranted(Guid clubId, string correlationId, long shortfallMinor);
}
