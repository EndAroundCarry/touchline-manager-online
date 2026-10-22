using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using static Tactical11Manager.Application.Tests.Finance.FinanceDoubles;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// The weekly finance settlement: what it credits, what it charges, and how a club that cannot pay is carried (`FIN-9`, `STRC-6`, `STRC-8`,
/// `STAD-7`, `FIN-16`, `DEBT-1`).
/// </summary>
/// <remarks>
/// The posting factories and the affordability guard are pinned by their own tests. What this suite proves is the orchestration around them:
/// the week's costs are met out of the overdraft, a club past its floor is granted exactly the difference and never ends below it, and the
/// whole run commits once. The wages and the sponsor are not here: they are paid on every matchday.
/// </remarks>
public sealed class RunWeeklyFinanceTests
{
    private static readonly DateOnly Week = new(2026, 9, 27);

    private static readonly StadiumSeats Ground = new(3_000, 1_000, 900, 100);

    [Fact]
    public async Task Every_club_is_charged_its_operating_cost_its_ground_and_its_buildings_and_no_wages()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(Obligations(clubId, 1), Funded(clubId, 500_000_000));

        var result = await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        result.Clubs.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Posted.Should().Be(3, "the operating cost, the ground's upkeep and the buildings' upkeep");
        result.Grants.Should().Be(0, "a funded club needs no grant");

        run.Ledger.Entries.Select(entry => entry.Category).Should().BeEquivalentTo(
            [LedgerCategory.OperatingCost, LedgerCategory.GroundUpkeep, LedgerCategory.StructureUpkeep],
            "wages and the sponsor are paid on every matchday, not in the weekly run");

        var expected = 500_000_000
            - EconomyRuleSet.PerWeeklyRun(EconomyRuleSet.OperatingCostSeasonMinorFor(1))
            - EconomyRuleSet.PerWeeklyRun(StadiumRuleSet.GroundUpkeepSeasonMinorFor(Ground, 1))
            - (2 * StructureRuleSet.UpkeepMinorFor(1, 1));

        run.Accounts.All.Single().CashMinor.Should().Be(expected);
        run.Accounts.All.Single().ReservedMinor.Should().Be(0);
        run.Audit.Entries.Should().BeEmpty("nothing needed a repair");

