namespace TouchlineManager.Domain.Ops;

/// <summary>
/// Why a job attempt failed, which decides whether a retry is worth scheduling.
/// </summary>
/// <remarks>
/// Permanent failures are dead-lettered immediately rather than burning through the retry budget
/// on an error that cannot succeed. Transient failures are rescheduled with exponential backoff
/// and jitter. See ADR-0003 and master plan §7.1.
/// </remarks>
public enum JobFailureKind
{
    /// <summary>An infrastructure or contention error. Retry with backoff.</summary>
    Transient = 0,

    /// <summary>A domain error that retrying cannot fix. Dead-letter immediately.</summary>
    Permanent = 1,
}
