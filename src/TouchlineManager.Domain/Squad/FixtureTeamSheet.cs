namespace TouchlineManager.Domain.Squad;

/// <summary>The lifecycle state of a fixture team sheet (`CAL-3`, master plan §6.5).</summary>
public enum TeamSheetStatus
{
    /// <summary>Still editable by the manager.</summary>
    Draft = 0,

    /// <summary>Frozen at the lock deadline. Later writes affect later fixtures only (`SQ-7`).</summary>
    Locked = 1,
}

/// <summary>Stable codes and storage representation for <see cref="TeamSheetStatus"/>.</summary>
public static class TeamSheetStatuses
{
    /// <summary>The code for <see cref="TeamSheetStatus.Draft"/>.</summary>
    public const string DraftCode = "draft";

    /// <summary>The code for <see cref="TeamSheetStatus.Locked"/>.</summary>
    public const string LockedCode = "locked";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 6;

    /// <summary>Converts a status to its stable code.</summary>
    /// <param name="status">The team-sheet status.</param>
    public static string ToCode(this TeamSheetStatus status) => status switch
    {
        TeamSheetStatus.Draft => DraftCode,
        TeamSheetStatus.Locked => LockedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown team-sheet status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    /// <param name="code">The stable code.</param>
    public static TeamSheetStatus FromCode(string code) => code switch
    {
        DraftCode => TeamSheetStatus.Draft,
        LockedCode => TeamSheetStatus.Locked,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown team-sheet status code."),
    };
}

/// <summary>
/// A club's selection for one specific fixture (`SQ-4`, master plan §6.5).
/// </summary>
/// <remarks>
/// <para>
/// The sheet references the <em>version</em> of the plan it was built from, not just the plan, because a
/// prepared fixture must stay interpretable after the manager saves a new plan version
/// (`data-model.md` §3.2).
/// </para>
/// <para>
/// <see cref="FixtureId"/> carries no foreign key yet: `competition.fixtures` does not exist until
/// Stage 6, which adds it. Stage 4 ships the shell so the fixture-independent editing work has
/// somewhere to land, exactly as Stage 3 shipped the competition and finance shells ahead of their
/// stages (ADR-0011).
/// </para>
/// </remarks>
public sealed class FixtureTeamSheet
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private FixtureTeamSheet()
    {
    }

    /// <summary>Gets the sheet identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the fixture the sheet is for. Foreign key arrives with Stage 6.</summary>
    public Guid FixtureId { get; private set; }

    /// <summary>Gets the club the sheet selects for.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the plan the sheet was built from.</summary>
    public Guid TacticalPlanId { get; private set; }

    /// <summary>Gets the plan version the sheet was built from.</summary>
    public long TacticalPlanVersion { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public TeamSheetStatus Status { get; private set; }

    /// <summary>Gets when the sheet was frozen, if it has been.</summary>
    public DateTimeOffset? LockedAt { get; private set; }

    /// <summary>Gets when the sheet was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the sheet was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets whether the sheet can still be edited.</summary>
    public bool IsEditable => Status == TeamSheetStatus.Draft;

    /// <summary>Opens a draft sheet for a fixture.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="fixtureId">The fixture the sheet is for.</param>
    /// <param name="clubId">The club the sheet selects for.</param>
    /// <param name="tacticalPlanId">The plan the sheet is built from.</param>
    /// <param name="tacticalPlanVersion">The plan version the sheet is built from.</param>
    /// <param name="now">The current instant.</param>
    public static FixtureTeamSheet Draft(
        Guid id,
        Guid fixtureId,
        Guid clubId,
        Guid tacticalPlanId,
        long tacticalPlanVersion,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tacticalPlanVersion);

        return new FixtureTeamSheet
        {
            Id = id,
            FixtureId = fixtureId,
            ClubId = clubId,
            TacticalPlanId = tacticalPlanId,
            TacticalPlanVersion = tacticalPlanVersion,
            Status = TeamSheetStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Rebuilds the draft against a newer plan version.</summary>
    /// <param name="tacticalPlanId">The plan the sheet is built from.</param>
    /// <param name="tacticalPlanVersion">The plan version.</param>
    /// <param name="now">The current instant.</param>
    public void Rebase(Guid tacticalPlanId, long tacticalPlanVersion, DateTimeOffset now)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("A locked team sheet cannot be edited (CAL-3).");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tacticalPlanVersion);

        TacticalPlanId = tacticalPlanId;
        TacticalPlanVersion = tacticalPlanVersion;

        Touch(now);
    }

    /// <summary>Freezes the sheet at the lock deadline (`CAL-3`).</summary>
    /// <param name="now">The current instant.</param>
    public void Lock(DateTimeOffset now)
    {
        if (Status == TeamSheetStatus.Locked)
        {
            throw new InvalidOperationException("A team sheet cannot be locked twice.");
        }

        Status = TeamSheetStatus.Locked;
        LockedAt = now;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
