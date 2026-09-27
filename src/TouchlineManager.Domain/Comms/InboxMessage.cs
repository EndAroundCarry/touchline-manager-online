namespace TouchlineManager.Domain.Comms;

/// <summary>
/// One thing the game told a manager, durable until it is read (master plan §6.9, F-41).
/// </summary>
/// <remarks>
/// <para>
/// A message carries a stable template key and the parameters that fill it, never finished prose: the same
/// reading the match commentary uses (`MAT-8`, master plan §8.6), so a message written today can be rendered
/// in another language later without being rewritten. The English a manager reads is derived from those two
/// fields by the application layer, which is why nothing here stores a sentence.
/// </para>
/// <para>
/// The recipient is a manager rather than a club, matching §6.9: a message is addressed to the person who
/// held the club when the event it describes happened, so it survives a takeover that happens afterwards.
/// </para>
/// <para>
/// Reading is the only mutation. There is no archive and no delete in the MVP, so neither is modelled: a
/// half-built archive with no way to reach it is exactly the feature-incomplete surface §17.12 keeps out.
/// </para>
/// </remarks>
public sealed class InboxMessage
{
    /// <summary>The longest template key a message may carry.</summary>
    public const int MaxTemplateKeyLength = 64;

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private InboxMessage()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the manager the message was written for.</summary>
    public Guid RecipientManagerId { get; private set; }

    /// <summary>Gets the shelf the message sits on.</summary>
    public InboxCategory Category { get; private set; }

    /// <summary>Gets the stable template that renders this message.</summary>
    public string TemplateKey { get; private set; } = string.Empty;

    /// <summary>Gets the template's parameters, as the stored JSON document.</summary>
    public string ParametersJson { get; private set; } = string.Empty;

    /// <summary>Gets the entity the message is about — a match, a player, a fixture — or null.</summary>
    public Guid? RelatedEntityId { get; private set; }

    /// <summary>Gets when the message was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the manager read it, or <see langword="null"/> while it is unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets a value indicating whether the manager has read the message.</summary>
    public bool IsRead => ReadAt is not null;

    /// <summary>Writes a message for a manager.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="recipientManagerId">The manager the message is for.</param>
    /// <param name="category">The shelf the message sits on.</param>
    /// <param name="templateKey">The stable template that renders it.</param>
    /// <param name="parametersJson">The template's parameters, as a stored document.</param>
    /// <param name="relatedEntityId">The entity the message is about, when it names one.</param>
    /// <param name="now">The current instant.</param>
    public static InboxMessage Record(
        Guid id,
        Guid recipientManagerId,
        InboxCategory category,
        string templateKey,
        string parametersJson,
        Guid? relatedEntityId,
        DateTimeOffset now)
    {
        if (recipientManagerId == Guid.Empty)
        {
            throw new ArgumentException("A message is addressed to a manager.", nameof(recipientManagerId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);

        if (templateKey.Length > MaxTemplateKeyLength)
        {
            throw new ArgumentException(
                $"A template key is at most {MaxTemplateKeyLength} characters.",
                nameof(templateKey));
        }

        return new InboxMessage
        {
            Id = id,
            RecipientManagerId = recipientManagerId,
            Category = category,
            TemplateKey = templateKey,
            ParametersJson = parametersJson,
            RelatedEntityId = relatedEntityId,
            CreatedAt = now,
            ReadAt = null,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Marks the message read. Reading twice is a no-op, so a retried command cannot advance the version.
    /// </summary>
    /// <param name="now">The current instant.</param>
    public void MarkRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            return;
        }

        ReadAt = now;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
