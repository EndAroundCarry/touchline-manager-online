using FluentAssertions;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Application.World;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Ops;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// What a computer club builds, and that it builds through the writer a manager uses (`ai-finance-v1`, `INS-12`, `FIN-15`).
/// </summary>
/// <remarks>
/// The policy is pinned by the domain tests. This suite proves the orchestration: a club that sold out pays for places by the ledger posting a
/// manager's order makes, as a service actor, in one save; a club in the red builds nothing; and the season must have played enough home matches
/// before a ground counts as sold out.
/// </remarks>
public sealed class EvaluateAiBuildingTests
{
    private static readonly Guid ClubId = Guid.Parse("0192f100-0000-7000-8000-000000000001");

    [Fact]
    public async Task A_club_whose_ground_sold_out_pays_for_new_places_as_a_service_actor_and_saves_once()
    {
        var run = Arrange(fans: 40_000, cashMinor: 50_000_000_000);

        var result = await run.UseCase.ExecuteAsync(CancellationToken.None);

        result.Clubs.Should().Be(1);
        result.Orders.Should().BeGreaterThan(0);
        result.Skipped.Should().Be(0);
        run.Stadium.Capacity.Should().BeGreaterThan(StadiumRuleSet.OpeningStanding + StadiumRuleSet.OpeningSeating + StadiumRuleSet.OpeningCoveredSeating + StadiumRuleSet.OpeningVip);

        run.Ledger.Entries.Should().Contain(entry => entry.Category == LedgerCategory.StadiumConstruction);
        run.Audit.Entries.Should().OnlyContain(entry => entry.ActorType == AuditActorTypes.Service && entry.ActorUserId == null);
        run.Unit.Saves.Should().Be(1);
    }

    [Fact]
    public async Task A_club_in_the_red_builds_nothing_whatever_its_ground_sells()
    {
        var run = Arrange(fans: 40_000, cashMinor: 50_000_000_000, state: FinancialStateKind.InTheRed);

        var result = await run.UseCase.ExecuteAsync(CancellationToken.None);

        result.Orders.Should().Be(0);
        result.Upgrades.Should().Be(0);
        run.Ledger.Entries.Should().BeEmpty();
        run.Unit.Saves.Should().Be(0, "nothing was written, so nothing is committed");
    }

    [Fact]
    public async Task A_ground_is_not_read_as_sold_out_before_the_season_has_five_home_matches_of_evidence()
    {
        var run = Arrange(fans: 40_000, cashMinor: 50_000_000_000, rounds: EvaluateAiBuilding.EvidenceRounds - 1);

        var result = await run.UseCase.ExecuteAsync(CancellationToken.None);

        result.Orders.Should().Be(0);
        run.Ledger.Entries.Should().NotContain(entry => entry.Category == LedgerCategory.StadiumConstruction);
    }

    [Fact]
    public async Task A_grown_club_starts_an_upgrade_that_a_manager_would_pay_for_the_same_way()
    {
        var run = Arrange(
            fans: 80_000,
            cashMinor: 50_000_000_000,
            seats: new StadiumSeats(9_000, 4_000, 5_600, 1_400),
            rounds: 0);

        var result = await run.UseCase.ExecuteAsync(CancellationToken.None);

        result.Upgrades.Should().BeGreaterThan(0);
        run.Ledger.Entries.Should().Contain(entry => entry.Category == LedgerCategory.StructureUpgrade);
        run.Jobs.Enqueued.Should().Contain(request => request.JobType == StructureJobTypes.CompleteUpgrade, "the same job finishes the work");
        run.Audit.Entries.Should().Contain(entry => entry.Action == WorldAuditActions.StructureUpgradeStarted && entry.ActorType == AuditActorTypes.Service);
    }

    [Fact]
    public async Task A_world_with_no_computer_clubs_does_nothing()
    {
        var run = Arrange(fans: 40_000, cashMinor: 50_000_000_000, withClub: false);

        var result = await run.UseCase.ExecuteAsync(CancellationToken.None);

        result.Should().Be(new EvaluateAiBuildingResult(0, 0, 0, 0));
        run.Unit.Saves.Should().Be(0);
    }

