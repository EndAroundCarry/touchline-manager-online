using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.Comms;
using Tactical11Manager.Domain.Comms;

namespace Tactical11Manager.Application.Comms;

/// <summary>The result of starting a conversation.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Sent">The conversation and its first message, when it was started.</param>
public sealed record StartConversationResult(DirectMessageOutcome Outcome, SentDirectMessageResponse? Sent);

/// <summary>
/// Starts a conversation with the manager who runs a club, from that club's page (`COM-10`, `COM-11`,
/// `COM-13`, `COM-15`).
/// </summary>
/// <remarks>
/// <para>
/// The recipient is never named by the request. The sender names a club, and the person it is addressed to is
/// whoever holds that club's open tenure at this moment, so a stale page cannot send to a manager who has
/// since left it. The conversation is then addressed to that person and outlives the tenure (`COM-10`).
/// </para>
/// <para>
/// The checks run from the cheapest and most certain to the ones that depend on the other side: the text, the
/// sender's own standing, the club, and last the recipient and any block. A block is reported as a block
/// because the plan is to tell the sender plainly that they are not being listened to (`COM-15`).
/// </para>
/// </remarks>
public sealed class StartConversation
{
    private readonly IDirectMessageRepository _messages;
    private readonly IManagerRepository _managers;
    private readonly IClubTenureRepository _tenures;
    private readonly IClubRepository _clubs;
    private readonly IClubManagerLookup _clubManagers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the command.</summary>
    public StartConversation(
        IDirectMessageRepository messages,
        IManagerRepository managers,
        IClubTenureRepository tenures,
        IClubRepository clubs,
        IClubManagerLookup clubManagers,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _messages = messages;
        _managers = managers;
        _tenures = tenures;
        _clubs = clubs;
        _clubManagers = clubManagers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Starts the conversation and sends its first message.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="clubId">The club page the conversation starts from.</param>
    /// <param name="subject">What the sender typed as the subject.</param>
    /// <param name="body">What the sender typed as the first message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<StartConversationResult> ExecuteAsync(
        Guid userId,
        Guid clubId,
        string? subject,
        string? body,
        CancellationToken cancellationToken)
    {
        var normalizedSubject = DirectMessageRules.NormalizeSubject(subject);
        var normalizedBody = DirectMessageRules.NormalizeBody(body);

        if (normalizedSubject is null || normalizedBody is null)
        {
            return Refused(DirectMessageOutcome.InvalidText);
        }

        var sender = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (sender is null)
        {
            return Refused(DirectMessageOutcome.NoManagerProfile);
        }

        var senderTenure = await _tenures.FindOpenByManagerAsync(sender.Id, cancellationToken);

        if (senderTenure is null)
        {
            return Refused(DirectMessageOutcome.SenderHasNoClub);
        }

        // Club identity is public game data, so saying a club does not exist discloses nothing.
        if (await _clubs.FindAsync(clubId, cancellationToken) is null)
        {
            return Refused(DirectMessageOutcome.ClubNotFound);
        }

        if (senderTenure.ClubId == clubId)
        {
            return Refused(DirectMessageOutcome.OwnClub);
        }

        var recipient = await _clubManagers.FindByClubAsync(clubId, cancellationToken);

        if (recipient is null)
        {
            return Refused(DirectMessageOutcome.ClubHasNoManager);
        }

        if (recipient.AccountClosing)
        {
            return Refused(DirectMessageOutcome.RecipientUnavailable);
        }

        if (await _messages.IsBlockedBetweenAsync(sender.Id, recipient.ManagerId, cancellationToken))
        {
            return Refused(DirectMessageOutcome.Blocked);
        }

        var now = _clock.UtcNow;

        var conversation = Conversation.Start(
            Guid.CreateVersion7(),
            normalizedSubject,
            sender.Id,
            recipient.ManagerId,
            clubId,
            now);

        var message = DirectMessage.Write(
            Guid.CreateVersion7(),
            conversation.Id,
            sender.Id,
            recipient.ManagerId,
            normalizedBody,
            now);

        _messages.Add(conversation);
        _messages.Add(message);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new StartConversationResult(
            DirectMessageOutcome.Ok,
            new SentDirectMessageResponse(conversation.Id, message.Id, message.SentAt));
    }

    private static StartConversationResult Refused(DirectMessageOutcome outcome) => new(outcome, null);
}
