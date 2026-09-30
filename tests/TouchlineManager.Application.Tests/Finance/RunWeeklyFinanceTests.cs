using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Finance;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Tests.Finance;

/// <summary>
/// The weekly finance settlement: what it credits, what it charges, and the grant that keeps a club paying
/// (`CON-2`, `FIN-4`, `FIN-7`, `FIN-9`, `FIN-16`).
/// </summary>
/// <remarks>
/// The posting factories and the affordability guard are pinned by their own tests. What this suite proves is
/// the orchestration around them: a solvent club is settled, a club that cannot pay is granted exactly its
/// shortfall and never ends overdrawn, and the whole run commits once.
/// </remarks>
public sealed class RunWeeklyFinanceTests
{
    private static readonly DateOnly Week = new(2026, 9, 27);

    [Fact]
    public async Task Every_club_is_credited_its_sponsorship_and_charged_its_wages_and_costs()
    {
        var clubId = Guid.CreateVersion7();
        var queries = new StubFinanceQueries(new ClubWeeklyObligations(clubId, 1, WeeklyWageMinor: 1_200_000, ContractedPlayers: 22));
        var accounts = new RecordingAccountRepository(FundedAccount(clubId, 50_000_000));
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();

        var result = await Create(queries, accounts, ledger, audit).ExecuteAsync(Week, CancellationToken.None);

        result.Clubs.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Posted.Should().Be(3, "sponsorship, wages, and the operating cost");
        result.Grants.Should().Be(0, "a funded club needs no grant");

        ledger.Entries.Select(entry => entry.Category).Should().BeEquivalentTo(
            [LedgerCategory.Sponsorship, LedgerCategory.Wages, LedgerCategory.OperatingCost]);

        var account = accounts.All.Single();
        var expected = 50_000_000
            + WorldRuleSet.WeeklySponsorshipMinorForTier(1)
            - 1_200_000
            - WorldRuleSet.WeeklyOperatingCostMinorForTier(1);

        account.CashMinor.Should().Be(expected);
        account.ReservedMinor.Should().Be(0);
        audit.Entries.Should().BeEmpty("nothing needed a repair");

        // Every line shares the week's key, so a real retry collides on the ledger's unique index rather
        // than charging the club twice (FIN-17).
        ledger.Entries.Should().OnlyContain(
            entry => entry.CorrelationId == $"weekly-run:{Week:yyyy-MM-dd}:{clubId:D}");
    }

    [Fact]
    public async Task A_club_that_cannot_pay_is_granted_the_exact_shortfall_and_the_grant_is_audited()
    {
        var clubId = Guid.CreateVersion7();
        var queries = new StubFinanceQueries(new ClubWeeklyObligations(clubId, 1, WeeklyWageMinor: 10_000_000, ContractedPlayers: 22));
        var accounts = new RecordingAccountRepository(FundedAccount(clubId, 1_000_000));
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();

        var result = await Create(queries, accounts, ledger, audit).ExecuteAsync(Week, CancellationToken.None);

        result.Grants.Should().Be(1);
        result.GrantedMinor.Should().Be(7_000_000, "the week cost 11,000,000 and the club could commit 4,000,000");

        var grant = ledger.Entries
            .Should()
            .ContainSingle(entry => entry.Category == LedgerCategory.EmergencyGrant)
            .Which;

        grant.SourceType.Should().Be(LedgerSourceType.SafetyJob);
        grant.CashDeltaMinor.Should().Be(7_000_000);

        var account = accounts.All.Single();
        account.CashMinor.Should().Be(0, "the grant covers the week exactly, never more");
        account.ReservedMinor.Should().Be(0);

        audit.Entries.Should().ContainSingle().Which.Action.Should().Be(FinanceAuditActions.EmergencyGrant);
    }

    [Fact]
    public async Task A_week_already_settled_is_skipped_rather_than_charged_again()
    {
        var clubId = Guid.CreateVersion7();
        var queries = new StubFinanceQueries(new ClubWeeklyObligations(clubId, 1, WeeklyWageMinor: 1_000_000, ContractedPlayers: 22));
        var accounts = new RecordingAccountRepository(FundedAccount(clubId, 50_000_000));
        var ledger = new RecordingLedgerRepository();

        ledger.Existing.Add($"weekly-run:{Week:yyyy-MM-dd}:{clubId:D}");

        var result = await Create(queries, accounts, ledger, new RecordingAuditWriter())
            .ExecuteAsync(Week, CancellationToken.None);

        result.Skipped.Should().Be(1);
        result.Posted.Should().Be(0);
        ledger.Entries.Should().BeEmpty("a retried week writes nothing (FIN-17)");
        accounts.All.Single().CashMinor.Should().Be(50_000_000, "a settled week moves no money");
    }