        // Every line shares the week's key, so a real retry collides on the ledger's unique index rather
        // than charging the club twice (FIN-17).
        run.Ledger.Entries.Should().OnlyContain(
            entry => entry.CorrelationId == $"weekly-run:{Week:yyyy-MM-dd}:{clubId:D}");
    }

    [Fact]
    public async Task A_club_with_no_overdraft_that_cannot_pay_is_granted_the_exact_shortfall_and_the_grant_is_audited()
    {
        var clubId = Guid.CreateVersion7();
        const long cash = 1_000;
        var run = Arrange(Obligations(clubId, 1), Funded(clubId, cash));

        var costs = EconomyRuleSet.PerWeeklyRun(EconomyRuleSet.OperatingCostSeasonMinorFor(1))
            + EconomyRuleSet.PerWeeklyRun(StadiumRuleSet.GroundUpkeepSeasonMinorFor(Ground, 1))
            + (2 * StructureRuleSet.UpkeepMinorFor(1, 1));

        var result = await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        result.Grants.Should().Be(1);
        result.GrantedMinor.Should().Be(costs - cash, "the costs less the cash in hand, with no overdraft to borrow from");

        var grant = run.Ledger.Entries
            .Should()
            .ContainSingle(entry => entry.Category == LedgerCategory.EmergencyGrant)
            .Which;

        grant.SourceType.Should().Be(LedgerSourceType.SafetyJob);
        grant.CashDeltaMinor.Should().Be(costs - cash);

        run.Accounts.All.Single().CashMinor.Should().Be(0, "the grant covers the week exactly, never more");
        run.Audit.Entries.Should().ContainSingle(entry => entry.Action == FinanceAuditActions.EmergencyGrant);
    }

    [Fact]
    public async Task A_club_with_overdraft_room_goes_into_the_red_without_a_grant_and_is_moved_and_told()
    {
        var clubId = Guid.CreateVersion7();
        var managerId = Guid.CreateVersion7();
        var costs = EconomyRuleSet.PerWeeklyRun(EconomyRuleSet.OperatingCostSeasonMinorFor(1))
            + EconomyRuleSet.PerWeeklyRun(StadiumRuleSet.GroundUpkeepSeasonMinorFor(Ground, 1))
            + (2 * StructureRuleSet.UpkeepMinorFor(1, 1));

        // A limit of 10 weeks of costs: the week fits inside it, with room to spare below the floor of twice that.
        var limit = costs * 10;
        var run = Arrange(Obligations(clubId, 1), Funded(clubId, cashMinor: 1_000, overdraftLimitMinor: limit));

        run.Inbox.Targets.Add(new ClubInboxTarget(clubId, managerId, "Test FC"));

        var result = await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        result.Grants.Should().Be(0, "the overdraft pays for the week");

        var account = run.Accounts.All.Single();

        account.CashMinor.Should().Be(1_000 - costs);
        account.FinancialState.Should().Be(FinancialStateKind.InTheRed);
        run.Inbox.Messages.Should().ContainSingle(message => message.TemplateKey == FinanceInboxTemplates.InTheRed);
    }

    [Fact]
    public async Task A_leading_bid_is_withdrawn_when_the_week_would_leave_it_reserved_above_the_cash()
    {
        var clubId = Guid.CreateVersion7();
        var costs = EconomyRuleSet.PerWeeklyRun(EconomyRuleSet.OperatingCostSeasonMinorFor(1))
            + EconomyRuleSet.PerWeeklyRun(StadiumRuleSet.GroundUpkeepSeasonMinorFor(Ground, 1))
            + (2 * StructureRuleSet.UpkeepMinorFor(1, 1));

        // The cash would cover the week, but all of it is promised to a bid.
        var account = Funded(clubId, cashMinor: costs * 2, overdraftLimitMinor: costs * 10);
        var bid = TransferBid.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), clubId, costs * 2, "key", null, Now);

        account.Post(LedgerPostings.BidReservation(Guid.CreateVersion7(), clubId, bid.Id, costs * 2), Now);

        var run = Arrange(Obligations(clubId, 1), account, new StubBidRepository(bid));

        await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        bid.Status.Should().Be(BidStatus.Released, "the club needed the money to pay its week");
        account.ReservedMinor.Should().Be(0);
        account.CashMinor.Should().Be(costs, "the cash less the week's costs");
        run.Ledger.Entries.Should().Contain(entry => entry.Category == LedgerCategory.ReservationRelease);
        run.Audit.Entries.Should().ContainSingle(entry => entry.Action == FinanceAuditActions.BidWithdrawn);
    }

    [Fact]
    public async Task A_week_already_settled_is_skipped_rather_than_charged_again()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(Obligations(clubId, 1), Funded(clubId, 500_000_000));

        run.Ledger.Existing.Add($"weekly-run:{Week:yyyy-MM-dd}:{clubId:D}");

        var result = await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        result.Skipped.Should().Be(1);
        result.Posted.Should().Be(0);
        run.Ledger.Entries.Should().BeEmpty("a retried week writes nothing (FIN-17)");
        run.Accounts.All.Single().CashMinor.Should().Be(500_000_000, "a settled week moves no money");
    }

    [Fact]
    public async Task The_run_commits_once_for_the_whole_world()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var run = Arrange(
            [Obligations(first, 1), Obligations(second, 2)],
            [Funded(first, 500_000_000), Funded(second, 500_000_000)]);

        await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        run.UnitOfWork.Saves.Should().Be(1, "a week is one transaction for every club (FIN-11)");
    }

    [Fact]
    public async Task A_world_with_no_clubs_settles_nothing()
    {
        var run = Arrange([], []);

        var result = await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        result.Clubs.Should().Be(0);
        run.UnitOfWork.Saves.Should().Be(0);
    }

    [Fact]
    public async Task The_merchandise_shop_credits_the_fans_times_the_spend_lifted_by_their_mood()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(
            Obligations(clubId, 1, fans: 10_000, moodBp: 5_000, merchandiseLevel: 3),
            Funded(clubId, 500_000_000));

        await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        var expected = StructureRuleSet.MerchTakingsMinorFor(3, 10_000, FanMoods.WordFor(5_000), tier: 1);

        expected.Should().BeGreaterThan(10_000 * StructureRuleSet.MerchSpendMinorFor(3, 1), "calm fans lift the takings");

        var entry = run.Ledger.Entries.Should().ContainSingle(item => item.Category == LedgerCategory.Merchandise).Which;

        entry.CashDeltaMinor.Should().Be(expected);
        entry.CorrelationId.Should().Be($"weekly-run:{Week:yyyy-MM-dd}:{clubId:D}");

        var row = run.Structures.Takings.Should().ContainSingle(item => item.RowKind == StructureTakingsKind.WeekSale).Which;

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
        var run = Arrange(
            Obligations(clubId, 2, snackBarLevel: 4, merchandiseLevel: 2),
            Funded(clubId, 500_000_000));

        await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        var snack = StructureRuleSet.UpkeepMinorFor(4, 2);
        var merch = StructureRuleSet.UpkeepMinorFor(2, 2);

        var entry = run.Ledger.Entries.Should().ContainSingle(item => item.Category == LedgerCategory.StructureUpkeep).Which;

        entry.CashDeltaMinor.Should().Be(-(snack + merch));

        var rows = run.Structures.Takings.Where(item => item.RowKind == StructureTakingsKind.Upkeep).ToList();

        rows.Should().HaveCount(2);
        rows.Single(item => item.Kind == StructureKind.SnackBar).AmountMinor.Should().Be(-snack);
        rows.Single(item => item.Kind == StructureKind.Merchandise).AmountMinor.Should().Be(-merch);
        rows.Should().OnlyContain(item => item.LedgerEntryId == entry.Id && item.WeekOf == Week);
    }

    [Fact]
    public async Task A_club_with_no_fans_sells_no_merchandise_and_writes_no_empty_entry()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(Obligations(clubId, 1, fans: 0), Funded(clubId, 500_000_000));

        await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        run.Ledger.Entries.Should().NotContain(item => item.Category == LedgerCategory.Merchandise);
        run.Structures.Takings.Should().NotContain(item => item.RowKind == StructureTakingsKind.WeekSale);
    }

    [Fact]
    public async Task A_settled_week_writes_no_takings_rows()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(Obligations(clubId, 1, fans: 10_000, moodBp: 5_000), Funded(clubId, 500_000_000));

        run.Ledger.Existing.Add($"weekly-run:{Week:yyyy-MM-dd}:{clubId:D}");

        await run.Subject.ExecuteAsync(Week, CancellationToken.None);

        run.Structures.Takings.Should().BeEmpty("a retried week must not write its history twice (FIN-17)");
    }

    private static ClubWeeklyObligations Obligations(
        Guid clubId,
        int tier,
        int fans = 0,
        int moodBp = 5_000,
        int snackBarLevel = 1,
        int merchandiseLevel = 1) =>
        new(clubId, tier, Ground, fans, moodBp, snackBarLevel, merchandiseLevel);

    private static Run Arrange(ClubWeeklyObligations club, ClubAccount account, StubBidRepository? bids = null) =>
        Arrange([club], [account], bids);

    private static Run Arrange(
        IReadOnlyList<ClubWeeklyObligations> clubs,
        IReadOnlyList<ClubAccount> accountList,
        StubBidRepository? bids = null)
    {
        var queries = new StubFinanceQueries();

        queries.Weekly.AddRange(clubs);

        var accounts = new RecordingAccountRepository([.. accountList]);
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();
        var inbox = new RecordingInboxRepository();
        var structures = new RecordingStructureRepository();
        var unitOfWork = new RecordingUnitOfWork();

        var subject = new RunWeeklyFinance(
            queries,
            accounts,
            ledger,
            structures,
            Room(ledger, audit, bids, inbox),
            States(accounts, queries, inbox, audit: audit),
            unitOfWork,
            new FixedClock(),
            NullLogger<RunWeeklyFinance>.Instance);

        return new Run(subject, accounts, ledger, audit, inbox, structures, unitOfWork);
    }

    private sealed record Run(
        RunWeeklyFinance Subject,
        RecordingAccountRepository Accounts,
        RecordingLedgerRepository Ledger,
        RecordingAuditWriter Audit,
        RecordingInboxRepository Inbox,
        RecordingStructureRepository Structures,
        RecordingUnitOfWork UnitOfWork);

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
