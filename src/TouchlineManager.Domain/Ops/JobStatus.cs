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
