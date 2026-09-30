using System.Diagnostics.CodeAnalysis;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Application.Abstractions.Jobs;

/// <summary>One job row, as an operator command addresses it by identity (`F-46`, ADR-0044).</summary>
/// <remarks>
/// The type and business key are what a retry needs to reach the row through
/// <see cref="IJobQueue.RequeueAsync"/>, and the status is what tells a retryable dead letter from a job
/// the queue already owns.
/// </remarks>
/// <param name="Id">The job's identity.</param>
/// <param name="JobType">The registered job type.</param>
/// <param name="BusinessKey">The deterministic key that makes the enqueue idempotent.</param>
/// <param name="Status">The lifecycle state.</param>
public sealed record JobSnapshot(Guid Id, string JobType, string BusinessKey, JobStatus Status);

/// <summary>
/// The durable job queue. Implemented by <c>PostgresJobQueue</c> (ADR-0003).
/// </summary>
/// <remarks>
/// Delivery is at-least-once. Every handler must therefore be idempotent on its business key
/// <em>and</em> protected by destination-table uniqueness. Idempotency is verified by tests,
/// never assumed.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "This genuinely is a queue. CA1711 exists to stop non-collection types from being named like collections; this port has producer/consumer semantics with claim, lease, and terminal states, and 'queue' is the term the runbooks and operations tooling use.")]
public interface IJobQueue
{
    /// <summary>
    /// Enqueues a job, or does nothing if a job with the same type and business key already
    /// exists. Safe to call inside a domain transaction.
    /// </summary>
    /// <returns><see langword="true"/> when a new row was inserted; <see langword="false"/> when it already existed.</returns>
    Task<bool> EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a dead-lettered job to the queue for another attempt, on an operator's authority.
    /// </summary>
    /// <remarks>
    /// Only a <c>dead_letter</c> row is acted on, and the attempt budget is reset, so an operator-forced
    /// retry is a clean attempt rather than a continuation of the exhausted one. A leased, pending, or
    /// completed job is left untouched — the queue already owns it, or it is already done (ADR-0003,
    /// ADR-0031).
    /// </remarks>
    /// <param name="jobType">The job type.</param>
    /// <param name="businessKey">The job's business key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when a dead-lettered row was reset; <see langword="false"/> otherwise.</returns>
    Task<bool> RequeueAsync(string jobType, string businessKey, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one job row by identity, for an operator command that addresses it (`F-46`, ADR-0044).
    /// </summary>
    /// <param name="jobId">The job's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The row, or <see langword="null"/> when no job has that identity.</returns>
    Task<JobSnapshot?> FindByIdAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Stops a job on an operator's authority (`F-46`, ADR-0044).
    /// </summary>
    /// <remarks>
    /// A <c>pending</c>, <c>leased</c>, or <c>dead_letter</c> row becomes <c>cancelled</c> (terminal), and
    /// its lease is cleared. A <c>completed</c> or already-<c>cancelled</c> row is left untouched: there is
    /// nothing left to stop. Cancelling a leased job is safe because the worker's own terminal statements
    /// are guarded by <c>status = 'leased'</c>, so a cancelled job can never also complete.
    /// </remarks>
    /// <param name="jobId">The job's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when a row was cancelled; <see langword="false"/> otherwise.</returns>
    Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Claims up to <paramref name="maxJobs"/> ready jobs, including jobs whose lease has
    /// expired, using <c>FOR UPDATE SKIP LOCKED</c> so concurrent workers never block on each
    /// other.
    /// </summary>
    Task<IReadOnlyList<LeasedJob>> ClaimAsync(
        string leaseOwner,
        int maxJobs,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>Marks a claimed job completed. Terminal.</summary>
    Task CompleteAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a failed attempt. Transient failures are rescheduled with exponential backoff and
    /// jitter; permanent failures, and attempts that have exhausted the budget, are dead-lettered.
    /// </summary>
    Task FailAsync(
        Guid jobId,
        string errorMessage,
        JobFailureKind kind,
        CancellationToken cancellationToken);
}
