namespace Tactical11Manager.Domain.Comms;

/// <summary>
/// A thread of direct messages between exactly two managers, with a subject (`COM-10`).
/// </summary>
/// <remarks>
/// <para>
/// It is addressed to managers, not clubs, so it outlives either side's tenure the way an inbox message does
/// (`COM-1`). The club it was started from is kept as a fact about how the conversation began, not as an
/// address.
/// </para>
/// <para>
/// The aggregate holds only what the thread list needs: the subject and when the last message arrived. The
/// messages themselves are <see cref="DirectMessage"/> rows, written one at a time, so a reply never loads the
/// whole thread.
/// </para>
/// </remarks>
public sealed class Conversation
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Conversation()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the subject, as stored (`COM-13`).</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>Gets the manager who started the conversation.</summary>
    public Guid StartedByManagerId { get; private set; }

    /// <summary>Gets the manager the conversation was started with.</summary>
    public Guid RecipientManagerId { get; private set; }

    /// <summary>Gets the club page the conversation was started from.</summary>
    public Guid StartedFromClubId { get; private set; }

    /// <summary>Gets when the conversation started.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the latest message was sent, which orders the list.</summary>
    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Starts a conversation between two managers (`COM-10`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="subject">What the starter typed; it is normalized and must be valid (`COM-13`).</param>
    /// <param name="startedByManagerId">The manager who starts it.</param>
    /// <param name="recipientManagerId">The manager it is addressed to.</param>
    /// <param name="startedFromClubId">The club page it was started from.</param>
    /// <param name="now">The current instant.</param>
    public static Conversation Start(
        Guid id,
        string subject,
        Guid startedByManagerId,
        Guid recipientManagerId,
        Guid startedFromClubId,
        DateTimeOffset now)
    {
        if (startedByManagerId == Guid.Empty)
        {
            throw new ArgumentException("A conversation is started by a manager.", nameof(startedByManagerId));
        }

        if (recipientManagerId == Guid.Empty)
        {
            throw new ArgumentException("A conversation is addressed to a manager.", nameof(recipientManagerId));
        }

        if (startedByManagerId == recipientManagerId)
        {
            throw new ArgumentException(
                "A conversation is between two different managers (COM-10).",
                nameof(recipientManagerId));
        }

        if (startedFromClubId == Guid.Empty)
        {
            throw new ArgumentException("A conversation starts from a club page.", nameof(startedFromClubId));
        }

        var normalized = DirectMessageRules.NormalizeSubject(subject)
            ?? throw new ArgumentException(
                $"A subject is 1 to {DirectMessageRules.SubjectMaxChars} characters after trimming (COM-13).",
                nameof(subject));

        return new Conversation
        {
            Id = id,
            Subject = normalized,
            StartedByManagerId = startedByManagerId,
            RecipientManagerId = recipientManagerId,
            StartedFromClubId = startedFromClubId,
            CreatedAt = now,
            LastMessageAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Returns whether the manager is one of the two people in the conversation.</summary>
    /// <param name="managerId">The manager.</param>
    public bool IsParticipant(Guid managerId) =>
        managerId == StartedByManagerId || managerId == RecipientManagerId;

    /// <summary>Returns the other person in the conversation.</summary>
    /// <param name="managerId">One of the two participants.</param>
    /// <exception cref="ArgumentException">The manager is not a participant.</exception>
    public Guid OtherParticipant(Guid managerId)
    {
        if (managerId == StartedByManagerId)
        {
            return RecipientManagerId;
        }

        if (managerId == RecipientManagerId)
        {
            return StartedByManagerId;
        }

        throw new ArgumentException("The manager is not part of this conversation.", nameof(managerId));
    }

    /// <summary>Records that a message was sent in the thread, so the list shows it first.</summary>
    /// <param name="now">The current instant.</param>
    public void RecordReply(DateTimeOffset now)
    {
        // A clock that steps backwards must not move the thread down the list.
        LastMessageAt = now > LastMessageAt ? now : LastMessageAt;
        UpdatedAt = now;
        Version++;
    }
}
