using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using static Tactical11Manager.Application.Tests.Finance.FinanceDoubles;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// How a club signs its sponsor: a manager's choice, the board's at the deadline, a new manager's second thoughts, and the computer clubs'
/// deals when a season opens (`SPN-2`…`SPN-6`).
/// </summary>
/// <remarks>
/// The arithmetic of the offers and the bonuses is pinned by the domain tests. What this suite proves is the wiring: what is staged, what is
/// paid for the matchdays already played, when the overdraft limit is set, and who is told.
/// </remarks>
public sealed class SponsorSigningTests
{
    private static readonly Guid WorldId = Guid.CreateVersion7();
    private static readonly Guid SeasonId = Guid.CreateVersion7();
    private static readonly Guid DivisionSeasonId = Guid.CreateVersion7();
    private static readonly StadiumSeats Ground = new(3_000, 1_000, 900, 100);

    [Fact]
    public async Task A_manager_signs_and_the_matchdays_already_played_are_paid_at_once()
    {
        var run = Arrange(published: 2);

        var result = await run.Subject.SignAsync(
            run.ClubId, SponsorOfferKind.Steady, SeasonObjective.TopHalf, expectedVersion: 0, Manager(), Now, CancellationToken.None);

        result.Outcome.Should().Be(SponsorSigningOutcome.Agreed);

        var deal = run.Sponsors.Deals.Should().ContainSingle().Which;

        deal.Kind.Should().Be(SponsorOfferKind.Steady);
        deal.Objective.Should().Be(SeasonObjective.TopHalf);
        deal.SignedBy.Should().Be(SponsorSigner.Manager);

        var entry = run.Ledger.Entries.Should().ContainSingle().Which;

        entry.Category.Should().Be(LedgerCategory.SponsorMatchday);
        entry.CashDeltaMinor.Should().Be(deal.FixedMatchdayMinor * 2, "two matchdays have been played");
        entry.CorrelationId.Should().Be($"sponsor-signing:{deal.Id:N}", "a retried signing collides with its first payment (FIN-17)");
        result.BackPaidMatchdays.Should().Be(2);
        result.BackPayMinor.Should().Be(entry.CashDeltaMinor);

        run.Account.OverdraftLimitMinor.Should().BeGreaterThan(0, "the limit is set from the forecast the deal gives, at signing (BUD-3)");
        run.Audit.Entries.Should().ContainSingle().Which.Action.Should().Be(FinanceAuditActions.SponsorSigned);
    }

    [Fact]
    public async Task A_signing_before_any_matchday_has_nothing_to_pay_back()
    {
        var run = Arrange(published: 0);

        var result = await run.Subject.SignAsync(
            run.ClubId, SponsorOfferKind.Balanced, SeasonObjective.TopSix, 0, Manager(), Now, CancellationToken.None);

        result.Outcome.Should().Be(SponsorSigningOutcome.Agreed);
        run.Ledger.Entries.Should().BeEmpty();
        run.Sponsors.Deals.Should().ContainSingle();
    }

