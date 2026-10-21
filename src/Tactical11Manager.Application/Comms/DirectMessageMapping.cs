using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Contracts.Comms;

namespace Tactical11Manager.Application.Comms;

/// <summary>Projects the direct-message read models into their contracts.</summary>
internal static class DirectMessageMapping
{
    /// <summary>Why a reply is refused when the reader has blocked the other manager (`COM-15`).</summary>
    public const string ReplyBlockedByMe = "You have blocked this manager. Unblock them to reply.";

    /// <summary>Why a reply is refused when the other manager has blocked the reader (`COM-15`).</summary>
    public const string ReplyBlockedByThem = "This manager is not accepting your messages.";

    /// <summary>Why a reply is refused when the other manager's account is closing (`COM-12`).</summary>
    public const string ReplyAccountClosing = "This manager's account is closing, so they cannot receive messages.";

    /// <summary>Projects the other participant.</summary>
    /// <param name="row">The stored participant.</param>
    public static ConversationParticipantResponse ToResponse(this DirectMessageParticipantRow row) =>
        new(row.ManagerId, row.DisplayName, row.ClubId, row.ClubName);

    /// <summary>Projects one message of a thread.</summary>
    /// <param name="row">The stored message.</param>
    public static DirectMessageResponse ToResponse(this DirectMessageRow row) =>
        new(row.Id, row.FromMe, row.Body, row.SentAt, row.ReadAt);
}