    [Fact]
    public async Task The_run_commits_once_for_the_whole_world()
    {
        var queries = new StubFinanceQueries(
            new ClubWeeklyObligations(Guid.CreateVersion7(), 1, 1_000_000, 22),
            new ClubWeeklyObligations(Guid.CreateVersion7(), 2, 500_000, 20));

        var accounts = new RecordingAccountRepository(
            FundedAccount(queries.Clubs[0].ClubId, 50_000_000),
            FundedAccount(queries.Clubs[1].ClubId, 50_000_000));

        var unitOfWork = new RecordingUnitOfWork();

        await Create(queries, accounts, new RecordingLedgerRepository(), new RecordingAuditWriter(), unitOfWork)
            .ExecuteAsync(Week, CancellationToken.None);

        unitOfWork.Saves.Should().Be(1, "a week is one transaction for every club (FIN-11)");
    }

    [Fact]
    public async Task A_world_with_no_clubs_settles_nothing()
    {
        var queries = new StubFinanceQueries();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await Create(queries, new RecordingAccountRepository(), new RecordingLedgerRepository(), new RecordingAuditWriter(), unitOfWork)
            .ExecuteAsync(Week, CancellationToken.None);

        result.Clubs.Should().Be(0);
        unitOfWork.Saves.Should().Be(0);
    }

    private static RunWeeklyFinance Create(
        StubFinanceQueries queries,
        RecordingAccountRepository accounts,
        RecordingLedgerRepository ledger,
        RecordingAuditWriter audit,
        RecordingUnitOfWork? unitOfWork = null) =>
        new(
            queries,
            accounts,
            ledger,
            audit,
            unitOfWork ?? new RecordingUnitOfWork(),
            new FixedClock(),
            NullLogger<RunWeeklyFinance>.Instance);

    private static ClubAccount FundedAccount(Guid clubId, long cashMinor)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var account = ClubAccount.Open(Guid.CreateVersion7(), clubId, now);

        account.Post(LedgerPostings.OpeningBalance(Guid.CreateVersion7(), clubId, cashMinor), now);

        return account;
    }

    private sealed class StubFinanceQueries(params ClubWeeklyObligations[] clubs) : IFinanceQueries
    {
        public ClubWeeklyObligations[] Clubs { get; } = clubs;

        public Task<IReadOnlyList<ClubRevenueBasis>> GetMatchdayRevenueBasisAsync(
            Guid matchdayId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubRevenueBasis>>([]);

        public Task<IReadOnlyList<ClubWeeklyObligations>> GetWeeklyObligationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubWeeklyObligations>>(Clubs);

        public Task<FinanceSummarySnapshot?> GetFinanceSummaryAsync(
            Guid clubId,
            CancellationToken cancellationToken) =>
            Task.FromResult<FinanceSummarySnapshot?>(null);

        public Task<FinanceLedgerPage> GetFinanceLedgerAsync(
            Guid clubId,
            long? beforeSequence,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceLedgerPage([], HasMore: false));
    }

    private sealed class RecordingAccountRepository(params ClubAccount[] accounts) : IClubAccountRepository
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

    private sealed class RecordingLedgerRepository : ILedgerRepository
    {
        public List<LedgerEntry> Entries { get; } = [];

        public HashSet<string> Existing { get; } = new(StringComparer.Ordinal);

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
            Task.FromResult<IReadOnlyList<ClubCategoryTotal>>([]);

        public Task<IReadOnlyDictionary<Guid, long>> LoadCashBalanceBeforeAsync(
            IReadOnlyCollection<Guid> clubIds,
            DateTimeOffset instant,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, long>>(new Dictionary<Guid, long>());
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;

            return Task.FromResult(0);
        }

        public Task<IDatabaseTransaction> BeginTransactionAsync(
            TransactionIsolation isolation,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The weekly run commits one unit of work.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 27, 23, 0, 0, TimeSpan.Zero);
    }
}
