using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.World;

/// <summary>
/// One of a club's buildings: its level, and the upgrade under way if there is one (game rules §13.7,
/// `STRC-1`…`STRC-3`).
/// </summary>
/// <remarks>
/// <para>
/// A club has exactly one building of each <see cref="StructureKind"/>. Every building opens at level 1 for
/// free and goes up to <see cref="StructureRuleSet.MaxLevel"/>. An upgrade takes days: while it runs the
/// building keeps trading at its old <see cref="Level"/>, and only <see cref="CompleteUpgrade"/> raises it
/// (`STRC-3`).
/// </para>
/// <para>
/// This type owns the state machine. The money is the finance module's, so the use case posts the
/// upgrade's ledger entry in the same unit of work that calls <see cref="StartUpgrade"/>, and the job that
/// completes the work is the application's.
/// </para>
/// </remarks>
public sealed class ClubStructure
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubStructure()
    {
    }

    /// <summary>Gets the building identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club that owns the building.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets which building this is.</summary>
    public StructureKind Kind { get; private set; }

    /// <summary>Gets the level the building trades at, 1–10.</summary>
    public int Level { get; private set; }

    /// <summary>Gets the level the running upgrade will reach, or null when nothing is being built.</summary>
    public int? UpgradeTargetLevel { get; private set; }

    /// <summary>Gets when the running upgrade finishes, or null when nothing is being built.</summary>
    public DateTimeOffset? UpgradeDueAt { get; private set; }

    /// <summary>Gets when the building was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the building was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets a value indicating whether an upgrade is under way (`STRC-3`).</summary>
    public bool IsBuilding => UpgradeTargetLevel is not null;

    /// <summary>Gets a value indicating whether the building is at the top level (`STRC-1`).</summary>
    public bool IsAtMaxLevel => Level >= StructureRuleSet.MaxLevel;

    /// <summary>Opens a club's building at level 1, free (`STRC-1`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The owning club.</param>
    /// <param name="kind">Which building.</param>
    /// <param name="now">The current instant.</param>
    public static ClubStructure Open(Guid id, Guid clubId, StructureKind kind, DateTimeOffset now)
    {
        _ = kind.ToCode();

        return new ClubStructure
        {
            Id = id,
            ClubId = clubId,
            Kind = kind,
            Level = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Starts the upgrade to the next level (`STRC-3`).</summary>
    /// <param name="dueAt">When the work finishes.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The level the work will reach.</returns>
    /// <exception cref="InvalidOperationException">When an upgrade is already running or the building is at the top level.</exception>
    public int StartUpgrade(DateTimeOffset dueAt, DateTimeOffset now)
    {
        if (IsBuilding)
        {
            throw new InvalidOperationException(
                $"The {Kind.ToCode()} is already being upgraded to level {UpgradeTargetLevel}; one upgrade runs at a time (STRC-3).");
        }

        if (IsAtMaxLevel)
        {
            throw new InvalidOperationException(
                $"The {Kind.ToCode()} is at level {Level}, the highest there is (STRC-1).");
        }

        if (dueAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(dueAt), dueAt, "An upgrade takes time, so it finishes after it starts (STRC-3).");
        }

        UpgradeTargetLevel = Level + 1;
        UpgradeDueAt = dueAt;
        UpdatedAt = now;
        Version++;

        return UpgradeTargetLevel.Value;
    }

    /// <summary>
    /// Takes the building back to level 1 and drops the upgrade under way, which is not refunded (`TKO-3`). Only a clean start does this. The job
    /// that would have finished the upgrade finds nothing building and does nothing (<see cref="CompleteUpgrade"/>).
    /// </summary>
    /// <param name="now">The current instant.</param>
    public void ResetToOpening(DateTimeOffset now)
    {
        Level = StructureRuleSet.OpeningLevel;
        UpgradeTargetLevel = null;
        UpgradeDueAt = null;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Finishes the running upgrade and raises the level (`STRC-3`).</summary>
    /// <param name="now">The current instant.</param>
    /// <returns>True when the level rose; false when nothing was being built, so a retried job does nothing.</returns>
    public bool CompleteUpgrade(DateTimeOffset now)
    {
        if (UpgradeTargetLevel is not { } target)
        {
            return false;
        }

        Level = target;
        UpgradeTargetLevel = null;
        UpgradeDueAt = null;
        UpdatedAt = now;
        Version++;

        return true;
    }
}
