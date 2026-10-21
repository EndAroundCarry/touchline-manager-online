namespace Tactical11Manager.Contracts.Comms;

/// <summary>
/// The stable refusal codes the inbox surface returns (master plan §10.7).
/// </summary>
/// <remarks>
/// A client branches on these rather than on prose. They are separate from the squad, world, and competition
/// codes because they answer the inbox's own questions: whether a message is one the caller may see, and
/// whether the page cursor is one this build produced.
/// </remarks>
public static class CommsErrorCodes
{
    /// <summary>No message exists with the requested identity for this manager.</summary>
    public const string MessageNotFound = "MESSAGE_NOT_FOUND";

    /// <summary>The page cursor was not produced by this build.</summary>
    public const string InvalidCursor = "INVALID_CURSOR";

    /// <summary>The club has no human manager, so there is nobody to write to (`COM-11`).</summary>
    public const string ClubHasNoManager = "CLUB_HAS_NO_MANAGER";

    /// <summary>The club is the sender's own (`COM-11`).</summary>
    public const string CannotMessageOwnClub = "CANNOT_MESSAGE_OWN_CLUB";

    /// <summary>The sender holds no club, and a conversation is started from one (`COM-11`).</summary>
    public const string SenderHasNoClub = "SENDER_HAS_NO_CLUB";

    /// <summary>The recipient's account is being closed (`COM-11`, `COM-12`).</summary>
    public const string RecipientUnavailable = "RECIPIENT_UNAVAILABLE";

    /// <summary>A block stands between the two managers, in either direction (`COM-15`).</summary>
    public const string ManagerBlocked = "MANAGER_BLOCKED";

    /// <summary>No conversation exists with the requested identity for this manager.</summary>
    public const string ConversationNotFound = "CONVERSATION_NOT_FOUND";

    /// <summary>The subject or message is empty, or too long after trimming (`COM-13`).</summary>
    public const string InvalidMessageText = "INVALID_MESSAGE_TEXT";

    /// <summary>No manager exists with the requested identity, or it is the caller's own.</summary>
    public const string ManagerNotFound = "MANAGER_NOT_FOUND";
}
