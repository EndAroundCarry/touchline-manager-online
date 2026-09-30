namespace TouchlineManager.Domain.Ops;

/// <summary>
/// Lifecycle states of a durable job row in <c>ops.jobs</c>.
/// </summary>
/// <remarks>
/// The state machine is owned by the ops module. Infrastructure only moves rows between
/// these states; it never invents new ones. See ADR-0003.
/// </remarks>
public enum JobStatus
{
    /// <summary>Ready to be claimed once <c>due_at</c> has passed.</summary>
    Pending = 0,

    /// <summary>Claimed by a worker and held under a lease that expires.</summary>
    Leased = 1,

    /// <summary>Finished successfully. Terminal.</summary>
    Completed = 2,

    /// <summary>Failed permanently. Terminal, and surfaces to operations as an alert.</summary>
    DeadLettered = 3,
}

/// <summary>Stable codes for <see cref="JobStatus"/>, as stored in <c>ops.jobs.status</c> (`F-46`, ADR-0003).</summary>
/// <remarks>
/// The queue writes these codes as literals in its SQL, so a change here is a change of the on-disk
/// vocabulary and must move with a migration. The codes are the ones the check constraint
/// <c>ck_jobs_status</c> allows.
/// </remarks>
public static class JobStatuses
{
    /// <summary>The code for <see cref="JobStatus.Pending"/>.</summary>
    public const string PendingCode = "pending";

    /// <summary>The code for <see cref="JobStatus.Leased"/>.</summary>
    public const string LeasedCode = "leased";

    /// <summary>The code for <see cref="JobStatus.Completed"/>.</summary>
    public const string CompletedCode = "completed";

    /// <summary>The code for <see cref="JobStatus.DeadLettered"/>.</summary>
    public const string DeadLetteredCode = "dead_letter";

    /// <summary>The longest code, used to size the storage column.</summary>
    public const int MaxCodeLength = 11;

    /// <summary>Converts a status to its stable code.</summary>
    /// <param name="status">The job status.</param>
    public static string ToCode(this JobStatus status) => status switch
    {
        JobStatus.Pending => PendingCode,
        JobStatus.Leased => LeasedCode,
        JobStatus.Completed => CompletedCode,
        JobStatus.DeadLettered => DeadLetteredCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown job status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    /// <param name="code">The stable code.</param>
    public static JobStatus FromCode(string code) => code switch
    {
        PendingCode => JobStatus.Pending,
        LeasedCode => JobStatus.Leased,
        CompletedCode => JobStatus.Completed,
        DeadLetteredCode => JobStatus.DeadLettered,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown job status code."),
    };

    /// <summary>Parses a stable code, or reports that it is not one of the four.</summary>
    /// <param name="code">The stable code, or null.</param>
    /// <param name="status">The parsed status.</param>
    /// <returns>Whether the code named a status.</returns>
    public static bool TryFromCode(string? code, out JobStatus status)
    {
        switch (code)
        {
            case PendingCode:
                status = JobStatus.Pending;
                return true;
            case LeasedCode:
                status = JobStatus.Leased;
                return true;
            case CompletedCode:
                status = JobStatus.Completed;
                return true;
            case DeadLetteredCode:
                status = JobStatus.DeadLettered;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
