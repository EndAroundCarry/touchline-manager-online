using Microsoft.Extensions.Logging.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// The in-memory collaborators the finance use cases are exercised with: they record what a use case stages and answer the reads it makes,
/// so a suite can prove the wiring without a database.
/// </summary>
internal static class FinanceDoubles
{
    public static readonly DateTimeOffset Now = new(2026, 10, 18, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Opens an account funded with a cash balance and, optionally, an overdraft limit.</summary>
    public static ClubAccount Funded(Guid clubId, long cashMinor, long overdraftLimitMinor = 0)
    {
        var account = ClubAccount.Open(Guid.CreateVersion7(), clubId, Now);

        if (cashMinor > 0)
        {
            account.Post(LedgerPostings.OpeningBalance(Guid.CreateVersion7(), clubId, cashMinor), Now);
        }

        if (overdraftLimitMinor > 0)
        {
            account.SetOverdraft(overdraftLimitMinor, Now);
        }

        return account;
    }

    /// <summary>Builds the collaborator that makes room for recurring costs.</summary>
    public static RecurringCostRoom Room(
        RecordingLedgerRepository ledger,
        RecordingAuditWriter audit,
        StubBidRepository? bids = null,
        RecordingInboxRepository? inbox = null) =>
        new(
            bids ?? new StubBidRepository(),
            new StubListingRepository(),
            ledger,
            inbox ?? new RecordingInboxRepository(),
            audit,
            NullLogger<RecurringCostRoom>.Instance);

    /// <summary>Builds the collaborator that reads clubs' money.</summary>
    public static ClubFinancialStates States(
        RecordingAccountRepository accounts,
        IFinanceQueries queries,
        RecordingInboxRepository? inbox = null,
        RecordingListingWriter? listings = null,
        RecordingAuditWriter? audit = null,
        StubTenureRepository? tenures = null,
        RecordingOutboxWriter? outbox = null,
        RecordingMetrics? metrics = null) =>
        new(
            accounts,
            queries,
            inbox ?? new RecordingInboxRepository(),
            listings ?? new RecordingListingWriter(),
            tenures ?? new StubTenureRepository(),
            outbox ?? new RecordingOutboxWriter(),
            audit ?? new RecordingAuditWriter(),
            metrics ?? new RecordingMetrics(),
            NullLogger<ClubFinancialStates>.Instance);

    public sealed class RecordingAccountRepository(params ClubAccount[] accounts) : IClubAccountRepository
    {
        public IReadOnlyList<ClubAccount> All { get; } = accounts;

        public void Add(ClubAccount account)
        {
        }

        public Task<ClubAccount?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(account => account.ClubId == clubId));

        public Task<IReadOnlyList<ClubAccount>> LoadAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubAccount>>([.. All.Where(account => clubIds.Contains(account.ClubId))]);

        public Task<IReadOnlyList<ClubAccount>> LoadAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(All);
    }

    public sealed class RecordingLedgerRepository : ILedgerRepository
    {
        public List<LedgerEntry> Entries { get; } = [];

        public HashSet<string> Existing { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<ClubCategoryTotal> Totals { get; init; } = [];

        public IReadOnlyDictionary<Guid, long> BalancesBefore { get; init; } = new Dictionary<Guid, long>();

        public void Add(LedgerEntry entry) => Entries.Add(entry);

        public Task<LedgerEntry?> FindByIdAsync(Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry => entry.Id == entryId));

