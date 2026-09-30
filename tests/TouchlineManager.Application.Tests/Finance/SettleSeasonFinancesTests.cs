using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Finance;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Tests.Finance;

/// <summary>
/// The rollover's finance settlement: the final-position award and each club's season finance summary
/// (`FIN-5`, master plan §6.8).
/// </summary>
public sealed class SettleSeasonFinancesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorldId = Guid.CreateVersion7();
    private static readonly Guid SeasonId = Guid.CreateVersion7();

    [Fact]
    public async Task Every_club_receives_its_position_award_and_a_summary()
    {
        var champion = Guid.CreateVersion7();
        var bottom = Guid.CreateVersion7();

        var accounts = new RecordingAccountRepository(FundedAccount(champion, 10_000_000), FundedAccount(bottom, 2_000_000));

        var ledger = new RecordingLedgerRepository
        {
            BalancesBefore = new Dictionary<Guid, long> { [champion] = 10_000_000, [bottom] = 2_000_000 },
            Totals =
            [
                new ClubCategoryTotal(champion, LedgerCategory.GateReceipt, 3_000_000),
                new ClubCategoryTotal(champion, LedgerCategory.Wages, -1_000_000),
                new ClubCategoryTotal(bottom, LedgerCategory.Wages, -500_000),
            ],
        };

        var summaries = new RecordingSummaryRepository();

        var result = await Create(accounts, ledger, summaries).SettleAsync(
            WorldId,
            ClosingSeason(),
            [new SeasonSettlementClub(champion, 1, 1), new SeasonSettlementClub(bottom, 1, 18)],
            Now,
            CancellationToken.None);

        result.Awarded.Should().Be(2);
        result.Summaries.Should().Be(2);

        ledger.Entries.Should().HaveCount(2).And.OnlyContain(entry => entry.Category == LedgerCategory.PositionAward);

        var championAward = ledger.Entries.Single(entry =>
            entry.CorrelationId.EndsWith(champion.ToString("N"), StringComparison.Ordinal));

        championAward.CashDeltaMinor.Should().Be(WorldRuleSet.PositionAwardMinorFor(1, 1));

        // The summary's closing cash is opening plus its category lines.
        var championSummary = summaries.Summaries.Single(summary => summary.ClubId == champion);
        championSummary.OpeningCashMinor.Should().Be(10_000_000);
        championSummary.ClosingCashMinor.Should().Be(10_000_000 + 3_000_000 - 1_000_000);

        summaries.Lines.Should().HaveCount(3);
        summaries.Lines.Should().ContainSingle(line =>
            line.ClubSeasonFinanceId == championSummary.Id && line.Category == LedgerCategory.GateReceipt);
    }

    [Fact]
    public async Task A_club_with_no_ledger_history_gets_a_zero_summary()
    {
        var clubId = Guid.CreateVersion7();
        var summaries = new RecordingSummaryRepository();

        var result = await Create(
                new RecordingAccountRepository(FundedAccount(clubId, 1)),
                new RecordingLedgerRepository(),
                summaries)
            .SettleAsync(WorldId, ClosingSeason(), [new SeasonSettlementClub(clubId, 1, 9)], Now, CancellationToken.None);

        result.Awarded.Should().Be(1);
        result.Summaries.Should().Be(1);

        var summary = summaries.Summaries.Single();
        summary.OpeningCashMinor.Should().Be(0);
        summary.ClosingCashMinor.Should().Be(0);
        summaries.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_plan_settles_nothing()
    {
        var summaries = new RecordingSummaryRepository();

        var result = await Create(new RecordingAccountRepository(), new RecordingLedgerRepository(), summaries)
            .SettleAsync(WorldId, ClosingSeason(), [], Now, CancellationToken.None);

        result.Should().Be(new SeasonSettlementResult(0, 0));
        summaries.Summaries.Should().BeEmpty();
    }

    private static SettleSeasonFinances Create(
        RecordingAccountRepository accounts,
        RecordingLedgerRepository ledger,
        RecordingSummaryRepository summaries) =>
        new(accounts, ledger, summaries, NullLogger<SettleSeasonFinances>.Instance);

    private static Season ClosingSeason() =>
        Season.Create(
            SeasonId,
            WorldId,
            sequenceNumber: 1,
            gameYear: 2026,
            WorldRuleSet.Version,
            firstMatchday: new DateOnly(2026, 9, 1),
            Now);

    private static ClubAccount FundedAccount(Guid clubId, long cashMinor)
    {
        var account = ClubAccount.Open(Guid.CreateVersion7(), clubId, Now);

        account.Post(LedgerPostings.OpeningBalance(Guid.CreateVersion7(), clubId, cashMinor), Now);

        return account;
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

        public IReadOnlyList<ClubCategoryTotal> Totals { get; init; } = [];

        public IReadOnlyDictionary<Guid, long> BalancesBefore { get; init; } = new Dictionary<Guid, long>();

        public void Add(LedgerEntry entry) => Entries.Add(entry);

        public Task<LedgerEntry?> FindByIdAsync(Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.SingleOrDefault(entry => entry.Id == entryId));

        public Task<IReadOnlySet<string>> FindExistingCorrelationIdsAsync(
            IReadOnlyCollection<string> correlationIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

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

    private sealed class RecordingSummaryRepository : IClubSeasonFinanceRepository
    {
        public List<ClubSeasonFinance> Summaries { get; } = [];

        public List<ClubSeasonFinanceLine> Lines { get; } = [];

        public void Add(ClubSeasonFinance summary) => Summaries.Add(summary);

        public void AddLine(ClubSeasonFinanceLine line) => Lines.Add(line);
    }
}