    [Fact]
    public async Task A_stale_version_is_refused_and_nothing_is_staged()
    {
        var run = Arrange(published: 1);

        var result = await run.Subject.SignAsync(
            run.ClubId, SponsorOfferKind.Steady, SeasonObjective.StayUp, expectedVersion: 7, Manager(), Now, CancellationToken.None);

        result.Outcome.Should().Be(SponsorSigningOutcome.PreconditionFailed);
        run.Sponsors.Deals.Should().BeEmpty();
        run.Ledger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_club_with_no_offers_cannot_sign()
    {
        var run = Arrange(published: 0, withOffers: false);

        var result = await run.Subject.SignAsync(
            run.ClubId, SponsorOfferKind.Steady, SeasonObjective.StayUp, 0, Manager(), Now, CancellationToken.None);

        result.Outcome.Should().Be(SponsorSigningOutcome.NoOffers);
    }

    [Fact]
    public async Task The_board_signs_the_balanced_offer_at_the_realistic_objective_and_tells_the_manager()
    {
        var run = Arrange(published: 3, rank: 5);
        var managerId = Guid.CreateVersion7();

        run.Inbox.Targets.Add(new ClubInboxTarget(run.ClubId, managerId, "Test FC"));

        var result = await run.Subject.SignAsync(
            run.ClubId, kind: null, objective: null, expectedVersion: null, Board(), Now, CancellationToken.None);

        result.Outcome.Should().Be(SponsorSigningOutcome.Agreed);

        var deal = run.Sponsors.Deals.Should().ContainSingle().Which;

        deal.Kind.Should().Be(SponsorOfferKind.Balanced);
        deal.Objective.Should().Be(SeasonObjective.TopSix, "the fifth strongest squad is a top-six side");
        deal.SignedBy.Should().Be(SponsorSigner.Board);
        run.Ledger.Entries.Single().CashDeltaMinor.Should().Be(deal.FixedMatchdayMinor * 3);

        var message = run.Inbox.Messages.Should().ContainSingle().Which;

        message.TemplateKey.Should().Be(FinanceInboxTemplates.SponsorSignedByBoard);
        message.RecipientManagerId.Should().Be(managerId);
    }

    [Fact]
    public async Task A_club_that_has_signed_cannot_sign_again_unless_a_new_manager_has_just_arrived()
    {
        var run = Arrange(published: 1);

        await run.Subject.SignAsync(run.ClubId, SponsorOfferKind.Steady, SeasonObjective.TopHalf, 0, Manager(), Now, CancellationToken.None);
        run.Queries.TenureStart = Now.AddDays(-30);

        var again = await run.Subject.SignAsync(
            run.ClubId, SponsorOfferKind.Performance, SeasonObjective.WinLeague, run.Sponsors.Deals[0].Version, Manager(), Now, CancellationToken.None);

        again.Outcome.Should().Be(SponsorSigningOutcome.AlreadySigned, "this manager signed the deal himself");
        run.Sponsors.Deals.Should().ContainSingle().Which.Kind.Should().Be(SponsorOfferKind.Steady);
    }

    [Fact]
    public async Task A_new_manager_may_take_another_offer_and_objective_in_his_first_matchdays()
    {
        var run = Arrange(published: 1);

        await run.Subject.SignAsync(run.ClubId, SponsorOfferKind.Steady, SeasonObjective.TopHalf, 0, Ai(), Now.AddDays(-5), CancellationToken.None);

        run.Queries.TenureStart = Now;
        run.Queries.PublishedSinceTakeover = 1;
        run.Ledger.Entries.Clear();

        var deal = run.Sponsors.Deals[0];
        var result = await run.Subject.SignAsync(
            run.ClubId, SponsorOfferKind.Performance, SeasonObjective.WinLeague, deal.Version, Manager(), Now, CancellationToken.None);

        result.Outcome.Should().Be(SponsorSigningOutcome.Changed);
        run.Sponsors.Deals.Should().ContainSingle();
        deal.Kind.Should().Be(SponsorOfferKind.Performance);
        deal.Objective.Should().Be(SeasonObjective.WinLeague);
        deal.SignedBy.Should().Be(SponsorSigner.Manager);
        run.Ledger.Entries.Should().BeEmpty("the matchdays already paid are not paid twice");
        run.Audit.Entries.Last().Action.Should().Be(FinanceAuditActions.SponsorChanged);
    }

    [Fact]
    public async Task The_days_to_think_again_pass_after_three_matchdays()
    {
        var run = Arrange(published: 4);

        await run.Subject.SignAsync(run.ClubId, SponsorOfferKind.Steady, SeasonObjective.TopHalf, 0, Ai(), Now.AddDays(-9), CancellationToken.None);

        run.Queries.TenureStart = Now.AddDays(-6);
        run.Queries.PublishedSinceTakeover = SponsorDeadline.TakeoverWindowMatchdays;

        var result = await run.Subject.SignAsync(
            run.ClubId, SponsorOfferKind.Performance, SeasonObjective.WinLeague, run.Sponsors.Deals[0].Version, Manager(), Now, CancellationToken.None);

        result.Outcome.Should().Be(SponsorSigningOutcome.AlreadySigned);
    }

    [Fact]
    public async Task The_board_never_overrides_a_deal_that_exists()
    {
        var run = Arrange(published: 1);

        await run.Subject.SignAsync(run.ClubId, SponsorOfferKind.Steady, SeasonObjective.TopHalf, 0, Manager(), Now, CancellationToken.None);

        var result = await run.Subject.SignAsync(run.ClubId, null, null, null, Board(), Now, CancellationToken.None);

        result.Outcome.Should().Be(SponsorSigningOutcome.AlreadySigned);
    }

    [Fact]
    public async Task The_deadline_job_signs_every_club_that_has_not_signed_and_leaves_the_rest()
    {
        var run = Arrange(published: 3);
        var signedClub = Guid.CreateVersion7();

        run.Queries.Unsigned.Add(run.ClubId);

        var job = new SignDueSponsorDeals(
            run.Queries,
            run.Subject,
            new FixedClock(Now),
            new CountingUnitOfWork(),
            NullLogger<SignDueSponsorDeals>.Instance);

        var signed = await job.ExecuteAsync(DivisionSeasonId, CancellationToken.None);

        signed.Should().Be(1);
        run.Sponsors.Deals.Should().ContainSingle().Which.SignedBy.Should().Be(SponsorSigner.Board);
        run.Sponsors.Deals.Should().NotContain(deal => deal.ClubId == signedClub);
    }

    [Fact]
    public async Task Opening_a_season_signs_the_computer_clubs_and_leaves_the_managers_to_choose()
    {
        var sponsors = new RecordingSponsorRepository();
        var humanClub = Guid.CreateVersion7();
        var aiClubs = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToList();
        var opener = new SponsorSeasonOpener(
            sponsors,
            new StubClubRepository(),
            new StubFanQueries(),
            new FanBaseKeeper(new NullFanRepository()),
            new RecordingInboxRepository());

        var opening = opener.Open(
            WorldId,
            SeasonId,
            1,
            1,
            [
                .. aiClubs.Select((clubId, index) => new SponsorClubInput(clubId, 20_000, 50, index + 1, IsHuman: false)),
                new SponsorClubInput(humanClub, 20_000, 50, 6, IsHuman: true),
            ],
            Now);

        opening.Offers.Should().Be(18, "three offers to each of six clubs");
        opening.AiDeals.Should().Be(5);
        opening.Choosing.Should().ContainSingle().Which.Should().Be(
            new SponsorUnsignedClub(humanClub, SeasonObjective.TopSix));

        sponsors.Offers.GroupBy(offer => offer.ClubId).Should().OnlyContain(group => group.Count() == 3);
        sponsors.Deals.Should().HaveCount(5).And.OnlyContain(deal => deal.SignedBy == SponsorSigner.Ai);
        sponsors.Deals.Should().NotContain(deal => deal.ClubId == humanClub);

        foreach (var deal in sponsors.Deals)
        {
            var rank = aiClubs.IndexOf(deal.ClubId) + 1;

            deal.Objective.Should().Be(AiFinancePolicy.ChooseObjective(rank), "a computer club aims at the board's realistic objective");
            deal.Kind.Should().Be(AiFinancePolicy.ChooseOffer(Tactical11Manager.Domain.Squad.AiMatchPlanPolicy.TemperamentOf(deal.ClubId)));
        }
    }

    [Fact]
    public async Task Opening_a_rollover_season_tells_a_manager_what_is_on_offer_and_by_when()
    {
        var humanClub = Guid.CreateVersion7();
        var aiClub = Guid.CreateVersion7();
        var managerId = Guid.CreateVersion7();
        var inbox = new RecordingInboxRepository();
        var sponsors = new RecordingSponsorRepository();

        inbox.Targets.Add(new ClubInboxTarget(humanClub, managerId, "Test FC"));

        var season = Season.Create(
            SeasonId,
            WorldId,
            sequenceNumber: 2,
            gameYear: 2027,
            ruleSetVersion: WorldRuleSet.Version,
            firstMatchday: SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2027, 8, 1)),
            Now);

