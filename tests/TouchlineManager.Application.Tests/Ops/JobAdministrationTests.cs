using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Ops;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Application.Tests.Ops;

/// <summary>
/// The operator recovery commands over a single job: retry and cancel (master plan §10.8, `F-46`, ADR-0044).
/// </summary>
public sealed class JobAdministrationTests
{
    private static readonly Guid OperatorId = Guid.CreateVersion7();

    [Fact]
    public async Task A_dead_lettered_job_is_retried_on_a_clean_budget_and_audited()
    {
        var job = Job(JobStatus.DeadLettered);
        var queue = new RecordingJobQueue { Job = job, RequeueResult = true };
        var audit = new RecordingAuditWriter();

        var result = await CreateRetry(queue, audit)
            .ExecuteAsync(job.Id, "the projection drift was reconciled", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.Applied);
        result.JobId.Should().Be(job.Id);
        result.Status.Should().Be(JobStatuses.PendingCode);

        queue.Requeued.Should().ContainSingle().Which.Should().Be((job.JobType, job.BusinessKey));

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].Action.Should().Be(AdminAuditActions.JobRetried);
        audit.Entries[0].Reason.Should().Be("the projection drift was reconciled");
        audit.Entries[0].ActorType.Should().Be(AuditActorTypes.User);
        audit.Entries[0].ActorUserId.Should().Be(OperatorId);
        audit.Entries[0].TargetType.Should().Be(AuditTargetTypes.Job);
        audit.Entries[0].TargetId.Should().Be(job.Id);
        audit.Entries[0].IpHash.Should().BeNull("the operator's address is not needed to retry a job");
    }

    [Fact]
    public async Task A_job_the_queue_still_owns_is_not_retried()
    {
        var job = Job(JobStatus.Pending);
        var queue = new RecordingJobQueue { Job = job };
        var audit = new RecordingAuditWriter();

        var result = await CreateRetry(queue, audit)
            .ExecuteAsync(job.Id, "premature", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.StateUnchanged);
        result.Status.Should().Be(JobStatuses.PendingCode);
        queue.Requeued.Should().BeEmpty("only a dead-lettered job is the operator's to reset");
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_retry_that_races_the_queue_changes_nothing()
    {
        // The row was dead-lettered when it was read, but a resume elsewhere reached it first.
        var job = Job(JobStatus.DeadLettered);
        var queue = new RecordingJobQueue { Job = job, RequeueResult = false };
        var audit = new RecordingAuditWriter();

        var result = await CreateRetry(queue, audit)
            .ExecuteAsync(job.Id, "racing a resume", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.StateUnchanged);
        audit.Entries.Should().BeEmpty("nothing changed, so nothing is audited");
    }

    [Fact]
    public async Task An_unknown_job_cannot_be_retried()
    {
        var queue = new RecordingJobQueue { Job = null };
        var audit = new RecordingAuditWriter();

        var result = await CreateRetry(queue, audit)
            .ExecuteAsync(Guid.CreateVersion7(), "no such job", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.NotFound);
        queue.Requeued.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_job_cannot_be_cancelled()
    {
        var queue = new RecordingJobQueue { Job = null };
        var audit = new RecordingAuditWriter();

        var result = await CreateCancel(queue, audit)
            .ExecuteAsync(Guid.CreateVersion7(), "no such job", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.NotFound);
        queue.Cancelled.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pending_job_is_cancelled_and_audited()
    {
        var job = Job(JobStatus.Pending);
        var queue = new RecordingJobQueue { Job = job, CancelResult = true };
        var audit = new RecordingAuditWriter();

        var result = await CreateCancel(queue, audit)
            .ExecuteAsync(job.Id, "the round was voided by hand", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.Applied);
        result.Status.Should().Be(JobStatuses.CancelledCode);

        queue.Cancelled.Should().ContainSingle().Which.Should().Be(job.Id);

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].Action.Should().Be(AdminAuditActions.JobCancelled);
        audit.Entries[0].Reason.Should().Be("the round was voided by hand");
        audit.Entries[0].TargetType.Should().Be(AuditTargetTypes.Job);
        audit.Entries[0].TargetId.Should().Be(job.Id);
    }

    [Fact]
    public async Task A_completed_job_cannot_be_cancelled()
    {
        var job = Job(JobStatus.Completed);
        var queue = new RecordingJobQueue { Job = job };
        var audit = new RecordingAuditWriter();

        var result = await CreateCancel(queue, audit)
            .ExecuteAsync(job.Id, "too late", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.StateUnchanged);
        result.Status.Should().Be(JobStatuses.CompletedCode);
        queue.Cancelled.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task An_already_cancelled_job_cannot_be_cancelled_again()
    {
        var job = Job(JobStatus.Cancelled);
        var queue = new RecordingJobQueue { Job = job };
        var audit = new RecordingAuditWriter();

        var result = await CreateCancel(queue, audit)
            .ExecuteAsync(job.Id, "again", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.StateUnchanged);
        result.Status.Should().Be(JobStatuses.CancelledCode);
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancel_that_races_the_worker_changes_nothing()
    {
        // The job was claimed between the read and the cancel; the worker's terminal statement wins.
        var job = Job(JobStatus.Leased);
        var queue = new RecordingJobQueue { Job = job, CancelResult = false };
        var audit = new RecordingAuditWriter();

        var result = await CreateCancel(queue, audit)
            .ExecuteAsync(job.Id, "racing a worker", CancellationToken.None);

        result.Outcome.Should().Be(JobAdministrationOutcome.StateUnchanged);
        audit.Entries.Should().BeEmpty("nothing changed, so nothing is audited");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        var job = Job(JobStatus.DeadLettered);

        var retry = async () => await CreateRetry(new RecordingJobQueue { Job = job }, new RecordingAuditWriter())
            .ExecuteAsync(job.Id, reason, CancellationToken.None);

        var cancel = async () => await CreateCancel(new RecordingJobQueue { Job = job }, new RecordingAuditWriter())
            .ExecuteAsync(job.Id, reason, CancellationToken.None);

        await retry.Should().ThrowAsync<ArgumentException>();
        await cancel.Should().ThrowAsync<ArgumentException>();
    }

    private static RetryJob CreateRetry(RecordingJobQueue queue, RecordingAuditWriter audit) =>
        new(queue, audit, new StubRequestContext(), new RecordingUnitOfWork(), NullLogger<RetryJob>.Instance);

    private static CancelJob CreateCancel(RecordingJobQueue queue, RecordingAuditWriter audit) =>
        new(queue, audit, new StubRequestContext(), new RecordingUnitOfWork(), NullLogger<CancelJob>.Instance);

    private static JobSnapshot Job(JobStatus status) =>
        new(Guid.CreateVersion7(), "competition.resolve-matchday", $"matchday:{Guid.CreateVersion7():D}:resolve", status);

    private sealed class RecordingJobQueue : IJobQueue
    {
        public JobSnapshot? Job { get; init; }

        public bool RequeueResult { get; init; } = true;

        public bool CancelResult { get; init; } = true;

        public List<(string JobType, string BusinessKey)> Requeued { get; } = [];

        public List<Guid> Cancelled { get; } = [];

        public Task<JobSnapshot?> FindByIdAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(Job);

        public Task<bool> RequeueAsync(string jobType, string businessKey, CancellationToken cancellationToken)
        {
            Requeued.Add((jobType, businessKey));

            return Task.FromResult(RequeueResult);
        }

        public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken)
        {
            Cancelled.Add(jobId);

            return Task.FromResult(CancelResult);
        }

        public Task<bool> EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LeasedJob>> ClaimAsync(
            string leaseOwner,
            int maxJobs,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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

    private sealed class StubRequestContext : IRequestContext
    {
        public Guid? ActorUserId { get; } = OperatorId;

        public string CorrelationId { get; } = Guid.CreateVersion7().ToString();

        public string? IpAddress => null;

        public string? UserAgent => null;
    }
}
