namespace TouchlineManager.Domain.World;

/// <summary>
/// A time-bounded period during which a manager controls a club (`WORLD-7`).
/// </summary>
/// <remarks>
/// <para>
/// The tenure is the whole of the ownership model. A club has no manager column; "who controls this
/// club" is a query over open tenures, and "how many humans are in this tier" is a count over the same
/// rows. That is what makes the partial unique indexes meaningful — one open tenure per club and one
/// open tenure per manager are database facts, not application conventions (`OCC-9`).
/// </para>
/// <para>
/// Closing a tenure never rewinds club state (`OCC-5`): the club keeps its squad, contracts, cash,
/// fixtures, and history, because none of those are stored here.
/// </para>
/// </remarks>
public sealed class ClubTenure
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubTenure()
    {
    }

    /// <summary>Gets the tenure identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the controlled club.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the controlling manager.</summary>
    public Guid ManagerId { get; private set; }

    /// <summary>Gets when control began.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets when control ended, or <see langword="null"/> while the tenure is open.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Gets why control ended, one of <see cref="ClubTenureEndReasons"/>.</summary>
    public string? EndReason { get; private set; }

    /// <summary>Gets when the manager was last seen. Drives the inactivity ladder (`OCC-1`–`OCC-3`).</summary>
    public DateTimeOffset LastActiveAt { get; private set; }

    /// <summary>Gets the control state.</summary>
    public ClubTenureControlStatus ControlStatus { get; private set; }

    /// <summary>
    /// Gets the caller-supplied idempotency key that created this tenure, so a retried claim cannot
    /// produce a second one (`CONC-3`, master plan §7.6).
    /// </summary>
    public string TakeoverIdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets a value indicating whether the tenure is open, so the club is taken.</summary>
    public bool IsOpen => ClubTenureControlStatusRules.IsOpen(ControlStatus);

    /// <summary>Gets a value indicating whether this tenure counts towards human occupancy (`OCC-8`).</summary>
    public bool OccupiesCapacity => ClubTenureControlStatusRules.OccupiesCapacity(ControlStatus);

    /// <summary>Opens a tenure for a manager taking over a club.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club being taken over.</param>
    /// <param name="managerId">The manager taking it over.</param>
    /// <param name="takeoverIdempotencyKey">The key that makes a retried takeover a no-op.</param>
    /// <param name="now">The current instant.</param>
    public static ClubTenure Start(
        Guid id,
        Guid clubId,
        Guid managerId,
        string takeoverIdempotencyKey,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(takeoverIdempotencyKey);

        return new ClubTenure
        {
            Id = id,
            ClubId = clubId,
            ManagerId = managerId,
            StartedAt = now,
            LastActiveAt = now,
            ControlStatus = ClubTenureControlStatus.Active,
            TakeoverIdempotencyKey = takeoverIdempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Records that the manager was active, which restarts the inactivity ladder.</summary>
    /// <param name="now">The current instant.</param>
    public void RecordActivity(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return;
        }

        LastActiveAt = now;

        Touch(now);
    }

    /// <summary>
    /// Hands routine decisions to the AI after the inactivity threshold, without closing the tenure,
    /// so the manager may resume by logging in (`OCC-2`).
    /// </summary>
    /// <param name="now">The current instant.</param>
    public void MarkInactive(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("A closed tenure cannot become inactive.");
        }

        ControlStatus = ClubTenureControlStatus.Inactive;

        Touch(now);
    }

    /// <summary>Restores full control after an inactive period, on the manager's return (`OCC-2`).</summary>
    /// <param name="now">The current instant.</param>
    public void Resume(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("A closed tenure cannot be resumed.");
        }

        ControlStatus = ClubTenureControlStatus.Active;
        LastActiveAt = now;

        Touch(now);
    }

    /// <summary>
    /// Ends the tenure. The club returns to full AI control; nothing about the club resets (`OCC-5`).
    /// </summary>
    /// <param name="reason">Why it ended, one of <see cref="ClubTenureEndReasons"/>.</param>
    /// <param name="now">The current instant.</param>
    public void Close(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!ClubTenureEndReasons.IsKnown(reason))
        {
            throw new ArgumentException($"'{reason}' is not a known tenure end reason.", nameof(reason));
        }

        if (!IsOpen)
        {
            return;
        }

        ControlStatus = ClubTenureControlStatus.Closed;
        EndedAt = now;
        EndReason = reason;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
