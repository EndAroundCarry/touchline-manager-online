using System.Diagnostics.CodeAnalysis;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Application.Abstractions.Jobs;

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
