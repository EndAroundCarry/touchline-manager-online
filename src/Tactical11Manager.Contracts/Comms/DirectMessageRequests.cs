namespace Tactical11Manager.Contracts.Comms;

/// <summary>Starts a conversation with the manager who runs a club (`COM-11`).</summary>
/// <param name="ClubId">The club page the conversation is started from.</param>
/// <param name="Subject">The subject, 1 to 80 characters after trimming (`COM-13`).</param>
/// <param name="Body">The first message, 1 to 1,000 characters after trimming (`COM-13`).</param>
public sealed record StartConversationRequest(Guid ClubId, string Subject, string Body);

/// <summary>Replies in a conversation (`COM-12`).</summary>
/// <param name="Body">The message, 1 to 1,000 characters after trimming (`COM-13`).</param>
public sealed record SendDirectMessageRequest(string Body);
