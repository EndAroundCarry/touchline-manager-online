namespace TouchlineManager.Domain.Competition;

/// <summary>
/// One tier's instance in one season: the thing that holds a table, a schedule, and a membership.
/// </summary>
/// <remarks>
/// <para>
/// The schedule and tie-draw seeds live here rather than on the division because both are per-season
/// facts. `CAL-8` requires a generated schedule to be reproducible from its seed, and `TBL-11`
/// requires the final tie-break draw to be generated <em>before</em> the season and stored, so a
/// season's ordering can always be explained after the fact.
/// </para>
/// <para>
/// Assignment of clubs to this instance is immutable history (`PR-6`). Promotion and relegation create
/// the <em>next</em> season's entries; they never move a club between divisions inside a finished one.
/// </para>
/// </remarks>
public sealed class DivisionSeason
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private DivisionSeason()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the division (the durable tier).</summary>
    public Guid DivisionId { get; private set; }

    /// <summary>Gets the season.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public DivisionSeasonStatus Status { get; private set; }

    /// <summary>Gets the seed the fixture list is reproducible from (`CAL-8`).</summary>
    public string ScheduleSeed { get; private set; } = string.Empty;

    /// <summary>Gets the seed the final tie-break draw is derived from (`TBL-11`).</summary>
    public string TieDrawSeed { get; private set; } = string.Empty;

    /// <summary>Gets the published hash of the tie-break draw, so it cannot be changed unnoticed.</summary>
    public string TieDrawHash { get; private set; } = string.Empty;

    /// <summary>Gets when the standings were finalised, which is the point they stop changing (`PR-4`).</summary>
    public DateTimeOffset? StandingsFinalizedAt { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Creates the instance for a division in a season.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="divisionId">The durable tier.</param>
    /// <param name="seasonId">The season.</param>
    /// <param name="scheduleSeed">The schedule seed.</param>
    /// <param name="tieDrawSeed">The tie-break draw seed.</param>
    /// <param name="tieDrawHash">The published hash of the tie-break draw.</param>
    /// <param name="now">The current instant.</param>
    public static DivisionSeason Create(
        Guid id,
        Guid divisionId,
        Guid seasonId,
        string scheduleSeed,
        string tieDrawSeed,
        string tieDrawHash,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleSeed);
        ArgumentException.ThrowIfNullOrWhiteSpace(tieDrawSeed);
        ArgumentException.ThrowIfNullOrWhiteSpace(tieDrawHash);

        return new DivisionSeason
        {
            Id = id,
            DivisionId = divisionId,
            SeasonId = seasonId,
            Status = DivisionSeasonStatus.Scheduled,
            ScheduleSeed = scheduleSeed,
            TieDrawSeed = tieDrawSeed,
            TieDrawHash = tieDrawHash,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Opens the instance for play.</summary>
    /// <param name="now">The current instant.</param>
    public void Activate(DateTimeOffset now)
    {
        if (Status != DivisionSeasonStatus.Scheduled)
        {
            throw new InvalidOperationException(
                $"Only a scheduled division-season can start, not one in state '{Status}'.");
        }

        Status = DivisionSeasonStatus.Active;

        Touch(now);
    }

    /// <summary>Freezes the standings for rollover. After this they are history (`PR-4`, `PR-6`).</summary>
    /// <param name="now">The current instant.</param>
    public void Complete(DateTimeOffset now)
    {
        if (Status == DivisionSeasonStatus.Completed)
        {
            return;
        }

        Status = DivisionSeasonStatus.Completed;
        StandingsFinalizedAt = now;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
