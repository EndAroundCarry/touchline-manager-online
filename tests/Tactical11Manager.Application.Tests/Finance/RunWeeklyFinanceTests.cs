using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Finance;

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
        var queries = new StubFinanceQueries(Obligations(clubId, 1, wage: 1_200_000, players: 22));
        var accounts = new RecordingAccountRepository(FundedAccount(clubId, 50_000_000));
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();

        var result = await Create(queries, accounts, ledger, audit).ExecuteAsync(Week, CancellationToken.None);

        result.Clubs.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Posted.Should().Be(4, "sponsorship, wages, the operating cost, and the buildings' upkeep");
        result.Grants.Should().Be(0, "a funded club needs no grant");

        ledger.Entries.Select(entry => entry.Category).Should().BeEquivalentTo(
            [LedgerCategory.Sponsorship, LedgerCategory.Wages, LedgerCategory.OperatingCost, LedgerCategory.StructureUpkeep]);

        var account = accounts.All.Single();
        var expected = 50_000_000
            + WorldRuleSet.WeeklySponsorshipMinorForTier(1)
            - 1_200_000
            - WorldRuleSet.WeeklyOperatingCostMinorForTier(1)
            - 2 * StructureRuleSet.UpkeepMinorFor(1, 1);

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
        var queries = new StubFinanceQueries(Obligations(clubId, 1, wage: 10_000_000, players: 22));
        var accounts = new RecordingAccountRepository(FundedAccount(clubId, 1_000_000));
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();

        var result = await Create(queries, accounts, ledger, audit).ExecuteAsync(Week, CancellationToken.None);

        result.Grants.Should().Be(1);
        result.GrantedMinor.Should().Be(
            7_000_000 + 2 * StructureRuleSet.UpkeepMinorFor(1, 1),
            "the week cost the wages, the operating cost and both buildings' upkeep, and the club could commit 4,000,000");

        var grant = ledger.Entries
            .Should()
            .ContainSingle(entry => entry.Category == LedgerCategory.EmergencyGrant)
            .Which;

        grant.SourceType.Should().Be(LedgerSourceType.SafetyJob);
        grant.CashDeltaMinor.Should().Be(7_000_000 + 2 * StructureRuleSet.UpkeepMinorFor(1, 1));

        var account = accounts.All.Single();
        account.CashMinor.Should().Be(0, "the grant covers the week exactly, never more");
        account.ReservedMinor.Should().Be(0);

        audit.Entries.Should().ContainSingle().Which.Action.Should().Be(FinanceAuditActions.EmergencyGrant);
    }

    [Fact]
    public async Task A_week_already_settled_is_skipped_rather_than_charged_again()
    {
        var clubId = Guid.CreateVersion7();
        var queries = new StubFinanceQueries(Obligations(clubId, 1, wage: 1_000_000, players: 22));
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
            Obligations(Guid.CreateVersion7(), 1, wage: 1_000_000, players: 22),
            Obligations(Guid.CreateVersion7(), 2, wage: 500_000, players: 20));

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

    [Fact]
    public async Task The_merchandise_shop_credits_the_fans_times_the_spend_lifted_by_their_mood()
    {
        var clubId = Guid.CreateVersion7();
        var queries = new StubFinanceQueries(
            Obligations(clubId, 1, wage: 0, players: 0, fans: 10_000, moodBp: 5_000, merchandiseLevel: 3));

        var accounts = new RecordingAccountRepository(FundedAccount(clubId, 50_000_000));
        var ledger = new RecordingLedgerRepository();
        var structures = new RecordingStructureRepository();

        await Create(queries, accounts, ledger, new RecordingAuditWriter(), structures: structures)
            .ExecuteAsync(Week, CancellationToken.None);

        var expected = StructureRuleSet.MerchTakingsMinorFor(3, 10_000, FanMoods.WordFor(5_000));

        expected.Should().BeGreaterThan(10_000 * StructureRuleSet.MerchSpendMinor(3), "calm fans lift the takings");

        var entry = ledger.Entries.Should().ContainSingle(item => item.Category == LedgerCategory.Merchandise).Which;

        entry.CashDeltaMinor.Should().Be(expected);
        entry.CorrelationId.Should().Be($"weekly-run:{Week:yyyy-MM-dd}:{clubId:D}");

        var row = structures.Takings.Should().ContainSingle(item => item.RowKind == StructureTakingsKind.WeekSale).Which;

        row.Kind.Should().Be(StructureKind.Merchandise);
        row.Level.Should().Be(3);
        row.Customers.Should().Be(10_000);
        row.AmountMinor.Should().Be(expected);
        row.WeekOf.Should().Be(Week);
        row.LedgerEntryId.Should().Be(entry.Id);
    }

    [Fact]
    public async Task One_combined_upkeep_entry_is_booked_and_each_building_gets_its_own_takings_row()
    {
        var clubId = Guid.CreateVersion7();
        var queries = new StubFinanceQueries(
            Obligations(clubId, 2, wage: 0, players: 0, snackBarLevel: 4, merchandiseLevel: 2));

        var accounts = new RecordingAccountRepository(FundedAccount(clubId, 50_000_000));
        var ledger = new RecordingLedgerRepository();
        var structures = new RecordingStructureRepository();

        await Create(queries, accounts, ledger, new RecordingAuditWriter(), structures: structures)
            .ExecuteAsync(Week, CancellationToken.None);

        var snack = StructureRuleSet.UpkeepMinorFor(4, 2);
        var merch = StructureRuleSet.UpkeepMinorFor(2, 2);

        var entry = ledger.Entries.Should().ContainSingle(item => item.Category == LedgerCategory.StructureUpkeep).Which;

        entry.CashDeltaMinor.Should().Be(-(snack + merch));

        var rows = structures.Takings.Where(item => item.RowKind == StructureTakingsKind.Upkeep).ToList();

        rows.Should().HaveCount(2);
        rows.Single(item => item.Kind == StructureKind.SnackBar).AmountMinor.Should().Be(-snack);
        rows.Single(item => item.Kind == StructureKind.Merchandise).AmountMinor.Should().Be(-merch);
        rows.Should().OnlyContain(item => item.LedgerEntryId == entry.Id && item.WeekOf == Week);
    }

    [Fact]
    public async Task A_club_with_no_fans_sells_no_merchandise_and_writes_no_empty_entry()
    {
        var clubId = Guid.CreateVersion7();
        var queries = new StubFinanceQueries(Obligations(clubId, 1, wage: 0, players: 0, fans: 0));
        var ledger = new RecordingLedgerRepository();
        var structures = new RecordingStructureRepository();

        await Create(
                queries,
                new RecordingAccountRepository(FundedAccount(clubId, 50_000_000)),
                ledger,
                new RecordingAuditWriter(),
                structures: structures)
            .ExecuteAsync(Week, CancellationToken.None);

        ledger.Entries.Should().NotContain(item => item.Category == LedgerCategory.Merchandise);
        structures.Takings.Should().NotContain(item => item.RowKind == StructureTakingsKind.WeekSale);
    }

    [Fact]
    public async Task The_upkeep_counts_toward_the_shortfall_that_triggers_the_emergency_grant()
    {
        var clubId = Guid.CreateVersion7();
        var upkeep = 2 * StructureRuleSet.UpkeepMinorFor(1, 1);

        // The wages are set so the sponsorship alone covers the week before the upkeep, and the club holds one
        // unit of cash: a grant can only be the upkeep tipping the week over.
        var wage = WorldRuleSet.WeeklySponsorshipMinorForTier(1) - WorldRuleSet.WeeklyOperatingCostMinorForTier(1);
        var queries = new StubFinanceQueries(Obligations(clubId, 1, wage: wage, players: 22));
        var accounts = new RecordingAccountRepository(FundedAccount(clubId, 1));
        var ledger = new RecordingLedgerRepository();

        var result = await Create(queries, accounts, ledger, new RecordingAuditWriter())
            .ExecuteAsync(Week, CancellationToken.None);

        result.Grants.Should().Be(1, "the buildings' upkeep is owed like the wages");
        result.GrantedMinor.Should().Be(upkeep - 1, "the grant is exactly what the upkeep needs beyond the cash in hand");
        accounts.All.Single().CashMinor.Should().Be(0, "the grant covers the week exactly, never more");
    }

    [Fact]
    public async Task A_settled_week_writes_no_takings_rows()
    {
        var clubId = Guid.CreateVersion7();
        var queries = new StubFinanceQueries(Obligations(clubId, 1, wage: 0, players: 0, fans: 10_000, moodBp: 5_000));
        var ledger = new RecordingLedgerRepository();
        var structures = new RecordingStructureRepository();

        ledger.Existing.Add($"weekly-run:{Week:yyyy-MM-dd}:{clubId:D}");

        await Create(
                queries,
                new RecordingAccountRepository(FundedAccount(clubId, 50_000_000)),
                ledger,
                new RecordingAuditWriter(),
                structures: structures)
            .ExecuteAsync(Week, CancellationToken.None);

        structures.Takings.Should().BeEmpty("a retried week must not write its history twice (FIN-17)");
    }

    private static ClubWeeklyObligations Obligations(
        Guid clubId,
        int tier,
        long wage,
        int players,
        int fans = 0,
        int moodBp = 5_000,
        int snackBarLevel = 1,
        int merchandiseLevel = 1) =>
        new(clubId, tier, wage, players, fans, moodBp, snackBarLevel, merchandiseLevel);

    private static RunWeeklyFinance Create(
        StubFinanceQueries queries,
        RecordingAccountRepository accounts,
        RecordingLedgerRepository ledger,
        RecordingAuditWriter audit,
        RecordingUnitOfWork? unitOfWork = null,
        RecordingStructureRepository? structures = null) =>
        new(
            queries,
            accounts,
            ledger,
            structures ?? new RecordingStructureRepository(),
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

    private sealed class RecordingStructureRepository : IStructureRepository
    {
        public List<ClubStructureTakings> Takings { get; } = [];

        public void Add(ClubStructure building)
        {
        }

        public void AddTakings(ClubStructureTakings takings) => Takings.Add(takings);

        public Task<ClubStructure?> FindAsync(Guid buildingId, CancellationToken cancellationToken) =>
            Task.FromResult<ClubStructure?>(null);

        public Task<ClubStructure?> FindByClubAsync(Guid clubId, StructureKind kind, CancellationToken cancellationToken) =>
            Task.FromResult<ClubStructure?>(null);

        public Task<IReadOnlyList<ClubStructure>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubStructure>>([]);
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