        public Task<IReadOnlySet<string>> FindExistingCorrelationIdsAsync(
            IReadOnlyCollection<string> correlationIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(
                Existing.Where(correlationIds.Contains).ToHashSet(StringComparer.Ordinal));

        public Task<IReadOnlyList<ClubCategoryTotal>> LoadCategoryTotalsAsync(
            IReadOnlyCollection<Guid> clubIds,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubCategoryTotal>>([.. Totals.Where(total => clubIds.Contains(total.ClubId))]);

        public Task<IReadOnlyDictionary<Guid, long>> LoadCashBalanceBeforeAsync(
            IReadOnlyCollection<Guid> clubIds,
            DateTimeOffset instant,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, long>>(
                BalancesBefore.Where(pair => clubIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    public sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    public sealed class RecordingInboxRepository : IInboxRepository
    {
        public List<InboxMessage> Messages { get; } = [];

        public List<ClubInboxTarget> Targets { get; } = [];

        public Dictionary<Guid, string> PlayerNames { get; } = [];

        public void Add(InboxMessage message) => Messages.Add(message);

        public Task<InboxMessage?> FindAsync(Guid managerId, Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult<InboxMessage?>(null);

        public Task<IReadOnlyList<InboxMessage>> LoadUnreadAsync(Guid managerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InboxMessage>>([]);

        public Task<IReadOnlyList<ClubInboxTarget>> FindClubTargetsAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubInboxTarget>>([.. Targets.Where(target => clubIds.Contains(target.ClubId))]);

        public Task<IReadOnlyDictionary<Guid, string>> FindPlayerNamesAsync(
            IReadOnlyCollection<Guid> playerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                PlayerNames.Where(pair => playerIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));

        public Task<IReadOnlyDictionary<Guid, string>> FindManagerEmailsAsync(
            IReadOnlyCollection<Guid> managerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    public sealed class StubBidRepository(params TransferBid[] leading) : IBidRepository
    {
        public void Add(TransferBid bid)
        {
        }

        public Task<TransferBid?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult<TransferBid?>(null);

        public Task<TransferBid?> FindActiveBidAsync(Guid listingId, Guid bidderClubId, CancellationToken cancellationToken) =>
            Task.FromResult<TransferBid?>(null);

        public Task<IReadOnlyList<TransferBid>> ListLeadingByClubAsync(Guid bidderClubId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransferBid>>(
                [.. leading.Where(bid => bid.BidderClubId == bidderClubId && bid.IsLeading).OrderByDescending(bid => bid.AmountMinor)]);

        public Task<TransferBid?> FindLeadingBidAsync(Guid listingId, CancellationToken cancellationToken) =>
            Task.FromResult(leading.FirstOrDefault(bid => bid.ListingId == listingId && bid.IsLeading));

        public Task<IReadOnlyList<TransferBid>> ListForListingAsync(Guid listingId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransferBid>>([.. leading.Where(bid => bid.ListingId == listingId)]);
    }

    public sealed class StubListingRepository(params TransferListing[] listings) : IListingRepository
    {
        public void Add(TransferListing listing)
        {
        }

        public Task<TransferListing?> FindAsync(Guid listingId, CancellationToken cancellationToken) =>
            Task.FromResult(listings.FirstOrDefault(listing => listing.Id == listingId));

        public Task<TransferListing?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult<TransferListing?>(null);

        public Task<TransferListing?> FindForResolutionAsync(Guid listingId, CancellationToken cancellationToken) =>
            Task.FromResult(listings.FirstOrDefault(listing => listing.Id == listingId));

        public Task<IReadOnlyList<TransferListing>> ListDueAsync(DateTimeOffset dueAt, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransferListing>>([]);
    }

    public sealed class RecordingListingWriter : IListingWriter
    {
        public List<Call> Calls { get; } = [];

        public Task<ListingWriteResult> ListAsync(
            Guid clubId,
            MarketActor actor,
            Guid playerId,
            long minimumFeeMinor,
            int seasons,
            string? idempotencyKey,
            CancellationToken cancellationToken,
            bool listedByBoard = false)
        {
            Calls.Add(new Call(clubId, playerId, minimumFeeMinor, seasons, idempotencyKey, listedByBoard));

            return Task.FromResult(new ListingWriteResult(MarketOutcome.Found, Guid.CreateVersion7(), Created: true));
        }

        public sealed record Call(Guid ClubId, Guid PlayerId, long MinimumFeeMinor, int Seasons, string? Key, bool ListedByBoard);
    }

    public sealed class StubTenureRepository(params OpenTenureRow[] rows) : IClubTenureRepository
    {
        public Task<ClubTenure?> FindOpenByManagerAsync(Guid managerId, CancellationToken cancellationToken) =>
            Task.FromResult<ClubTenure?>(null);

        public Task<ClubTenure?> FindOpenByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult<ClubTenure?>(null);

        public Task<IReadOnlyList<ClubTenure>> ListByManagerAsync(Guid managerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubTenure>>([]);

        public Task<ClubTenure?> FindByTakeoverKeyAsync(string takeoverIdempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult<ClubTenure?>(null);

        public void Add(ClubTenure tenure)
        {
        }

        public Task<IReadOnlyList<OpenTenureRow>> ListOpenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OpenTenureRow>>([.. rows]);

        public Task<IReadOnlyCollection<Guid>> ListOpenClubIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Guid>>([]);
    }

    public sealed class RecordingOutboxWriter : IOutboxWriter
    {
        public List<OutboxDraft> Drafts { get; } = [];

        public void Enqueue(OutboxDraft draft) => Drafts.Add(draft);
    }

    public sealed class RecordingMetrics : IOperationalMetrics
    {
        public List<string> Closed { get; } = [];

        public void OnboardingStep(OnboardingStep onboardingStep)
        {
        }

        public void TenureBecameInactive()
        {
        }

        public void TenureClosed(string reason) => Closed.Add(reason);
    }

    public sealed class StubClubRepository(params Club[] clubs) : IClubRepository
    {
        public Task<Club?> FindAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult(clubs.FirstOrDefault(club => club.Id == clubId));

        public Task<IReadOnlyList<Club>> LoadAsync(IReadOnlyCollection<Guid> clubIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Club>>([.. clubs.Where(club => clubIds.Contains(club.Id))]);

        public void Add(Club club)
        {
        }
    }

    public sealed class RecordingSponsorRepository(params ClubSponsorDeal[] deals) : ISponsorRepository
    {
        public List<ClubStandingEvent> Steps { get; } = [];

        public Task<IReadOnlyList<ClubSponsorDeal>> LoadDealsAsync(
            Guid seasonId,
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubSponsorDeal>>(
                [.. deals.Where(deal => deal.SeasonId == seasonId && clubIds.Contains(deal.ClubId))]);

        public void AddStandingEvent(ClubStandingEvent standingEvent) => Steps.Add(standingEvent);
    }

    /// <summary>A configurable read double: set what each read should answer.</summary>
    public sealed class StubFinanceQueries : IFinanceQueries
    {
        public List<ClubRevenueBasis> Revenue { get; } = [];

        public List<ClubWeeklyObligations> Weekly { get; } = [];

        public List<ClubFinanceBasis> Basis { get; } = [];

        public List<AdministrationPlayer> Squad { get; } = [];

        public int Ordinal { get; set; }

        public Task<IReadOnlyList<ClubRevenueBasis>> GetMatchdayRevenueBasisAsync(
            Guid matchdayId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubRevenueBasis>>([.. Revenue]);

        public Task<IReadOnlyList<ClubWeeklyObligations>> GetWeeklyObligationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubWeeklyObligations>>([.. Weekly]);

        public Task<IReadOnlyList<ClubFinanceBasis>> GetClubFinanceBasisAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubFinanceBasis>>([.. Basis.Where(row => clubIds.Contains(row.ClubId))]);

        public Task<IReadOnlyList<AdministrationPlayer>> GetAdministrationSquadAsync(
            Guid clubId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdministrationPlayer>>([.. Squad]);

        public Task<int> GetSettledMatchdayOrdinalAsync(CancellationToken cancellationToken) => Task.FromResult(Ordinal);

        public Task<FinanceSummarySnapshot?> GetFinanceSummaryAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult<FinanceSummarySnapshot?>(null);

        public Task<FinanceLedgerPage> GetFinanceLedgerAsync(
            Guid clubId,
            long? beforeSequence,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceLedgerPage([], HasMore: false));
    }
}