    private static Run Arrange(
        int fans,
        long cashMinor,
        FinancialStateKind state = FinancialStateKind.Healthy,
        int rounds = EvaluateAiBuilding.EvidenceRounds,
        StadiumSeats? seats = null,
        bool withClub = true)
    {
        var now = FinanceDoubles.Now;
        var stadium = ClubStadium.Open(Guid.CreateVersion7(), ClubId, now);
        var snackBar = ClubStructure.Open(Guid.CreateVersion7(), ClubId, StructureKind.SnackBar, now);
        var shop = ClubStructure.Open(Guid.CreateVersion7(), ClubId, StructureKind.Merchandise, now);
        var account = FinanceDoubles.Funded(ClubId, cashMinor);

        var ground = seats ?? new StadiumSeats(stadium.StandingSeats, stadium.SeatingSeats, stadium.CoveredSeats, stadium.VipSeats);

        if (seats is { } bigger)
        {
            stadium.AddSeats(StadiumStand.Standing, bigger.Standing - stadium.StandingSeats, now);
            stadium.AddSeats(StadiumStand.Seating, bigger.Seating - stadium.SeatingSeats, now);
            stadium.AddSeats(StadiumStand.CoveredSeating, bigger.CoveredSeating - stadium.CoveredSeats, now);
            stadium.AddSeats(StadiumStand.Vip, bigger.Vip - stadium.VipSeats, now);
        }

        var finance = new FinanceDoubles.StubFinanceQueries();

        finance.Basis.Add(new ClubFinanceBasis(
            ClubId,
            TierNumber: 1,
            ground,
            fans,
            FanRuleSet.CalmMoodBp,
            StructureRuleSet.OpeningLevel,
            StructureRuleSet.OpeningLevel,
            SponsorFixedMatchdayMinor: 0,
            SeasonWageMinor: 0,
            ContractedPlayers: 0));

        var ledger = new FinanceDoubles.RecordingLedgerRepository();
        var audit = new FinanceDoubles.RecordingAuditWriter();
        var jobs = new RecordingJobQueue();
        var unit = new RecordingUnitOfWork();

        var useCase = new EvaluateAiBuilding(
            new StubAiFinanceQueries(withClub ? [new AiClubMoney(ClubId, account.AvailableMinor, state)] : [], rounds),
            finance,
            new FinanceDoubles.RecordingAccountRepository(account),
            new StubStadiums(stadium),
            new StubStructures(snackBar, shop),
            new ClubBuilding(ledger, jobs, audit),
            unit,
            new FixedClock(now),
            new StubRequestContext());

        return new Run(useCase, stadium, ledger, audit, jobs, unit);
    }

    private sealed record Run(
        EvaluateAiBuilding UseCase,
        ClubStadium Stadium,
        FinanceDoubles.RecordingLedgerRepository Ledger,
        FinanceDoubles.RecordingAuditWriter Audit,
        RecordingJobQueue Jobs,
        RecordingUnitOfWork Unit);

    private sealed class StubAiFinanceQueries(IReadOnlyList<AiClubMoney> clubs, int rounds) : IAiFinanceQueries
    {
        public Task<IReadOnlyList<AiClubMoney>> LoadAiClubMoneyAsync(CancellationToken cancellationToken) => Task.FromResult(clubs);

        public Task<int> GetRoundsPublishedAsync(CancellationToken cancellationToken) => Task.FromResult(rounds);
    }

    private sealed class StubStadiums(ClubStadium stadium) : IStadiumRepository
    {
        public void Add(ClubStadium added)
        {
        }

        public Task<ClubStadium?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult<ClubStadium?>(clubId == stadium.ClubId ? stadium : null);
    }

    private sealed class StubStructures(params ClubStructure[] buildings) : IStructureRepository
    {
        public void Add(ClubStructure building)
        {
        }

        public void AddTakings(ClubStructureTakings takings)
        {
        }

        public Task<ClubStructure?> FindAsync(Guid buildingId, CancellationToken cancellationToken) =>
            Task.FromResult(buildings.FirstOrDefault(building => building.Id == buildingId));

        public Task<ClubStructure?> FindByClubAsync(Guid clubId, StructureKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(buildings.FirstOrDefault(building => building.ClubId == clubId && building.Kind == kind));

        public Task<IReadOnlyList<ClubStructure>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubStructure>>([.. buildings.Where(building => building.ClubId == clubId)]);
    }

    private sealed class RecordingJobQueue : IJobQueue
    {
        public List<JobEnqueueRequest> Enqueued { get; } = [];

        public Task<bool> EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken)
        {
            Enqueued.Add(request);

            return Task.FromResult(true);
        }

        public Task<bool> RequeueAsync(string jobType, string businessKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<JobSnapshot?> FindByIdAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<LeasedJob>> ClaimAsync(
            string leaseOwner,
            int maxJobs,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task FailAsync(Guid jobId, string errorMessage, JobFailureKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;

            return Task.FromResult(0);
        }

        public Task<IDatabaseTransaction> BeginTransactionAsync(TransactionIsolation isolation, CancellationToken cancellationToken) =>
            Task.FromResult<IDatabaseTransaction>(new NoOpTransaction());
    }

    private sealed class NoOpTransaction : IDatabaseTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class StubRequestContext : IRequestContext
    {
        public Guid? ActorUserId => null;

        public string CorrelationId { get; } = Guid.CreateVersion7().ToString();

        public string? IpAddress => null;

        public string? UserAgent => null;
    }
}
