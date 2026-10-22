using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using static Tactical11Manager.Application.Tests.Finance.FinanceDoubles;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// What a published round posts for the home club besides the gate: the snack bar's takings, lifted by the
/// result and judged against what the fans expected (`FIN-3`, `STRC-4`, `STRC-5`).
/// </summary>
/// <remarks>
/// The rule arithmetic is pinned by the domain tests. What this suite proves is the wiring: the crowd, the
/// level, the frozen expectation, the key and the history row.
/// </remarks>
public sealed class MatchdayFinancesTests
{
    // A Sunday afternoon, before the 23:00 boundary, so the week is the Sunday's own date.
    private static readonly DateTimeOffset Kickoff = new(2026, 10, 18, 15, 0, 0, TimeSpan.Zero);

    private static readonly StadiumSeats Ground = new(3_000, 1_000, 900, 100);

    [Fact]
    public async Task An_underdog_home_win_lifts_the_snack_bar_takings_by_thirty_percent()
    {
        var run = Arrange(homeGoals: 2, awayGoals: 0, expectedPoints: UnderdogPoints);

        await run.PostAsync();

        var spectators = StadiumRuleSet.AttendanceFor(Ground, 10_000, 5_000);
        var expected = StructureRuleSet.SnackTakingsMinorFor(1, spectators, 3_000, tier: 1);

        spectators.Should().BeGreaterThan(0);

        var entry = run.Ledger.Entries.Should().ContainSingle(item => item.Category == LedgerCategory.SnackBar).Which;

        entry.CashDeltaMinor.Should().Be(expected);
        entry.ClubId.Should().Be(run.HomeClubId);
        entry.SourceType.Should().Be(LedgerSourceType.Matchday);
        entry.CorrelationId.Should().Be($"matchday:{run.Fixture.Id:D}", "the snack bar shares the gate's fixture key");

        var row = run.Structures.Takings.Should().ContainSingle().Which;

        row.Kind.Should().Be(StructureKind.SnackBar);
        row.RowKind.Should().Be(StructureTakingsKind.MatchSale);
        row.FixtureId.Should().Be(run.Fixture.Id);
        row.Customers.Should().Be(spectators);
        row.BaseMinor.Should().Be(spectators * StructureRuleSet.SnackSpendMinorFor(1, 1));
        row.BonusBp.Should().Be(3_000);
        row.BonusReason.Should().Be("underdog_win");
        row.AmountMinor.Should().Be(expected);
        row.LedgerEntryId.Should().Be(entry.Id);
        row.WeekOf.Should().Be(new DateOnly(2026, 10, 18), "the week the Sunday run settles");
    }

    [Fact]
    public async Task A_win_by_the_favourite_earns_only_five_percent()
    {
        var run = Arrange(homeGoals: 3, awayGoals: 1, expectedPoints: FavouritePoints);

        await run.PostAsync();

        run.Structures.Takings.Should().ContainSingle().Which.BonusBp.Should().Be(500);
    }

    [Fact]
    public async Task A_defeat_earns_no_bonus_but_the_snack_bar_still_sells_to_the_crowd()
    {
        var run = Arrange(homeGoals: 0, awayGoals: 1, expectedPoints: UnderdogPoints);

        await run.PostAsync();

        var row = run.Structures.Takings.Should().ContainSingle().Which;

        row.BonusBp.Should().Be(0);
        row.BonusReason.Should().Be("none");
        row.AmountMinor.Should().Be(row.BaseMinor, "the crowd came before the result");
    }

    [Fact]
    public async Task A_fixture_with_no_frozen_expectation_is_judged_as_an_even_game_at_home()
    {
        var run = Arrange(homeGoals: 1, awayGoals: 0, expectedPoints: null);

        await run.PostAsync();

        var fallback = FanRuleSet.ExpectationForGap(0, atHome: true).ExpectedPointsCentis;
        var expected = StructureRuleSet.ResultBonusFor(1, 0, fallback);

        run.Structures.Takings.Should().ContainSingle().Which.BonusBp.Should().Be(expected.Bp);
    }

    [Fact]
    public async Task The_snack_bar_sells_at_its_own_level()
    {
        var run = Arrange(homeGoals: 0, awayGoals: 0, expectedPoints: FavouritePoints, snackBarLevel: 5);

        await run.PostAsync();

        var row = run.Structures.Takings.Should().ContainSingle().Which;

        row.Level.Should().Be(5);
        row.BaseMinor.Should().Be(row.Customers * StructureRuleSet.SnackSpendMinorFor(5, 1));
    }

