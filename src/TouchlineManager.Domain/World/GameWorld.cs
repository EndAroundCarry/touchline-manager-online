using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.World;

/// <summary>
/// The persistent game world. The aggregate root for global configuration and season numbering.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is per-manager: the world owns the calendar every country shares (`WORLD-10`) and the
/// rule-set version that interpretations of historical seasons are resolved against.
/// </para>
/// <para>
/// Like every aggregate in this project it is deterministic — it takes the current instant as an
/// argument and never reads the clock, so the lifecycle is testable without freezing time.
/// </para>
/// </remarks>
public sealed class GameWorld
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private GameWorld()
    {
    }

    /// <summary>Gets the world identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the world's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the lifecycle state.</summary>
    public GameWorldStatus Status { get; private set; }

    /// <summary>Gets the rule-set version in force. Stamped onto every season created under it.</summary>
    public string RuleSetVersion { get; private set; } = string.Empty;

    /// <summary>Gets the number of the season currently running, starting at 1.</summary>
    public int CurrentSeasonNumber { get; private set; }

    /// <summary>Gets the standard kickoff time in UTC that every country's calendar uses (`CAL-2`).</summary>
    public TimeOnly KickoffUtc { get; private set; }

    /// <summary>Gets when the world was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the world was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets a value indicating whether the world currently accepts new club claims.</summary>
    public bool AcceptsClaims => GameWorldStatusRules.AcceptsClaims(Status);

    /// <summary>Creates a world in the <see cref="GameWorldStatus.Active"/> state, at season 1.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="name">The world's name.</param>
    /// <param name="ruleSetVersion">The rule-set version to stamp onto it.</param>
    /// <param name="kickoffUtc">The standard kickoff time.</param>
    /// <param name="now">The current instant.</param>
    public static GameWorld Create(
        Guid id,
        string name,
        string ruleSetVersion,
        TimeOnly kickoffUtc,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleSetVersion);

        return new GameWorld
        {
            Id = id,
            Name = name.Trim(),
            Status = GameWorldStatus.Active,
            RuleSetVersion = ruleSetVersion,
            CurrentSeasonNumber = 1,
            KickoffUtc = kickoffUtc,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Creates a world with the current rule set and the standard kickoff time.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="name">The world's name.</param>
    /// <param name="now">The current instant.</param>
    public static GameWorld Create(Guid id, string name, DateTimeOffset now) =>
        Create(id, name, WorldRuleSet.Version, WorldRuleSet.KickoffUtc, now);

    /// <summary>Freezes the world: onboarding stops, play continues (master plan §13).</summary>
    /// <param name="now">The current instant.</param>
    public void Freeze(DateTimeOffset now)
    {
        Status = GameWorldStatus.Frozen;

        Touch(now);
    }

    /// <summary>Reopens a frozen world for claims.</summary>
    /// <param name="now">The current instant.</param>
    public void Resume(DateTimeOffset now)
    {
        if (Status == GameWorldStatus.Retired)
        {
            throw new InvalidOperationException("A retired world cannot be resumed.");
        }

        Status = GameWorldStatus.Active;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
