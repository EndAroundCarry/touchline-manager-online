using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Ops;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Tests.Competition;

/// <summary>
/// The operator rollover controls: the run-now trigger and the audited resume (ADR-0034).
/// </summary>
public sealed class SeasonRolloverControlsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorldId = Guid.CreateVersion7();
    private static readonly Guid SeasonId = Guid.CreateVersion7();
    private static readonly Guid OperatorId = Guid.CreateVersion7();

    [Fact]
    public async Task A_failed_rollover_is_retried_audited_and_requeued()
    {
        var rollover = FailedRollover();
        var queue = new RecordingJobQueue { RequeueResult = true };
        var audit = new RecordingAuditWriter();

        var result = await CreateResume(rollover, queue, audit)
            .ExecuteAsync(SeasonId, "the drift was reconciled", CancellationToken.None);

        result.Outcome.Should().Be(ResumeSeasonRolloverOutcome.Resumed);
        result.RolloverId.Should().Be(rollover.Id);
        result.BusinessKey.Should().Be(SeasonRolloverJobTypes.RolloverKey(SeasonId));
        result.Requeued.Should().BeTrue();

        rollover.Phase.Should().Be(SeasonRolloverPhase.Started, "the state machine resumes from its first checkpoint");
        rollover.FailureReason.Should().BeNull();

        queue.Requeued.Should().ContainSingle()
            .Which.Should().Be((SeasonRolloverJobTypes.Rollover, SeasonRolloverJobTypes.RolloverKey(SeasonId)));
        queue.Enqueued.Should().BeEmpty("the dead-lettered row was requeued rather than a second job created");

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].Action.Should().Be(WorldAuditActions.RolloverResumed);
        audit.Entries[0].Reason.Should().Be("the drift was reconciled");
        audit.Entries[0].ActorType.Should().Be(AuditActorTypes.User);
        audit.Entries[0].ActorUserId.Should().Be(OperatorId);
        audit.Entries[0].TargetType.Should().Be(AuditTargetTypes.SeasonRollover);
        audit.Entries[0].TargetId.Should().Be(rollover.Id);
    }

    [Fact]
    public async Task A_resume_enqueues_a_job_when_no_dead_lettered_row_exists()
    {
        var rollover = FailedRollover();
        var queue = new RecordingJobQueue { RequeueResult = false };

        var result = await CreateResume(rollover, queue, new RecordingAuditWriter())
            .ExecuteAsync(SeasonId, "no job row survived", CancellationToken.None);

        result.Outcome.Should().Be(ResumeSeasonRolloverOutcome.Resumed);
        result.Requeued.Should().BeFalse();

        queue.Enqueued.Should().ContainSingle();
        queue.Enqueued[0].JobType.Should().Be(SeasonRolloverJobTypes.Rollover);
        queue.Enqueued[0].BusinessKey.Should().Be(SeasonRolloverJobTypes.RolloverKey(SeasonId));
        queue.Enqueued[0].DueAt.Should().Be(Now);
    }

    [Fact]
    public async Task A_completed_rollover_is_not_resumed()
    {
        var rollover = CompletedRollover();
        var queue = new RecordingJobQueue();
        var audit = new RecordingAuditWriter();

        var result = await CreateResume(rollover, queue, audit)
            .ExecuteAsync(SeasonId, "nothing to do", CancellationToken.None);

        result.Outcome.Should().Be(ResumeSeasonRolloverOutcome.AlreadyCompleted);
        result.Requeued.Should().BeFalse();
        queue.Requeued.Should().BeEmpty();
        queue.Enqueued.Should().BeEmpty();
        audit.Entries.Should().BeEmpty("no operator action was taken");
    }

    [Fact]
    public async Task A_rollover_that_has_not_failed_is_left_to_the_queue()
    {
        var rollover = SeasonRollover.Start(Guid.CreateVersion7(), WorldId, SeasonId, Now);
        rollover.Freeze(Now);

        var queue = new RecordingJobQueue();
        var audit = new RecordingAuditWriter();

        var result = await CreateResume(rollover, queue, audit)
            .ExecuteAsync(SeasonId, "premature", CancellationToken.None);

        result.Outcome.Should().Be(ResumeSeasonRolloverOutcome.NotFailed);
        rollover.Phase.Should().Be(SeasonRolloverPhase.Frozen, "it is mid-flight and the queue owns it");
        queue.Requeued.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_rollover_reports_not_found()
    {
        var queue = new RecordingJobQueue();

        var result = await CreateResume(rollover: null, queue, new RecordingAuditWriter())
            .ExecuteAsync(SeasonId, "nothing here", CancellationToken.None);

        result.Outcome.Should().Be(ResumeSeasonRolloverOutcome.NotFound);
        queue.Requeued.Should().BeEmpty();
    }

    [Fact]
    public async Task A_blank_reason_is_refused()
    {
        var act = async () => await CreateResume(FailedRollover(), new RecordingJobQueue(), new RecordingAuditWriter())
            .ExecuteAsync(SeasonId, "   ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Triggering_enqueues_the_real_rollover_job_due_now()
    {
        var queue = new RecordingJobQueue();

        var result = await CreateTrigger(world: World(), queue)
            .ExecuteAsync(seasonId: null, CancellationToken.None);

        result.Outcome.Should().Be(TriggerSeasonRolloverOutcome.Enqueued);
        result.SeasonId.Should().Be(SeasonId);
        result.BusinessKey.Should().Be(SeasonRolloverJobTypes.RolloverKey(SeasonId));
        result.Enqueued.Should().BeTrue();

        queue.Enqueued.Should().ContainSingle();
        queue.Enqueued[0].JobType.Should().Be(SeasonRolloverJobTypes.Rollover);
        queue.Enqueued[0].BusinessKey.Should().Be(SeasonRolloverJobTypes.RolloverKey(SeasonId));
        queue.Enqueued[0].DueAt.Should().Be(Now);
    }

    [Fact]
    public async Task Triggering_an_unknown_season_reports_not_found()
    {
        var queue = new RecordingJobQueue();

        var result = await CreateTrigger(world: World(), queue)
            .ExecuteAsync(Guid.CreateVersion7(), CancellationToken.None);

        result.Outcome.Should().Be(TriggerSeasonRolloverOutcome.SeasonNotFound);
        queue.Enqueued.Should().BeEmpty();
    }

    private static ResumeSeasonRollover CreateResume(
        SeasonRollover? rollover,
        RecordingJobQueue queue,
        RecordingAuditWriter audit) =>
        new(
            new FixedClock(),
            new StubWorldRepository(World(), ClosingSeason()),
            new RecordingRolloverRepository(rollover),
            queue,
            new NoOpAdvisoryLock(),
            audit,
            new StubRequestContext(),
            new RecordingUnitOfWork(),
            NullLogger<ResumeSeasonRollover>.Instance);

    private static TriggerSeasonRollover CreateTrigger(GameWorld world, RecordingJobQueue queue) =>
        new(new StubWorldRepository(world, ClosingSeason()), queue, new FixedClock());

    private static GameWorld World() => GameWorld.Create(WorldId, "Rollover controls", Now);

    private static Season ClosingSeason() =>
        Season.Create(SeasonId, WorldId, sequenceNumber: 1, gameYear: 2026, WorldRuleSet.Version, new DateOnly(2026, 9, 1), Now);

    private static SeasonRollover FailedRollover()
    {
        var rollover = SeasonRollover.Start(Guid.CreateVersion7(), WorldId, SeasonId, Now);
        rollover.Fail("a projection drifted (TBL-13)", Now);

        return rollover;
    }

    private static SeasonRollover CompletedRollover()
    {
        var rollover = SeasonRollover.Start(Guid.CreateVersion7(), WorldId, SeasonId, Now);
        rollover.Freeze(Now);
        rollover.Finalize(Now);
        rollover.SettleSquads(Now);
        rollover.Move(Guid.CreateVersion7(), Now);
        rollover.Complete(Now);

        return rollover;
    }

    private sealed class StubWorldRepository(GameWorld? world, params Season[] seasons) : IWorldRepository
    {
        private readonly List<Season> _seasons = [.. seasons];

        public Task<GameWorld?> FindWorldAsync(CancellationToken cancellationToken) => Task.FromResult(world);

        public Task<Season?> FindSeasonByIdAsync(Guid seasonId, CancellationToken cancellationToken) =>
            Task.FromResult(_seasons.FirstOrDefault(season => season.Id == seasonId));

        public Task<Season?> FindSeasonAsync(Guid worldId, int sequenceNumber, CancellationToken cancellationToken) =>
            Task.FromResult(_seasons.FirstOrDefault(season =>
                season.WorldId == worldId && season.SequenceNumber == sequenceNumber));

        public void AddWorld(GameWorld gameWorld) => throw new NotSupportedException();

        public void AddSeason(Season season) => throw new NotSupportedException();

        public Task<IReadOnlyList<Country>> ListCountriesAsync(Guid worldId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Country?> FindCountryAsync(Guid countryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddCountry(Country country) => throw new NotSupportedException();

        public Task<Division?> FindLowestActiveDivisionAsync(Guid countryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Division?> FindDivisionAsync(Guid divisionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Division?> FindDivisionByTierAsync(
            Guid countryId,
            int tierNumber,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddDivision(Division division) => throw new NotSupportedException();

        public Task<DivisionSeason?> FindDivisionSeasonAsync(
            Guid divisionId,
            Guid seasonId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddDivisionSeason(DivisionSeason divisionSeason) => throw new NotSupportedException();

        public void AddClubSeasonEntry(ClubSeasonEntry entry) => throw new NotSupportedException();

        public Task<IReadOnlyList<Division>> ListActiveDivisionsAsync(
            Guid countryId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DivisionSeason>> ListDivisionSeasonsAsync(
            Guid seasonId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ClubSeasonEntry>> ListClubSeasonEntriesAsync(
            Guid divisionSeasonId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingRolloverRepository(params SeasonRollover?[] rollovers) : ISeasonRolloverRepository
    {
        private readonly List<SeasonRollover> _rollovers = [.. rollovers.OfType<SeasonRollover>()];

        public void Add(SeasonRollover rollover) => _rollovers.Add(rollover);

        public Task<SeasonRollover?> FindBySeasonAsync(
            Guid worldId,
            Guid seasonId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_rollovers.FirstOrDefault(rollover =>
                rollover.WorldId == worldId && rollover.SeasonId == seasonId));

        public Task<SeasonRollover?> FindByIdAsync(Guid rolloverId, CancellationToken cancellationToken) =>
            Task.FromResult(_rollovers.FirstOrDefault(rollover => rollover.Id == rolloverId));
    }

    private sealed class RecordingJobQueue : IJobQueue
    {
        public List<JobEnqueueRequest> Enqueued { get; } = [];

        public List<(string JobType, string BusinessKey)> Requeued { get; } = [];

        public bool RequeueResult { get; init; }

        public Task<bool> EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken)
        {
            Enqueued.Add(request);

            return Task.FromResult(true);
        }

        public Task<bool> RequeueAsync(string jobType, string businessKey, CancellationToken cancellationToken)
        {
            Requeued.Add((jobType, businessKey));

            return Task.FromResult(RequeueResult);
        }

        public Task<JobSnapshot?> FindByIdAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult<JobSnapshot?>(null);

        public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<LeasedJob>> ClaimAsync(
            string leaseOwner,
            int maxJobs,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task FailAsync(
            Guid jobId,
            string errorMessage,
            JobFailureKind kind,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class NoOpAdvisoryLock : IAdvisoryLock
    {
        public Task AcquireAsync(AdvisoryLockKey key, CancellationToken cancellationToken) => Task.CompletedTask;
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
            Task.FromResult<IDatabaseTransaction>(new NoOpTransaction());
    }

    private sealed class NoOpTransaction : IDatabaseTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubRequestContext : IRequestContext
    {
        public Guid? ActorUserId { get; } = OperatorId;

        public string CorrelationId { get; } = Guid.CreateVersion7().ToString();

        public string? IpAddress => null;

        public string? UserAgent => null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = Now;
    }
}
