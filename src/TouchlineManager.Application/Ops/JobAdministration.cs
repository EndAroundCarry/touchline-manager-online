using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Application.Ops;

/// <summary>What happened when an operator acted on a durable job.</summary>
public enum JobAdministrationOutcome
{
    /// <summary>The job was returned to the queue, or cancelled.</summary>
    Applied = 0,

    /// <summary>No job exists with that identity.</summary>
    NotFound = 1,

    /// <summary>The job is not in a state the action applies to, so nothing changed.</summary>
    StateUnchanged = 2,
}

/// <summary>The result of an operator action on a job.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="JobId">The job that was addressed.</param>
/// <param name="Status">The job's state as a stable code, when one is known.</param>
public sealed record JobAdministrationResult(
    JobAdministrationOutcome Outcome,
    Guid JobId,
    string? Status = null);

/// <summary>
/// Returns a dead-lettered job to the queue on an operator's authority (`F-46`, §10.8, ADR-0044).
/// </summary>
/// <remarks>
/// <para>
/// The queue cannot re-run a dead-lettered job, and the operator console's answer to a dead letter is this
/// action: it resets the row with a fresh attempt budget through <see cref="IJobQueue.RequeueAsync"/>, the
/// same primitive the rollover resume uses, and records who did it and why.
/// </para>
/// <para>
/// Only a dead-lettered job is retried. A pending, leased, or completed job is left where it is — the queue
/// already owns it, or it is done — and the caller is told the state did not change.
/// </para>
/// </remarks>
public sealed partial class RetryJob
{
    private readonly IJobQueue _jobs;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RetryJob> _logger;

    /// <summary>Initializes the use case.</summary>
    public RetryJob(
        IJobQueue jobs,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        ILogger<RetryJob> logger)
    {
        _jobs = jobs;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Returns a dead-lettered job to the queue.</summary>
    /// <param name="jobId">The job to retry.</param>
    /// <param name="reason">Why the operator is retrying it. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JobAdministrationResult> ExecuteAsync(
        Guid jobId,
        string reason,
        CancellationToken cancellationToken)
    {
        AccountAdministration.ValidateReason(reason);

        var job = await _jobs.FindByIdAsync(jobId, cancellationToken);

        if (job is null)
        {
            return new JobAdministrationResult(JobAdministrationOutcome.NotFound, jobId);
        }

        if (job.Status != JobStatus.DeadLettered)
        {
            return new JobAdministrationResult(
                JobAdministrationOutcome.StateUnchanged,
                jobId,
                job.Status.ToCode());
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        if (!await _jobs.RequeueAsync(job.JobType, job.BusinessKey, cancellationToken))
        {
            // The row changed between the read and the requeue (it was claimed, or completed). Nothing to do.
            await transaction.RollbackAsync(cancellationToken);

            return new JobAdministrationResult(
                JobAdministrationOutcome.StateUnchanged,
                jobId,
                job.Status.ToCode());
        }

        _audit.Record(new AuditEntry(
            AdminAuditActions.JobRetried,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.Job,
            jobId,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogRetried(jobId, job.JobType);

        return new JobAdministrationResult(
            JobAdministrationOutcome.Applied,
            jobId,
            JobStatus.Pending.ToCode());
    }

    [LoggerMessage(
        EventId = 5230,
        Level = LogLevel.Warning,
        Message = "An operator retried the dead-lettered job {JobId} of type {JobType} (F-46, ADR-0044).")]
    private partial void LogRetried(Guid jobId, string jobType);
}

/// <summary>
/// Stops a stuck job on an operator's authority (`F-46`, §10.8, ADR-0044).
/// </summary>
/// <remarks>
/// A job the queue still owns — pending, leased, or dead-lettered — becomes <c>cancelled</c>, a terminal
/// state that neither the worker nor a materialiser will touch again. A completed or already-cancelled job
/// has nothing left to stop, so the state is reported unchanged.
/// </remarks>
public sealed partial class CancelJob
{
    private readonly IJobQueue _jobs;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelJob> _logger;

    /// <summary>Initializes the use case.</summary>
    public CancelJob(
        IJobQueue jobs,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        ILogger<CancelJob> logger)
    {
        _jobs = jobs;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Cancels the job.</summary>
    /// <param name="jobId">The job to cancel.</param>
    /// <param name="reason">Why the operator is cancelling it. Required, and stored in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JobAdministrationResult> ExecuteAsync(
        Guid jobId,
        string reason,
        CancellationToken cancellationToken)
    {
        AccountAdministration.ValidateReason(reason);

        var job = await _jobs.FindByIdAsync(jobId, cancellationToken);

        if (job is null)
        {
            return new JobAdministrationResult(JobAdministrationOutcome.NotFound, jobId);
        }

        if (job.Status is JobStatus.Completed or JobStatus.Cancelled)
        {
            return new JobAdministrationResult(
                JobAdministrationOutcome.StateUnchanged,
                jobId,
                job.Status.ToCode());
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        if (!await _jobs.CancelAsync(jobId, cancellationToken))
        {
            // The worker reached a terminal state between the read and the cancel. Its result stands.
            await transaction.RollbackAsync(cancellationToken);

            return new JobAdministrationResult(
                JobAdministrationOutcome.StateUnchanged,
                jobId,
                job.Status.ToCode());
        }

        _audit.Record(new AuditEntry(
            AdminAuditActions.JobCancelled,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.Job,
            jobId,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogCancelled(jobId, job.JobType);

        return new JobAdministrationResult(
            JobAdministrationOutcome.Applied,
            jobId,
            JobStatus.Cancelled.ToCode());
    }

    [LoggerMessage(
        EventId = 5231,
        Level = LogLevel.Warning,
        Message = "An operator cancelled the job {JobId} of type {JobType} (F-46, ADR-0044).")]
    private partial void LogCancelled(Guid jobId, string jobType);
}
