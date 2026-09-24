namespace TouchlineManager.Domain.World;

/// <summary>
/// A durable request to create the next tier of a country's pyramid (`PYR-2`, ADR-0005).
/// </summary>
/// <remarks>
/// <para>
/// The request is created inside the takeover transaction, but generation happens later in a worker
/// (ADR-0005). That split is the point: a manager is never charged for a claim that waits on the
/// engine, and a crash mid-generation leaves a resumable request rather than a half-built division.
/// </para>
/// <para>
/// <c>unique (country_id, target_tier)</c> is the primary defence against a duplicate tier (`PYR-3`).
/// It is why the evaluation can safely be attempted from more than one place: the database, not the
/// caller's ordering, decides that a tier is created once.
/// </para>
/// </remarks>
public sealed class DivisionProvisioningRequest
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private DivisionProvisioningRequest()
    {
    }

    /// <summary>Gets the request identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the country whose pyramid is growing.</summary>
    public Guid CountryId { get; private set; }

    /// <summary>Gets the tier to create, always one above the tier that filled up.</summary>
    public int TargetTier { get; private set; }

    /// <summary>
    /// Gets the season the new tier belongs to.
    /// </summary>
    /// <remarks>
    /// Not necessarily the season that was running when the request was made: if rollover held the
    /// country lock, the request targets the next season so the closing one is never mutated
    /// (`PYR-9`).
    /// </remarks>
    public Guid TargetSeasonId { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public ProvisioningRequestStatus Status { get; private set; }

    /// <summary>Gets the seed that makes the generated tier reproducible (`PYR-14`).</summary>
    public string GenerationSeed { get; private set; } = string.Empty;

    /// <summary>Gets when the request was recorded.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Gets when a worker started generating.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Gets when generation finished, successfully or not.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Gets the failure diagnostics, when generation failed. Never shown to players.</summary>
    public string? FailureDiagnostics { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets a value indicating whether the tier is still coming.</summary>
    public bool IsPending => ProvisioningRequestStatusRules.IsPending(Status);

    /// <summary>Records a request to create tier <paramref name="targetTier"/>.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="countryId">The country whose pyramid is growing.</param>
    /// <param name="targetTier">The tier to create.</param>
    /// <param name="targetSeasonId">The season the new tier plays in.</param>
    /// <param name="generationSeed">The seed that makes the tier reproducible.</param>
    /// <param name="now">The current instant.</param>
    public static DivisionProvisioningRequest Request(
        Guid id,
        Guid countryId,
        int targetTier,
        Guid targetSeasonId,
        string generationSeed,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generationSeed);

        if (targetTier < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetTier),
                targetTier,
                "Only a tier above the first is ever provisioned; tier 1 is seeded (PYR-2).");
        }

        return new DivisionProvisioningRequest
        {
            Id = id,
            CountryId = countryId,
            TargetTier = targetTier,
            TargetSeasonId = targetSeasonId,
            Status = ProvisioningRequestStatus.Requested,
            GenerationSeed = generationSeed,
            RequestedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Claims the request for a worker.</summary>
    /// <param name="now">The current instant.</param>
    public void Start(DateTimeOffset now)
    {
        if (Status != ProvisioningRequestStatus.Requested)
        {
            throw new InvalidOperationException(
                $"Only a requested provision can start, not one in state '{Status}'.");
        }

        Status = ProvisioningRequestStatus.Running;
        StartedAt = now;

        Touch(now);
    }

    /// <summary>Marks generation, validation, and backfill complete. The tier becomes claimable (`PYR-8`).</summary>
    /// <param name="now">The current instant.</param>
    public void Complete(DateTimeOffset now)
    {
        if (Status is not (ProvisioningRequestStatus.Running or ProvisioningRequestStatus.Requested))
        {
            return;
        }

        Status = ProvisioningRequestStatus.Completed;
        CompletedAt = now;
        FailureDiagnostics = null;

        Touch(now);
    }

    /// <summary>Marks the request failed. The tier is not claimable and needs an operator to retry.</summary>
    /// <param name="diagnostics">What went wrong. Retained for operations, never exposed to managers.</param>
    /// <param name="now">The current instant.</param>
    public void Fail(string diagnostics, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostics);

        Status = ProvisioningRequestStatus.Failed;
        CompletedAt = now;
        FailureDiagnostics = diagnostics;

        Touch(now);
    }

    /// <summary>
    /// Returns a failed request to the queue for another attempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retrying reuses this row rather than creating a new one, and that is the point:
    /// <c>unique (country_id, target_tier)</c> means a second request for the same tier cannot exist, so
    /// without this transition a single failed run would block the tier permanently. It also preserves
    /// the recorded seed, which is what `PYR-14` requires — two attempts at the same tier must be
    /// attempting the same world, not two different ones.
    /// </para>
    /// <para>
    /// Deliberately not automatic. A failure means generation or validation found something wrong, and
    /// re-running it in a loop would turn a diagnosable fault into a hot loop against the database.
    /// </para>
    /// </remarks>
    /// <param name="now">The current instant.</param>
    public void Retry(DateTimeOffset now)
    {
        if (Status != ProvisioningRequestStatus.Failed)
        {
            throw new InvalidOperationException($"Only a failed request can be retried, not one in state '{Status}'.");
        }

        Status = ProvisioningRequestStatus.Requested;
        StartedAt = null;
        CompletedAt = null;
        FailureDiagnostics = null;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
