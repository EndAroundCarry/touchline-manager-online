namespace Tactical11Manager.Domain.Comms;

/// <summary>
/// One plain-text message in a <see cref="Conversation"/>, unread until its recipient opens the thread
/// (`COM-13`, `COM-16`).
/// </summary>
/// <remarks>
/// Reading is the only mutation. A manager can neither edit nor delete a message (`COM-16`), so neither is
/// modelled.
/// </remarks>
public sealed class DirectMessage
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private DirectMessage()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the conversation the message belongs to.</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>Gets the manager who wrote it.</summary>
    public Guid SenderManagerId { get; private set; }

    /// <summary>Gets the manager it is addressed to.</summary>
    public Guid RecipientManagerId { get; private set; }

    /// <summary>Gets the text, as stored (`COM-13`).</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Gets when it was sent.</summary>
    public DateTimeOffset SentAt { get; private set; }

    /// <summary>Gets when the recipient opened the conversation, or null while it is unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>Gets a value indicating whether the recipient has read the message.</summary>
    public bool IsRead => ReadAt is not null;

    /// <summary>Writes a message to the other person in a conversation.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="senderManagerId">The author.</param>
    /// <param name="recipientManagerId">The other participant.</param>
    /// <param name="body">What the author typed; it is normalized and must be valid (`COM-13`).</param>
    /// <param name="now">The current instant.</param>
    public static DirectMessage Write(
        Guid id,
        Guid conversationId,
        Guid senderManagerId,
        Guid recipientManagerId,
        string body,
        DateTimeOffset now)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("A message belongs to a conversation.", nameof(conversationId));
        }

        if (senderManagerId == Guid.Empty || recipientManagerId == Guid.Empty)
        {
            throw new ArgumentException("A message goes from one manager to another.", nameof(senderManagerId));
        }

        if (senderManagerId == recipientManagerId)
        {
            throw new ArgumentException(
                "A message goes between two different managers (COM-10).",
                nameof(recipientManagerId));
        }

        var normalized = DirectMessageRules.NormalizeBody(body)
            ?? throw new ArgumentException(
                $"A message is 1 to {DirectMessageRules.BodyMaxChars} characters after trimming (COM-13).",
                nameof(body));

        return new DirectMessage
        {
            Id = id,
            ConversationId = conversationId,
            SenderManagerId = senderManagerId,
            RecipientManagerId = recipientManagerId,
            Body = normalized,
            SentAt = now,
            ReadAt = null,
        };
    }

    /// <summary>Marks the message read. Reading twice does nothing, so the first read is kept (`COM-16`).</summary>
    /// <param name="now">The current instant.</param>
    public void MarkRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            return;
        }

        ReadAt = now;
    }
}
