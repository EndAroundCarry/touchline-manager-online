using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;

namespace Tactical11Manager.Application.Comms;

/// <summary>
/// Marks every message in a conversation that is addressed to the caller as read (`COM-16`).
/// </summary>
/// <remarks>
/// Only the caller's own messages are loaded, and marking an already-read message is a no-op, which is what
/// makes a retried command safe: it cannot move a read time that was already recorded. A conversation the
/// caller is not part of is answered as one that does not exist (threat I-5).
/// </remarks>
public sealed class MarkConversationRead
{
    private readonly IDirectMessageRepository _messages;
    private readonly IManagerRepository _managers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the command.</summary>
    public MarkConversationRead(
        IDirectMessageRepository messages,
        IManagerRepository managers,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _messages = messages;
        _managers = managers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Marks the conversation read.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DirectMessageOutcome> ExecuteAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return DirectMessageOutcome.NoManagerProfile;
        }

        if (await _messages.FindConversationAsync(manager.Id, conversationId, cancellationToken) is null)
        {
            return DirectMessageOutcome.ConversationNotFound;
        }

        var unread = await _messages.LoadUnreadAsync(manager.Id, conversationId, cancellationToken);

        if (unread.Count == 0)
        {
            return DirectMessageOutcome.Ok;
        }

        var now = _clock.UtcNow;

        foreach (var message in unread)
        {
            message.MarkRead(now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return DirectMessageOutcome.Ok;
    }
}
