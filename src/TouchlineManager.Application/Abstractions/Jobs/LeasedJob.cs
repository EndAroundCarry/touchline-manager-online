namespace TouchlineManager.Application.Abstractions.Jobs;

/// <summary>
/// A job that has been claimed by this worker and is held under a lease.
/// </summary>
/// <param name="Id">The job row identity.</param>
/// <param name="JobType">The job type, matched against a registered handler.</param>
/// <param name="BusinessKey">The deterministic business identity of the job.</param>
/// <param name="PayloadJson">The handler payload as a JSON document.</param>
/// <param name="AttemptCount">Attempts made so far, including this one.</param>
/// <param name="MaxAttempts">Attempts allowed before dead-lettering.</param>
public sealed record LeasedJob(
    Guid Id,
    string JobType,
    string BusinessKey,
    string PayloadJson,
    int AttemptCount,
    int MaxAttempts);
