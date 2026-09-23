namespace TouchlineManager.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence model for a durable job row in <c>ops.jobs</c>.
/// </summary>
/// <remarks>
/// This is the ops module's storage shape, not a domain aggregate. State transitions are
/// performed by <c>PostgresJobQueue</c>; see ADR-0003.
/// </remarks>
public sealed class OpsJob
{
    /// <summary>Gets or sets the job identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the job type, matched against a registered handler.</summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>Gets or sets the deterministic business identity of the job.</summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the handler payload as a JSON document.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Gets or sets the instant from which the job becomes claimable.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Gets or sets the claim priority. Lower values are claimed first.</summary>
    public short Priority { get; set; }

    /// <summary>Gets or sets the lifecycle state.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Gets or sets the number of attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Gets or sets the attempt ceiling before dead-lettering.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>Gets or sets the worker currently holding the lease.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets when the current lease expires and the job becomes claimable again.</summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>Gets or sets the last recorded failure, for operations.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets when the job completed successfully.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency version.</summary>
    public long Version { get; set; }
}
