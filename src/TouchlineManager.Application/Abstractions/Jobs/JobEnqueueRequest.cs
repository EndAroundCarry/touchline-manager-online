namespace TouchlineManager.Application.Abstractions.Jobs;

/// <summary>
/// A request to enqueue a durable job.
/// </summary>
/// <remarks>
/// <see cref="JobType"/> plus <see cref="BusinessKey"/> form the enqueue idempotency identity
/// (ADR-0003). The business key must be derived from domain identity
/// (<c>fixture:{id}:lock</c>), never from a timestamp or a random value, or re-running a use
/// case would queue a duplicate business action.
/// </remarks>
public sealed record JobEnqueueRequest
{
    /// <summary>Gets the job type, matched against an <see cref="IJobHandler.JobType"/>.</summary>
    public required string JobType { get; init; }

    /// <summary>Gets the deterministic business identity of this job.</summary>
    public required string BusinessKey { get; init; }

    /// <summary>Gets the instant from which the job becomes claimable.</summary>
    public required DateTimeOffset DueAt { get; init; }

    /// <summary>Gets the handler payload as a JSON document. Defaults to an empty object.</summary>
    public string PayloadJson { get; init; } = "{}";

    /// <summary>Gets the claim priority. Lower values are claimed first.</summary>
    public short Priority { get; init; }

    /// <summary>Gets the number of attempts allowed before the job is dead-lettered.</summary>
    public int MaxAttempts { get; init; } = TouchlineManager.Domain.Ops.JobRetryPolicy.DefaultMaxAttempts;
}
