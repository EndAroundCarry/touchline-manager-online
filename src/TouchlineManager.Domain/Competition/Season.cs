using System.Globalization;

namespace TouchlineManager.Domain.Competition;

/// <summary>
/// One season of play, shared by every country in the world (`WORLD-10`).
/// </summary>
/// <remarks>
/// <para>
/// The season owns the real-time window; the divisions own who plays in it. Keeping the calendar on
/// the season is what makes `CAL-7` true — all six countries advance together, and a division cannot
/// drift onto its own schedule.
/// </para>
/// <para>
/// The game year is stored, not derived from the start date, because game years advance at rollover
/// and not on a real-world anniversary (`TIME-3`). An accelerated test season must therefore be able
/// to declare its own year.
/// </para>
/// </remarks>
public sealed class Season
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Season()
    {
    }

    /// <summary>Gets the season identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning world.</summary>
    public Guid WorldId { get; private set; }

    /// <summary>Gets the season's ordinal within the world, starting at 1.</summary>
    public int SequenceNumber { get; private set; }

    /// <summary>Gets the label managers see, e.g. <c>2026/27</c>.</summary>
    public string DisplayLabel { get; private set; } = string.Empty;

    /// <summary>Gets the game year. Player aging and contract years advance from here (`TIME-3`).</summary>
    public int GameYear { get; private set; }

    /// <summary>Gets the kickoff of matchday 1.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Gets the kickoff of the final matchday.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Gets when the rollover period closes (`CAL-6`).</summary>
    public DateTimeOffset RolloverEndsAt { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public SeasonStatus Status { get; private set; }

    /// <summary>Gets the rule-set version in force for this season, frozen at creation.</summary>
    public string RuleSetVersion { get; private set; } = string.Empty;

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Creates a season shell scheduled from its first matchday.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="worldId">The owning world.</param>
    /// <param name="sequenceNumber">The ordinal within the world.</param>
    /// <param name="gameYear">The game year.</param>
    /// <param name="ruleSetVersion">The rule-set version in force.</param>
    /// <param name="firstMatchday">The first matchday date; it must fall on a matchday weekday.</param>
    /// <param name="now">The current instant.</param>
    public static Season Create(
        Guid id,
        Guid worldId,
        int sequenceNumber,
        int gameYear,
        string ruleSetVersion,
        DateOnly firstMatchday,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleSetVersion);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequenceNumber, 1);

        var window = SeasonCalendar.Window(firstMatchday);

        return new Season
        {
            Id = id,
            WorldId = worldId,
            SequenceNumber = sequenceNumber,
            DisplayLabel = LabelFor(gameYear),
            GameYear = gameYear,
            StartsAt = window.StartsAt,
            EndsAt = window.EndsAt,
            RolloverEndsAt = window.RolloverEndsAt,
            Status = SeasonStatus.Scheduled,
            RuleSetVersion = ruleSetVersion,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Gets the display label for a game year, e.g. <c>2026/27</c>.</summary>
    public static string LabelFor(int gameYear) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{gameYear}/{((gameYear + 1) % 100).ToString("D2", CultureInfo.InvariantCulture)}");

    /// <summary>Starts the season, opening it for claims and play.</summary>
    /// <param name="now">The current instant.</param>
    public void Activate(DateTimeOffset now)
    {
        if (Status != SeasonStatus.Scheduled)
        {
            throw new InvalidOperationException($"Only a scheduled season can start, not one in state '{Status}'.");
        }

        Status = SeasonStatus.Active;

        Touch(now);
    }

    /// <summary>Closes match play and hands the season to the rollover state machine (`CAL-6`).</summary>
    /// <param name="now">The current instant.</param>
    public void BeginRollover(DateTimeOffset now)
    {
        if (Status != SeasonStatus.Active)
        {
            throw new InvalidOperationException($"Only an active season can roll over, not one in state '{Status}'.");
        }

        Status = SeasonStatus.Rollover;

        Touch(now);
    }

    /// <summary>Seals the season. History from here is immutable (`PR-6`).</summary>
    /// <param name="now">The current instant.</param>
    public void Complete(DateTimeOffset now)
    {
        if (Status == SeasonStatus.Completed)
        {
            return;
        }

        Status = SeasonStatus.Completed;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