        var opener = new SponsorSeasonOpener(
            sponsors,
            new StubClubRepository(),
            new StubFanQueries(humanClub, 3),
            new FanBaseKeeper(new NullFanRepository()),
            inbox);

        var opening = await opener.OpenSeasonAsync(
            season,
            tier: 1,
            [humanClub, aiClub],
            new HashSet<Guid> { humanClub },
            Now,
            CancellationToken.None);

        opening.Offers.Should().Be(6);
        opening.AiDeals.Should().Be(1);
        sponsors.Deals.Should().ContainSingle().Which.ClubId.Should().Be(aiClub);

        var message = inbox.Messages.Should().ContainSingle().Which;

        message.RecipientManagerId.Should().Be(managerId);
        message.TemplateKey.Should().Be(FinanceInboxTemplates.SponsorOffersReady);

        var text = InboxMessageText.Render(message.TemplateKey, message.ParametersJson);

        text.Body.Should().Contain(
            SponsorDeadline.LockAtFor(DateOnly.FromDateTime(season.StartsAt.UtcDateTime)).UtcDateTime.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Opening_a_season_is_the_same_every_time_for_the_same_world()
    {
        var clubId = Guid.CreateVersion7();
        var first = new RecordingSponsorRepository();
        var second = new RecordingSponsorRepository();

        foreach (var sponsors in new[] { first, second })
        {
            new SponsorSeasonOpener(
                    sponsors,
                    new StubClubRepository(),
                    new StubFanQueries(),
                    new FanBaseKeeper(new NullFanRepository()),
                    new RecordingInboxRepository())
                .Open(WorldId, SeasonId, 3, 2, [new SponsorClubInput(clubId, 9_000, 42, 4, IsHuman: false)], Now);
        }

        first.Offers.Select(offer => (offer.SponsorName, offer.FixedSeasonMinor))
            .Should().Equal(second.Offers.Select(offer => (offer.SponsorName, offer.FixedSeasonMinor)));

        await Task.CompletedTask;
    }

    private static SponsorActor Manager() => new(SponsorSigner.Manager, Guid.CreateVersion7(), "test", "hash");

    private static SponsorActor Board() => new(SponsorSigner.Board, null, "test", null);

    private static SponsorActor Ai() => new(SponsorSigner.Ai, null, "test", null);

    private static Run Arrange(int published, bool withOffers = true, int rank = 5)
    {
        var clubId = Guid.CreateVersion7();
        var account = Funded(clubId, 500_000_000);
        var sponsors = new RecordingSponsorRepository();

        if (withOffers)
        {
            sponsors.AddOffers(
                SponsorOffers.For(WorldId, 1, clubId, 1, 20_000, 50).Select(offer =>
                    ClubSponsorOffer.Record(Guid.CreateVersion7(), clubId, SeasonId, offer, 1, 20_000, 50, Now)));
        }

        var queries = new StubSponsorQueries
        {
            Context = new SponsorSeasonContext(clubId, "Test FC", WorldId, SeasonId, 1, new DateOnly(2026, 10, 3), DivisionSeasonId, 1, 20_000, 50, published),
        };

        var finance = new StubFinanceQueries();

        finance.Basis.Add(new ClubFinanceBasis(clubId, 1, Ground, 20_000, 5_000, 1, 1, 0, 34_000_000, 22));

        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();
        var inbox = new RecordingInboxRepository();
        var fanQueries = new StubFanQueries(clubId, rank);

        return new Run(
            new SponsorSigning(
                queries,
                sponsors,
                new RecordingAccountRepository(account),
                ledger,
                finance,
                fanQueries,
                inbox,
                audit,
                new NoOpLock()),
            clubId,
            account,
            sponsors,
            queries,
            ledger,
            audit,
            inbox);
    }

    private sealed record Run(
        SponsorSigning Subject,
        Guid ClubId,
        ClubAccount Account,
        RecordingSponsorRepository Sponsors,
        StubSponsorQueries Queries,
        RecordingLedgerRepository Ledger,
        RecordingAuditWriter Audit,
        RecordingInboxRepository Inbox);

    private sealed class StubSponsorQueries : ISponsorQueries
    {
        public SponsorSeasonContext? Context { get; set; }

        public DateTimeOffset? TenureStart { get; set; }

        public int PublishedSinceTakeover { get; set; }

        public List<Guid> Unsigned { get; } = [];

        public Task<SponsorSeasonContext?> GetClubContextAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult(Context is not null && Context.ClubId == clubId ? Context : null);

        public Task<IReadOnlyList<Guid>> ListClubsWithoutDealAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([.. Unsigned]);

        public Task<int> CountMatchdaysPublishedSinceAsync(Guid divisionSeasonId, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(PublishedSinceTakeover);

        public Task<DateTimeOffset?> GetTenureStartAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult(TenureStart);
    }

    /// <summary>A division of eighteen where the given club ranks where the test says by squad strength.</summary>
    private sealed class StubFanQueries : IFanQueries
    {
        private readonly Guid _clubId;
        private readonly int _rank;

        public StubFanQueries()
        {
        }

        public StubFanQueries(Guid clubId, int rank)
        {
            _clubId = clubId;
            _rank = rank;
        }

        public Task<IReadOnlyList<FanSeasonClubRow>> ListDivisionOfClubAsync(Guid clubId, CancellationToken cancellationToken)
        {
            var rows = new List<FanSeasonClubRow> { new(_clubId, DivisionSeasonId, SeasonId, 1, 0, 0, 0) };

            // Seventeen rivals, with identities that sort after the club's so the ranking has no ties to break.
            rows.AddRange(Enumerable.Range(0, 17).Select(index =>
                new FanSeasonClubRow(Guid.CreateVersion7(), DivisionSeasonId, SeasonId, 1, 0, 0, 0)));

            return Task.FromResult<IReadOnlyList<FanSeasonClubRow>>(_clubId == Guid.Empty ? [] : rows);
        }

        public Task<IReadOnlyDictionary<Guid, int>> GetSquadStrengthsAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken)
        {
            // The club has (_rank - 1) stronger rivals; the others are weaker.
            var strengths = new Dictionary<Guid, int>();
            var rivals = clubIds.Where(id => id != _clubId).ToList();

            strengths[_clubId] = 5_000;

            for (var index = 0; index < rivals.Count; index++)
            {
                strengths[rivals[index]] = index < _rank - 1 ? 6_000 + index : 4_000 - index;
            }

            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(strengths);
        }

        public Task<IReadOnlyList<FanSeasonClubRow>> ListCurrentSeasonAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, FanForm>> GetFormAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int?> GetDivisionTierAsync(Guid divisionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> GetFirstSeasonClubIdsAsync(IReadOnlyCollection<Guid> clubIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FanClubRow?> GetClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FanEventRow>> ListEventsAsync(Guid clubId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, FanExpectation>> GetFrozenExpectationsAsync(
            Guid clubId,
            IReadOnlyCollection<Guid> fixtureIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NullFanRepository : IFanRepository
    {
        public void Add(ClubFanBase fanBase)
        {
        }

        public void AddEvent(ClubFanEvent fanEvent)
        {
        }

        public Task<IReadOnlyList<ClubFanBase>> LoadAsync(IReadOnlyCollection<Guid> clubIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubFanBase>>([]);

        public Task<IReadOnlyList<FixtureFanExpectation>> LoadExpectationsAsync(
            IReadOnlyCollection<Guid> fixtureIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddExpectation(FixtureFanExpectation expectation) => throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> FindClubsReviewedAsync(DateOnly weekOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpLock : IAdvisoryLock
    {
        public Task AcquireAsync(AdvisoryLockKey key, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IDatabaseTransaction> BeginTransactionAsync(TransactionIsolation isolation, CancellationToken cancellationToken) =>
            Task.FromResult<IDatabaseTransaction>(new NoTransaction());
    }

    private sealed class NoTransaction : IDatabaseTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
