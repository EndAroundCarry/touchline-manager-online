namespace Tactical11Manager.Application.Abstractions.Comms;

/// <summary>The other person in a conversation, as the reader sees them now.</summary>
/// <remarks>
/// The club is where the manager is today, not where they were when the conversation started, because the
/// conversation is addressed to the person and not the club (`COM-10`). It is null when they hold none.
/// </remarks>
/// <param name="ManagerId">The manager.</param>
/// <param name="DisplayName">The account's display name.</param>
/// <param name="ClubId">The club they hold now, or null.</param>
/// <param name="ClubName">The name of that club, or null.</param>
public sealed record DirectMessageParticipantRow(Guid ManagerId, string DisplayName, Guid? ClubId, string? ClubName);

/// <summary>Where a page starts: the last row the previous page returned.</summary>
/// <param name="At">The row's instant, which is the conversation's last message or the message's send time.</param>
/// <param name="Id">The row's identity, which breaks a same-instant tie.</param>
public sealed record DirectMessageCursorPosition(DateTimeOffset At, Guid Id);

/// <summary>One line of the conversation list.</summary>
/// <param name="Id">The conversation.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="Other">The other participant.</param>
/// <param name="LastMessageAt">When the latest message was sent.</param>
/// <param name="LastMessagePreview">The start of the latest message, or null if the thread is empty.</param>
/// <param name="UnreadCount">How many messages in it are addressed to the reader and unread.</param>
/// <param name="BlockedByMe">Whether the reader has blocked the other participant.</param>
public sealed record ConversationRow(
    Guid Id,
    string Subject,
    DirectMessageParticipantRow Other,
    DateTimeOffset LastMessageAt,
    string? LastMessagePreview,
    int UnreadCount,
    bool BlockedByMe);

/// <summary>One page of the conversation list.</summary>
/// <param name="Conversations">The page, most recently active first.</param>
/// <param name="HasMore">Whether a further page exists.</param>
public sealed record ConversationPage(IReadOnlyList<ConversationRow> Conversations, bool HasMore);

/// <summary>One message in a thread, from the reader's side.</summary>
/// <param name="Id">The message.</param>
/// <param name="FromMe">Whether the reader wrote it.</param>
/// <param name="Body">The stored text.</param>
/// <param name="SentAt">When it was sent.</param>
/// <param name="ReadAt">When its recipient read it, or null while unread.</param>
public sealed record DirectMessageRow(Guid Id, bool FromMe, string Body, DateTimeOffset SentAt, DateTimeOffset? ReadAt);

/// <summary>One page of a thread, with what the reader needs to decide whether they can reply.</summary>
/// <param name="Id">The conversation.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="Other">The other participant.</param>
/// <param name="Messages">The page, newest first.</param>
/// <param name="HasMore">Whether older messages exist.</param>
/// <param name="BlockedByMe">Whether the reader has blocked the other participant.</param>
/// <param name="BlockedMe">Whether the other participant has blocked the reader.</param>
public sealed record ConversationThreadPage(
    Guid Id,
    string Subject,
    DirectMessageParticipantRow Other,
    IReadOnlyList<DirectMessageRow> Messages,
    bool HasMore,
    bool BlockedByMe,
    bool BlockedMe);

/// <summary>
/// The read side of direct messages (`COM-10` to `COM-16`).
/// </summary>
/// <remarks>
/// A projection, not an aggregate (`MOD-3`): it joins in the other participant's display name and current club,
/// which belong to the auth and world modules, so the comms entities hold only managers' identities. Both lists
/// are keyset-paged and fetch one row more than they return, so a next page is a fact rather than a guess.
/// </remarks>
public interface IDirectMessageQueries
{
    /// <summary>Reads one page of a manager's conversations, most recently active first.</summary>
    /// <param name="managerId">The reader.</param>
    /// <param name="before">Where to continue from, or null for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ConversationPage> GetConversationsAsync(
        Guid managerId,
        DirectMessageCursorPosition? before,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one page of a conversation's messages, newest first, or null when the manager is not part of it
    /// or it does not exist.
    /// </summary>
    /// <param name="managerId">The reader.</param>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="before">Where to continue from, or null for the newest page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ConversationThreadPage?> GetThreadAsync(
        Guid managerId,
        Guid conversationId,
        DirectMessageCursorPosition? before,
        CancellationToken cancellationToken);

    /// <summary>Counts the direct messages addressed to a manager that they have not read, which the sync poll needs.</summary>
    /// <param name="managerId">The recipient.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountUnreadAsync(Guid managerId, CancellationToken cancellationToken);
}
