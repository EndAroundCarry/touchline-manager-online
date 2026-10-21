using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.Comms;
using Tactical11Manager.Domain.Comms;

namespace Tactical11Manager.Application.Comms;

/// <summary>The result of replying in a conversation.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Sent">The conversation and the new message, when it was sent.</param>
public sealed record ReplyToConversationResult(DirectMessageOutcome Outcome, SentDirectMessageResponse? Sent);

/// <summary>
/// Adds a message to a conversation the caller is part of (`COM-12`, `COM-13`, `COM-15`).
/// </summary>
/// <remarks>
/// A reply needs no club: either side can answer at any time, so a manager who has since left their club still
/// answers the people who wrote to them. What stops a reply is a block in either direction, or the other side's
/// account being closed. A conversation the caller is not part of is answered as one that does not exist, so a
/// guessed identity confirms nothing (threat I-5).
/// </remarks>
public sealed class ReplyToConversation
{
    private readonly IDirectMessageRepository _messages;
    private readonly IManagerRepository _managers;
    private readonly IClubManagerLookup _clubManagers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the command.</summary>
    public ReplyToConversation(
        IDirectMessageRepository messages,
        IManagerRepository managers,
        IClubManagerLookup clubManagers,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _messages = messages;
        _managers = managers;
        _clubManagers = clubManagers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Sends the reply.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="body">What the sender typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReplyToConversationResult> ExecuteAsync(
        Guid userId,
        Guid conversationId,
        string? body,
        CancellationToken cancellationToken)
    {
        var sender = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (sender is null)
        {
            return Refused(DirectMessageOutcome.NoManagerProfile);
        }

        var conversation = await _messages.FindConversationAsync(sender.Id, conversationId, cancellationToken);

        if (conversation is null)
        {
            return Refused(DirectMessageOutcome.ConversationNotFound);
        }

        var normalizedBody = DirectMessageRules.NormalizeBody(body);

        if (normalizedBody is null)
        {
            return Refused(DirectMessageOutcome.InvalidText);
        }

        var recipientId = conversation.OtherParticipant(sender.Id);

        if (await _clubManagers.IsAccountClosingAsync(recipientId, cancellationToken))
        {
            return Refused(DirectMessageOutcome.RecipientUnavailable);
        }

        if (await _messages.IsBlockedBetweenAsync(sender.Id, recipientId, cancellationToken))
        {
            return Refused(DirectMessageOutcome.Blocked);
        }

        var now = _clock.UtcNow;

        var message = DirectMessage.Write(
            Guid.CreateVersion7(),
            conversation.Id,
            sender.Id,
            recipientId,
            normalizedBody,
            now);

        _messages.Add(message);
        conversation.RecordReply(now);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReplyToConversationResult(
            DirectMessageOutcome.Ok,
            new SentDirectMessageResponse(conversation.Id, message.Id, message.SentAt));
    }

    private static ReplyToConversationResult Refused(DirectMessageOutcome outcome) => new(outcome, null);
}
