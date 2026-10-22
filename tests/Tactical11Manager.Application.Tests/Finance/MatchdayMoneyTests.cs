using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;
using static Tactical11Manager.Application.Tests.Finance.FinanceDoubles;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// What a published round pays every club of the division: the league's television money, the sponsor's fixed part and the wages, and what
/// reading the clubs' money then does (`FIN-4`, `FIN-7`, `SPN-3`, `DEBT-1`…`DEBT-4`).
/// </summary>
/// <remarks>
/// The arithmetic is pinned by the domain tests. What this suite proves is the wiring: who is paid, under which key, when the overdraft
/// limit is set, and what a club that cannot pay is made to do.
/// </remarks>
public sealed class MatchdayMoneyTests
{
    private static readonly Guid MatchdayId = Guid.CreateVersion7();

    private static readonly StadiumSeats Ground = new(3_000, 1_000, 900, 100);

    [Fact]
    public async Task Every_club_of_the_division_is_paid_the_television_money_and_charged_its_wages_under_one_key()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(Basis(clubId, wage: 30_000_000, players: 22), Funded(clubId, 500_000_000));

        await run.Subject.PostMatchdayMoneyAsync(MatchdayId, 1, 5, Now, CancellationToken.None);

        run.Ledger.Entries.Select(entry => entry.Category).Should().BeEquivalentTo(
            [LedgerCategory.LeagueTv, LedgerCategory.Wages],
            "no sponsor deal is signed yet, so there is nothing from a sponsor");

        var key = $"money:{MatchdayId:N}:{clubId:N}";

        run.Ledger.Entries.Should().OnlyContain(entry => entry.CorrelationId == key, "a retried publication collides with each (FIN-17)");

        var television = run.Ledger.Entries.Single(entry => entry.Category == LedgerCategory.LeagueTv);
        var wages = run.Ledger.Entries.Single(entry => entry.Category == LedgerCategory.Wages);

