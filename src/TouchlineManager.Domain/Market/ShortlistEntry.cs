using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Market;

/// <summary>
/// A private note a manager keeps about a player they are watching (`SCT-3`).
/// </summary>
/// <remarks>
/// <para>
/// The shortlist is the manager's own list, not club state: it belongs to the manager profile so it survives
/// a change of club, and it is never visible to another manager. That is why it is keyed by the manager and
/// the player rather than by a club, and why the notes are a bounded free-text field rather than structured
/// data the game reasons about (`SCT-3`).
/// </para>
/// </remarks>
public sealed class ShortlistEntry
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ShortlistEntry()
    {
    }

    /// <summary>Gets the entry identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the manager who keeps the note.</summary>
    public Guid ManagerId { get; private set; }

    /// <summary>Gets the watched player.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the private note, or null when there is none.</summary>
    public string? Notes { get; private set; }

    /// <summary>Gets when the entry was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the entry was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Adds a player to a manager's shortlist (`SCT-3`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="managerId">The manager who keeps the note.</param>
    /// <param name="playerId">The watched player.</param>
    /// <param name="notes">The private note, or null for none.</param>
    /// <param name="now">The current instant.</param>
    public static ShortlistEntry Add(
        Guid id,
        Guid managerId,
        Guid playerId,
        string? notes,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            ManagerId = managerId,
            PlayerId = playerId,
            Notes = Normalize(notes),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>Replaces the private note (`SCT-3`).</summary>
    /// <param name="notes">The new note, or null to clear it.</param>
    /// <param name="now">The current instant.</param>
    public void UpdateNotes(string? notes, DateTimeOffset now)
    {
        Notes = Normalize(notes);

        UpdatedAt = now;
        Version++;
    }

    private static string? Normalize(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var trimmed = notes.Trim();

        if (trimmed.Length > WorldRuleSet.ShortlistNotesMaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(notes),
                trimmed.Length,
                $"A shortlist note is at most {WorldRuleSet.ShortlistNotesMaxLength} characters (SCT-3).");
        }

        return trimmed;
    }
}
