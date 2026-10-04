namespace TouchlineManager.Contracts.Comms;

/// <summary>One message in a manager's inbox (master plan §6.9, §10.7, F-41).</summary>
/// <remarks>
/// The rendered English is carried beside the stable <paramref name="Category"/> and
/// <paramref name="TemplateKey"/> so a client shows a sentence today and can re-render the same message in
/// another language when the template tokens are localized, which is the master-plan §8.6 contract for
/// commentary applied to the inbox. The raw parameters stay server-side: they name internal identities a
/// manager cannot act on.
/// </remarks>
/// <param name="Id">The message.</param>
/// <param name="Category">The shelf the message sits on, a stable code.</param>
/// <param name="TemplateKey">The stable template that rendered it.</param>
/// <param name="Title">The rendered headline.</param>
/// <param name="Body">The rendered detail.</param>
/// <param name="RelatedEntityId">
/// The entity the message is about — a match, a player, a fixture — or null. What it names is decided by
/// the category, so a client links a result to its match and a suspension to its player.
/// </param>
/// <param name="IsRead">Whether the manager has read it.</param>
/// <param name="CreatedAt">When it was written.</param>
/// <param name="ReadAt">When it was read, or null while unread.</param>
/// <param name="Spoiler">
/// What <paramref name="Title"/> and <paramref name="Body"/> hold back because it gives a match away — the
/// score, the outcome, the table move — or null. The client shows it only when the manager asks, or has watched
/// the match, so a result never arrives already told.
/// </param>
public sealed record InboxMessageResponse(
    Guid Id,
    string Category,
    string TemplateKey,
    string Title,
    string Body,
    Guid? RelatedEntityId,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? Spoiler = null);

/// <summary>One page of a manager's inbox (master plan §10.7).</summary>
/// <param name="Messages">The page, newest first.</param>
/// <param name="UnreadCount">How many of the manager's messages are unread, for the badge.</param>
/// <param name="NextCursor">Where to continue, or null when this is the last page.</param>
/// <param name="ServerTime">
/// The server's current instant, so a client with a wrong clock shows the right "as of" (`TIME-5`).
/// </param>
public sealed record InboxResponse(
    IReadOnlyList<InboxMessageResponse> Messages,
    int UnreadCount,
    string? NextCursor,
    DateTimeOffset ServerTime);

/// <summary>
/// The lightweight synchronization summary the shell polls (master plan §10.7, §11.2, ADR-0007).
/// </summary>
/// <remarks>
/// The unread count is what the navigation badge needs and the poll is the one read that keeps it fresh
/// while a manager works. Changed-resource hints for the other modules arrive with the screens that chase
/// them; a hint with no consumer would be a field nobody reads.
/// </remarks>
/// <param name="ServerTime">The server's current instant.</param>
/// <param name="UnreadInboxCount">How many inbox messages are unread.</param>
/// <param name="ReadOnly">
/// Whether the game is in read-only mode, so the shell can block manager writes before they are sent
/// (master plan §13, `F-51`).
/// </param>
/// <param name="ReadOnlyMessage">The operator's stated reason while read-only, otherwise null.</param>
public sealed record SyncResponse(
    DateTimeOffset ServerTime,
    int UnreadInboxCount,
    bool ReadOnly,
    string? ReadOnlyMessage);