        television.CashDeltaMinor.Should().Be(EconomyRuleSet.LeagueTvSeasonMinorFor(1) / EconomyRuleSet.MatchdaysPerSeason);
        wages.CashDeltaMinor.Should().Be(-1_000_000, "a thirtieth of the season's wage bill");
        wages.SourceType.Should().Be(LedgerSourceType.Matchday);
    }

    [Fact]
    public async Task A_club_with_a_signed_deal_is_paid_its_fixed_part_on_every_matchday()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(Basis(clubId, wage: 0, players: 0, sponsorMatchday: 123_456), Funded(clubId, 500_000_000));

        await run.Subject.PostMatchdayMoneyAsync(MatchdayId, 1, 5, Now, CancellationToken.None);

        run.Ledger.Entries.Should().ContainSingle(entry => entry.Category == LedgerCategory.SponsorMatchday)
            .Which.CashDeltaMinor.Should().Be(123_456);
        run.Ledger.Entries.Should().NotContain(entry => entry.Category == LedgerCategory.Wages, "a club with no payroll pays no wages");
    }

    [Fact]
    public async Task The_overdraft_limit_is_set_from_the_forecast_on_the_first_round_and_again_after_the_fifteenth()
    {
        var clubId = Guid.CreateVersion7();
        var basis = Basis(clubId, wage: 0, players: 0);
        var account = Funded(clubId, 500_000_000);
        var run = Arrange(basis, account);

        await run.Subject.PostMatchdayMoneyAsync(MatchdayId, 1, 1, Now, CancellationToken.None);

        account.OverdraftLimitMinor.Should().Be(basis.OverdraftLimitMinor).And.BePositive();
        account.OverdraftFloorMinor.Should().Be(2 * basis.OverdraftLimitMinor, "the integrity net is twice the limit");

        // The ground grew and the fans came: the limit follows the forecast after the fifteenth round.
        run.Queries.Basis[0] = basis with { Fans = basis.Fans * 3 };

        await run.Subject.PostMatchdayMoneyAsync(Guid.CreateVersion7(), 1, EconomyRuleSet.MatchdaysPerSeason / 2, Now.AddDays(100), CancellationToken.None);

        account.OverdraftLimitMinor.Should().Be(run.Queries.Basis[0].OverdraftLimitMinor).And.BeGreaterThan(basis.OverdraftLimitMinor);
    }

    [Fact]
    public void The_limit_is_not_lowered_below_what_the_debt_the_club_already_carries_needs()
    {
        var clubId = Guid.CreateVersion7();

        // A club deep in the red under a big limit: the new, smaller forecast must not strand its balance below the new floor.
        var account = Funded(clubId, 0, overdraftLimitMinor: 1_000_000_000);

        account.Post(LedgerPostings.OperatingCost(Guid.CreateVersion7(), clubId, new DateOnly(2026, 9, 1), 1_500_000_000), Now);

        MatchdayFinances.SetOverdraft(account, limitMinor: 100, Now);

        account.OverdraftLimitMinor.Should().Be(750_000_000, "half the debt, so the floor of twice the limit still holds it");
        account.CashMinor.Should().Be(-1_500_000_000);
    }

    [Fact]
    public async Task Wages_that_pass_the_overdraft_floor_are_met_by_a_grant_of_exactly_the_difference()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(Basis(clubId, wage: 34_000_000, players: 22), Funded(clubId, 0));

        // No cash, and the forecast gives the first round a limit; make the wages bigger than the whole floor.
        run.Queries.Basis[0] = run.Queries.Basis[0] with { SeasonWageMinor = 34L * 20_000_000_000 };

        await run.Subject.PostMatchdayMoneyAsync(MatchdayId, 1, 5, Now, CancellationToken.None);

        var account = run.Accounts.All.Single();
        var grant = run.Ledger.Entries.Should().ContainSingle(entry => entry.Category == LedgerCategory.EmergencyGrant).Which;

        grant.CashDeltaMinor.Should().BePositive();
        account.CashMinor.Should().Be(-account.OverdraftFloorMinor, "the grant brings the club to its floor and no further");
        run.Audit.Entries.Should().ContainSingle(entry => entry.Action == FinanceAuditActions.EmergencyGrant);
    }

    [Fact]
    public async Task A_club_whose_wages_take_it_below_zero_is_in_the_red_and_its_manager_is_told()
    {
        var clubId = Guid.CreateVersion7();
        var managerId = Guid.CreateVersion7();
        var basis = Basis(clubId, wage: 34L * 20_000_000, players: 22);
        var account = Funded(clubId, 100_000);
        var run = Arrange(basis, account);

        run.Inbox.Targets.Add(new ClubInboxTarget(clubId, managerId, "Test FC"));

        var states = await run.Subject.PostMatchdayMoneyAsync(MatchdayId, 1, 5, Now, CancellationToken.None);

        states.Changed.Should().Be(1);
        account.FinancialState.Should().Be(FinancialStateKind.InTheRed);
        account.StateSinceMatchday.Should().Be(5);
        run.Inbox.Messages.Should().ContainSingle().Which.TemplateKey.Should().Be(FinanceInboxTemplates.InTheRed);
        run.Audit.Entries.Should().Contain(entry => entry.Action == FinanceAuditActions.StateChanged);
    }

    [Fact]
    public async Task A_leading_bid_is_withdrawn_when_the_wages_would_leave_it_reserved_above_the_cash()
    {
        var clubId = Guid.CreateVersion7();
        var account = Funded(clubId, 10_000_000);
        var bid = TransferBid.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), clubId, 10_000_000, "key", null, Now);

        account.Post(LedgerPostings.BidReservation(Guid.CreateVersion7(), clubId, bid.Id, 10_000_000), Now);

        var run = Arrange(Basis(clubId, wage: 34L * 30_000_000, players: 22), account, new StubBidRepository(bid));

        await run.Subject.PostMatchdayMoneyAsync(MatchdayId, 1, 5, Now, CancellationToken.None);

        bid.Status.Should().Be(BidStatus.Released);
        account.ReservedMinor.Should().Be(0);
        run.Ledger.Entries.Should().Contain(entry => entry.Category == LedgerCategory.ReservationRelease);
    }

    [Fact]
    public async Task A_club_in_administration_has_its_highest_earners_listed_by_the_board_with_the_flag()
    {
        var clubId = Guid.CreateVersion7();
        var managerId = Guid.CreateVersion7();
        var basis = Basis(clubId, wage: 34_000_000_000, players: 24);
        var account = Funded(clubId, 0);

        // A balance past the limit: the next reading puts the club in administration.
        account.SetOverdraft(1_000_000, Now);
        account.Post(LedgerPostings.OperatingCost(Guid.CreateVersion7(), clubId, new DateOnly(2026, 9, 1), 1_500_000), Now);

        var squad = Enumerable.Range(0, 24)
            .Select(index => new AdministrationPlayer(
                Guid.CreateVersion7(),
                index == 0 ? PositionFamily.Goalkeeper : PositionFamily.Midfield,
                SeasonWageMinor: 1_000_000_000 + (index * 10_000),
                ValueMinor: 5_000_000_000,
                IsListed: false))
            .ToList();

        var run = Arrange(basis, account);

        run.Queries.Squad.AddRange(squad);
        run.Inbox.Targets.Add(new ClubInboxTarget(clubId, managerId, "Test FC"));

        // The basis gives the limit back on round 1, which would lift the club out of administration; keep the one it holds.
        var states = await run.Subject.PostMatchdayMoneyAsync(MatchdayId, 1, 5, Now, CancellationToken.None);

        states.Listed.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(EconomyRuleSet.AdministrationMaxListings);
        run.Listings.Calls.Should().OnlyContain(call => call.ListedByBoard && call.ClubId == clubId);
        run.Listings.Calls.Select(call => call.MinimumFeeMinor).Should().OnlyContain(fee => fee == 5_000_000_000 * 8_000 / 10_000);
        run.Inbox.Messages.Should().Contain(message => message.TemplateKey == FinanceInboxTemplates.BoardListedPlayers);
    }

    private static ClubFinanceBasis Basis(
        Guid clubId,
        long wage,
        int players,
        long sponsorMatchday = 0,
        int fans = 20_000) =>
        new(clubId, 1, Ground, fans, 5_000, 1, 1, sponsorMatchday, wage, players);

    private static Run Arrange(ClubFinanceBasis basis, ClubAccount account, StubBidRepository? bids = null)
    {
        var queries = new StubFinanceQueries();

        queries.Basis.Add(basis);
        queries.Revenue.Add(new ClubRevenueBasis(basis.ClubId, basis.Seats, basis.TierNumber, basis.Fans, basis.MoodBp));

        var accounts = new RecordingAccountRepository(account);
        var ledger = new RecordingLedgerRepository();
        var audit = new RecordingAuditWriter();
        var inbox = new RecordingInboxRepository();
        var listings = new RecordingListingWriter();

        var subject = new MatchdayFinances(
            queries,
            accounts,
            ledger,
            new NoFans(),
            new NoStructures(),
            Room(ledger, audit, bids, inbox),
            States(accounts, queries, inbox, listings, audit));

        return new Run(subject, queries, accounts, ledger, audit, inbox, listings);
    }

    private sealed record Run(
        MatchdayFinances Subject,
        StubFinanceQueries Queries,
        RecordingAccountRepository Accounts,
        RecordingLedgerRepository Ledger,
        RecordingAuditWriter Audit,
        RecordingInboxRepository Inbox,
        RecordingListingWriter Listings);

    private sealed class NoFans : Tactical11Manager.Application.Abstractions.Fans.IFanRepository
    {
        public void Add(ClubFanBase fanBase)
        {
        }

        public void AddEvent(ClubFanEvent fanEvent)
        {
        }

        public void AddExpectation(FixtureFanExpectation expectation)
        {
        }

        public Task<IReadOnlyList<ClubFanBase>> LoadAsync(IReadOnlyCollection<Guid> clubIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubFanBase>>([]);

        public Task<IReadOnlyList<FixtureFanExpectation>> LoadExpectationsAsync(
            IReadOnlyCollection<Guid> fixtureIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FixtureFanExpectation>>([]);

        public Task<IReadOnlySet<Guid>> FindClubsReviewedAsync(DateOnly weekOf, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }

    private sealed class NoStructures : Tactical11Manager.Application.Abstractions.World.IStructureRepository
    {
        public void Add(ClubStructure building)
        {
        }

        public void AddTakings(ClubStructureTakings takings)
        {
        }

        public Task<ClubStructure?> FindAsync(Guid buildingId, CancellationToken cancellationToken) =>
            Task.FromResult<ClubStructure?>(null);

        public Task<ClubStructure?> FindByClubAsync(Guid clubId, StructureKind kind, CancellationToken cancellationToken) =>
            Task.FromResult<ClubStructure?>(null);

        public Task<IReadOnlyList<ClubStructure>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubStructure>>([]);
    }
}
