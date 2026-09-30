using FluentAssertions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Finance;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Tests.Finance;

/// <summary>
/// The operator's compensating finance entry: an append-only correction that names the line it fixes
/// (master plan §10.8, §13, `FIN-12`, `F-46`, ADR-0045).
/// </summary>
public sealed class PostCompensatingEntryTests
{
    private static readonly Guid OperatorId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_repair_posts_a_compensating_entry_that_names_the_line_it_corrects_and_is_audited()
    {
        var (account, original) = AccountWithOpening(1_000);
        var ledger = new RecordingLedgerRepository { Original = original };
        var audit = new RecordingAuditWriter();

        var result = await Create(account, ledger, audit).ExecuteAsync(
            account.ClubId,
            cashDeltaMinor: -250,
            original.Id,
            "the gate was double counted",
            "repair-1",
            CancellationToken.None);

        result.Outcome.Should().Be(PostCompensatingEntryOutcome.Applied);
        result.EntryId.Should().NotBeNull();
        result.ResultingCashMinor.Should().Be(750);
        result.ReversesEntryId.Should().Be(original.Id);

        var entry = ledger.Entries.Should().ContainSingle().Subject;

        entry.Category.Should().Be(LedgerCategory.Compensation);
        entry.SourceType.Should().Be(LedgerSourceType.AdminRepair);
        entry.CashDeltaMinor.Should().Be(-250);
        entry.ResultingCashMinor.Should().Be(750);
        entry.CorrelationId.Should().Be("repair-1", "the operator's key is the entry's correlation key (FIN-17)");
        entry.ReversesEntryId.Should().Be(original.Id);

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].Action.Should().Be(FinanceAuditActions.CompensatingEntry);
        audit.Entries[0].ActorType.Should().Be(AuditActorTypes.User);
        audit.Entries[0].ActorUserId.Should().Be(OperatorId);
        audit.Entries[0].TargetType.Should().Be(AuditTargetTypes.LedgerEntry);
        audit.Entries[0].TargetId.Should().Be(entry.Id);
        audit.Entries[0].CorrelationId.Should().Be("repair-1");
        audit.Entries[0].Reason.Should().Be("the gate was double counted");
    }

    [Fact]
    public async Task A_repair_may_name_no_entry()
    {
        var (account, _) = AccountWithOpening(1_000);
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();

        var result = await Create(account, ledger, audit).ExecuteAsync(
            account.ClubId,
            cashDeltaMinor: 500,
            reversesEntryId: null,
            "a goodwill credit",
            "repair-2",
            CancellationToken.None);

        result.Outcome.Should().Be(PostCompensatingEntryOutcome.Applied);
        ledger.Entries.Should().ContainSingle().Which.ReversesEntryId.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_club_has_no_ledger_to_correct()
    {
        var (account, _) = AccountWithOpening(1_000);
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();

        var result = await Create(account, ledger, audit).ExecuteAsync(
            Guid.CreateVersion7(),
            cashDeltaMinor: 100,
            reversesEntryId: null,
            "no such club",
            "repair-3",
            CancellationToken.None);

        result.Outcome.Should().Be(PostCompensatingEntryOutcome.ClubNotFound);
        ledger.Entries.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task An_entry_that_belongs_to_another_club_is_not_found()
    {
        var (account, _) = AccountWithOpening(1_000);
        var otherClubEntry = OriginalFor(Guid.CreateVersion7());
        var ledger = new RecordingLedgerRepository { Original = otherClubEntry };
        var audit = new RecordingAuditWriter();

        var result = await Create(account, ledger, audit).ExecuteAsync(
            account.ClubId,
            cashDeltaMinor: 100,
            otherClubEntry.Id,
            "wrong club",
            "repair-4",
            CancellationToken.None);

        result.Outcome.Should().Be(PostCompensatingEntryOutcome.EntryNotFound);
        ledger.Entries.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_replayed_key_does_not_post_a_second_entry()
    {
        var (account, _) = AccountWithOpening(1_000);
        var ledger = new RecordingLedgerRepository();
        ledger.Existing.Add("repair-5");
        var audit = new RecordingAuditWriter();

        var result = await Create(account, ledger, audit).ExecuteAsync(
            account.ClubId,
            cashDeltaMinor: 100,
            reversesEntryId: null,
            "retried by the operator",
            "repair-5",
            CancellationToken.None);

        result.Outcome.Should().Be(PostCompensatingEntryOutcome.AlreadyPosted);
        ledger.Entries.Should().BeEmpty("the key's first entry is the one that counts (FIN-17)");
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_correction_that_would_overdraw_is_refused()
    {
        var (account, _) = AccountWithOpening(1_000);
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();

        var result = await Create(account, ledger, audit).ExecuteAsync(
            account.ClubId,
            cashDeltaMinor: -1_500,
            reversesEntryId: null,
            "too much",
            "repair-6",
            CancellationToken.None);

        result.Outcome.Should().Be(PostCompensatingEntryOutcome.NotAffordable);
        ledger.Entries.Should().BeEmpty("a balance is never negative (FIN-13)");
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_correction_that_moves_nothing_is_refused()
    {
        var (account, _) = AccountWithOpening(1_000);
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();

        var result = await Create(account, ledger, audit).ExecuteAsync(
            account.ClubId,
            cashDeltaMinor: 0,
            reversesEntryId: null,
            "no move",
            "repair-7",
            CancellationToken.None);

        result.Outcome.Should().Be(PostCompensatingEntryOutcome.InvalidAmount);
        ledger.Entries.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        var (account, _) = AccountWithOpening(1_000);

        var act = async () => await Create(account, new RecordingLedgerRepository(), new RecordingAuditWriter())
            .ExecuteAsync(account.ClubId, 100, null, reason, "repair-8", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static PostCompensatingEntry Create(
        ClubAccount account,
        RecordingLedgerRepository ledger,
        RecordingAuditWriter audit) =>
        new(
            new FixedClock(),
            new RecordingClubAccountRepository { Account = account },
            ledger,
            audit,
            new StubSecureTokens(),
            new StubRequestContext(),
            new RecordingUnitOfWork());

    /// <summary>Builds an account funded with an opening balance, plus the entry that funded it.</summary>
    private static (ClubAccount Account, LedgerEntry Opening) AccountWithOpening(long amountMinor)
    {
        var clubId = Guid.CreateVersion7();
        var account = ClubAccount.Open(Guid.CreateVersion7(), clubId, Now);
        var entry = account.Post(LedgerPostings.OpeningBalance(Guid.CreateVersion7(), clubId, amountMinor), Now);

        return (account, entry);
    }

    private static LedgerEntry OriginalFor(Guid clubId) =>
        LedgerEntry.Record(
            Guid.CreateVersion7(),
            clubId,
            sequence: 2,
            LedgerCategory.GateReceipt,
            cashDeltaMinor: 100,
            reservedDeltaMinor: 0,
            resultingCashMinor: 1_100,
            resultingReservedMinor: 0,
            LedgerSourceType.Matchday,
            sourceId: Guid.CreateVersion7(),
            correlationId: $"matchday:{Guid.CreateVersion7():D}",
            descriptionTemplate: "finance.gate_receipt",
            descriptionParametersJson: """{"amountMinor":100}""",
            Now);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class RecordingClubAccountRepository : IClubAccountRepository
    {
        public ClubAccount? Account { get; init; }

        public void Add(ClubAccount account) => throw new NotSupportedException();

        public Task<ClubAccount?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult(Account?.ClubId == clubId ? Account : null);

        public Task<IReadOnlyList<ClubAccount>> LoadAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ClubAccount>> LoadAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLedgerRepository : ILedgerRepository
    {
        public List<LedgerEntry> Entries { get; } = [];

        public HashSet<string> Existing { get; } = new(StringComparer.Ordinal);

        public LedgerEntry? Original { get; init; }

        public void Add(LedgerEntry entry) => Entries.Add(entry);

        public Task<LedgerEntry?> FindByIdAsync(Guid entryId, CancellationToken cancellationToken) =>
            Task.FromResult(Original?.Id == entryId ? Original : null);

        public Task<IReadOnlySet<string>> FindExistingCorrelationIdsAsync(
            IReadOnlyCollection<string> correlationIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(Existing.Where(correlationIds.Contains).ToHashSet(StringComparer.Ordinal));

        public Task<IReadOnlyList<ClubCategoryTotal>> LoadCategoryTotalsAsync(
            IReadOnlyCollection<Guid> clubIds,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, long>> LoadCashBalanceBeforeAsync(
            IReadOnlyCollection<Guid> clubIds,
            DateTimeOffset instant,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class StubSecureTokens : ISecureTokenService
    {
        public string CreateToken() => throw new NotSupportedException();

        public string CreateSecurityStamp() => throw new NotSupportedException();

        public byte[] CreateRandomBytes(int length) => throw new NotSupportedException();

        public string HashToken(string token) => throw new NotSupportedException();

        public string? HashClientValue(string? value) => value is null ? null : "hashed";
    }

    private sealed class StubRequestContext : IRequestContext
    {
        public Guid? ActorUserId { get; } = OperatorId;

        public string CorrelationId { get; } = Guid.CreateVersion7().ToString();

        public string? IpAddress => null;

        public string? UserAgent => null;
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IDatabaseTransaction> BeginTransactionAsync(
            TransactionIsolation isolation,
            CancellationToken cancellationToken) =>
            Task.FromResult<IDatabaseTransaction>(new NoOpTransaction());
    }

    private sealed class NoOpTransaction : IDatabaseTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
