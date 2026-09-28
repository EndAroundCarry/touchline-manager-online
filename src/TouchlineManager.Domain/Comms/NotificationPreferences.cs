namespace TouchlineManager.Domain.Comms;

/// <summary>
/// A manager's choice of which notification emails the game sends (Stage 11, master plan §11.1).
/// </summary>
/// <remarks>
/// <para>
/// Preferences gate the <em>email</em> channel only. The inbox is the game's own record of what happened and
/// is never optional (`COM-1`); a manager who turns an email off still sees the message in their inbox.
/// </para>
/// <para>
/// A missing row means every preference is at its default. The row is created the first time a manager
/// changes a preference, so a manager who never opens the screen is not charged a write.
/// </para>
/// </remarks>
public sealed class NotificationPreferences
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private NotificationPreferences()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the manager the preferences belong to.</summary>
    public Guid ManagerId { get; private set; }

    /// <summary>Gets a value indicating whether deadline reminders are emailed.</summary>
    public bool EmailDeadlineReminders { get; private set; }

    /// <summary>Gets a value indicating whether inactivity warnings are emailed (`OCC-1`, `OCC-3`).</summary>
    public bool EmailInactivityWarnings { get; private set; }

    /// <summary>Gets a value indicating whether market messages are emailed.</summary>
    public bool EmailMarketMessages { get; private set; }

    /// <summary>Gets a value indicating whether a news digest is emailed.</summary>
    public bool EmailNewsDigest { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Creates a manager's preferences at their defaults.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="managerId">The manager.</param>
    /// <param name="now">The current instant.</param>
    public static NotificationPreferences CreateDefault(Guid id, Guid managerId, DateTimeOffset now) =>
        new()
        {
            Id = id,
            ManagerId = managerId,
            EmailDeadlineReminders = true,
            EmailInactivityWarnings = true,
            EmailMarketMessages = true,
            EmailNewsDigest = true,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>Updates the manager's email preferences.</summary>
    /// <param name="deadlineReminders">Whether deadline reminders are emailed.</param>
    /// <param name="inactivityWarnings">Whether inactivity warnings are emailed.</param>
    /// <param name="marketMessages">Whether market messages are emailed.</param>
    /// <param name="newsDigest">Whether a news digest is emailed.</param>
    /// <param name="now">The current instant.</param>
    public void Update(
        bool deadlineReminders,
        bool inactivityWarnings,
        bool marketMessages,
        bool newsDigest,
        DateTimeOffset now)
    {
        EmailDeadlineReminders = deadlineReminders;
        EmailInactivityWarnings = inactivityWarnings;
        EmailMarketMessages = marketMessages;
        EmailNewsDigest = newsDigest;

        UpdatedAt = now;
        Version++;
    }
}