    [Fact]
    public async Task A_club_at_the_opening_level_earns_level_one_takings()
    {
        // The basis reads a club with no snack bar row as level 1 (an AI club never upgrades, STRC-2).
        var run = Arrange(homeGoals: 1, awayGoals: 1, expectedPoints: UnderdogPoints);

        await run.PostAsync();

        run.Structures.Takings.Should().ContainSingle().Which.Level.Should().Be(StructureRuleSet.OpeningLevel);
        run.Ledger.Entries.Should().Contain(item => item.Category == LedgerCategory.SnackBar);
    }

    [Fact]
    public async Task A_ground_nobody_comes_to_posts_neither_a_gate_nor_a_snack_bar_sale()
    {
        var run = Arrange(homeGoals: 1, awayGoals: 0, expectedPoints: UnderdogPoints, fans: 0);

        var posted = await run.PostAsync();

        posted.Should().Be(0);
        run.Ledger.Entries.Should().BeEmpty();
        run.Structures.Takings.Should().BeEmpty();
    }

    private static int UnderdogPoints => FanRuleSet.ExpectationForGap(-FanRuleSet.CurveLimitCentis, atHome: true).ExpectedPointsCentis;

    private static int FavouritePoints => FanRuleSet.ExpectationForGap(FanRuleSet.CurveLimitCentis, atHome: true).ExpectedPointsCentis;

    private static Run Arrange(
        int homeGoals,
        int awayGoals,
        int? expectedPoints,
        int snackBarLevel = 1,
        int fans = 10_000)
    {
        var homeClubId = Guid.CreateVersion7();
        var fixture = Fixture.Schedule(
            Guid.CreateVersion7(), Guid.CreateVersion7(), homeClubId, Guid.CreateVersion7(), Kickoff, Now);

        fixture.Lock(Now);
        fixture.Stage(homeGoals, awayGoals, Guid.CreateVersion7(), Now);
        fixture.Publish(Now);

        var basis = new ClubRevenueBasis(homeClubId, Ground, 1, fans, MoodBp: 5_000, snackBarLevel);
        var account = ClubAccount.Open(Guid.CreateVersion7(), homeClubId, Now);
        var fanRepository = new StubFanRepository();

        if (expectedPoints is { } points)
        {
            fanRepository.Expectations.Add(FixtureFanExpectation.Freeze(
                fixture.Id,
                homeClubId,
                FanRuleSet.ExpectationForGap(0, atHome: true) with { ExpectedPointsCentis = points },
                Now));
        }

        var ledger = new RecordingLedgerRepository();
        var structures = new RecordingStructureRepository();
        var queries = new StubFinanceQueries();
        var accounts = new RecordingAccountRepository(account);

        queries.Revenue.Add(basis);

        var finances = new MatchdayFinances(
            queries,
            accounts,
            ledger,
            fanRepository,
            structures,
            Room(ledger, new RecordingAuditWriter()),
            States(accounts, queries));

        return new Run(finances, fixture, homeClubId, ledger, structures);
    }

    private sealed class Run(
        MatchdayFinances finances,
        Fixture fixture,
        Guid homeClubId,
        RecordingLedgerRepository ledger,
        RecordingStructureRepository structures)
    {
        public Fixture Fixture { get; } = fixture;

        public Guid HomeClubId { get; } = homeClubId;

        public RecordingLedgerRepository Ledger { get; } = ledger;

        public RecordingStructureRepository Structures { get; } = structures;

        public Task<int> PostAsync() =>
            finances.PostMatchdayTakingsAsync(Guid.CreateVersion7(), [Fixture], Now, CancellationToken.None);
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

    private sealed class StubFanRepository : IFanRepository
    {
        public List<FixtureFanExpectation> Expectations { get; } = [];

        public void Add(ClubFanBase fanBase)
        {
        }

        public void AddEvent(ClubFanEvent fanEvent)
        {
        }

        public void AddExpectation(FixtureFanExpectation expectation)
        {
        }

        public Task<IReadOnlyList<ClubFanBase>> LoadAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubFanBase>>([]);

        public Task<IReadOnlyList<FixtureFanExpectation>> LoadExpectationsAsync(
            IReadOnlyCollection<Guid> fixtureIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FixtureFanExpectation>>(
                [.. Expectations.Where(expectation => fixtureIds.Contains(expectation.FixtureId))]);

        public Task<IReadOnlySet<Guid>> FindClubsReviewedAsync(DateOnly weekOf, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }
}
