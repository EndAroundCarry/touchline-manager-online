namespace Tactical11Manager.Contracts.Comms;

/// <summary>The other person in a conversation, as the reader sees them now (`COM-10`).</summary>
/// <remarks>
/// The club is the one they hold today, not the one they held when the conversation started, because a
/// conversation is addressed to the person. The manager identity is here so the screen can block them.
/// </remarks>
/// <param name="ManagerId">The manager.</param>
/// <param name="DisplayName">The account's display name.</param>
/// <param name="ClubId">The club they hold now, or null when they hold none.</param>
/// <param name="ClubName">The name of that club, or null.</param>
public sealed record ConversationParticipantResponse(Guid ManagerId, string DisplayName, Guid? ClubId, string? ClubName);

/// <summary>One line of the conversation list.</summary>
/// <param name="Id">The conversation.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="OtherManager">The other participant.</param>
/// <param name="LastMessageAt">When the latest message was sent.</param>
/// <param name="LastMessagePreview">The start of the latest message, on one line.</param>
/// <param name="UnreadCount">How many messages in it are addressed to the reader and unread.</param>
/// <param name="BlockedByMe">Whether the reader has blocked the other participant.</param>
public sealed record ConversationSummaryResponse(
    Guid Id,
    string Subject,
    ConversationParticipantResponse OtherManager,
    DateTimeOffset LastMessageAt,
    string? LastMessagePreview,
    int UnreadCount,
    bool BlockedByMe);

/// <summary>One page of a manager's conversations, most recently active first.</summary>
/// <param name="Conversations">The page.</param>
/// <param name="UnreadCount">How many direct messages addressed to the reader are unread, for the tab.</param>
/// <param name="NextCursor">Where to continue, or null when this is the last page.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record ConversationListResponse(
    IReadOnlyList<ConversationSummaryResponse> Conversations,
    int UnreadCount,
    string? NextCursor,
    DateTimeOffset ServerTime);

/// <summary>One message in a thread, from the reader's side.</summary>
/// <param name="Id">The message.</param>
/// <param name="FromMe">Whether the reader wrote it.</param>
/// <param name="Body">The text, which a client always shows as text and never as markup (`COM-13`).</param>
/// <param name="SentAt">When it was sent.</param>
/// <param name="ReadAt">When its recipient read it, or null while unread.</param>
public sealed record DirectMessageResponse(Guid Id, bool FromMe, string Body, DateTimeOffset SentAt, DateTimeOffset? ReadAt);

/// <summary>One page of a thread.</summary>
/// <param name="Id">The conversation.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="OtherManager">The other participant.</param>
/// <param name="Messages">The page, newest first; a client shows it oldest first.</param>
/// <param name="NextCursor">Where to continue towards older messages, or null when there are none.</param>
/// <param name="BlockedByMe">Whether the reader has blocked the other participant.</param>
/// <param name="CanReply">Whether a reply would be accepted now (`COM-12`, `COM-15`).</param>
/// <param name="ReplyBlockedReason">Why a reply would be refused, in words, or null when it would not.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record ConversationResponse(
    Guid Id,
    string Subject,
    ConversationParticipantResponse OtherManager,
    IReadOnlyList<DirectMessageResponse> Messages,
    string? NextCursor,
    bool BlockedByMe,
    bool CanReply,
    string? ReplyBlockedReason,
    DateTimeOffset ServerTime);

/// <summary>What a start or a reply returns: where the message went.</summary>
/// <param name="ConversationId">The conversation.</param>
/// <param name="MessageId">The message that was sent.</param>
/// <param name="SentAt">When it was sent.</param>
public sealed record SentDirectMessageResponse(Guid ConversationId, Guid MessageId, DateTimeOffset SentAt);
