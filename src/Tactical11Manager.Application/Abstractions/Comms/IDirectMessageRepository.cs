using Tactical11Manager.Domain.Comms;

namespace Tactical11Manager.Application.Abstractions.Comms;

/// <summary>
/// The comms module's staging port for direct messages between managers (`COM-10` to `COM-16`).
/// </summary>
/// <remarks>
/// It stages and never saves, so a start or a reply commits as one transaction with the conversation's
/// bumped timestamp. Every finder is scoped to a participant: a conversation a manager is not part of is
/// simply not found, which keeps a guessed identity from confirming that a conversation exists.
/// </remarks>
public interface IDirectMessageRepository
{
    /// <summary>Stages a new conversation.</summary>
    /// <param name="conversation">The conversation.</param>
    void Add(Conversation conversation);

    /// <summary>Stages a new message.</summary>
    /// <param name="message">The message.</param>
    void Add(DirectMessage message);

    /// <summary>Stages a block.</summary>
    /// <param name="block">The block.</param>
    void Add(ManagerBlock block);

    /// <summary>Stages the removal of a block.</summary>
    /// <param name="block">The block, as loaded by <see cref="FindBlockAsync"/>.</param>
    void Remove(ManagerBlock block);

    /// <summary>Finds a conversation the manager is part of, tracked, so a reply can bump it.</summary>
    /// <param name="managerId">A participant.</param>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Conversation?> FindConversationAsync(
        Guid managerId,
        Guid conversationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the messages in a conversation that are addressed to the manager and still unread, tracked, so
    /// opening the thread can mark them read (`COM-16`).
    /// </summary>
    /// <param name="managerId">The recipient.</param>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DirectMessage>> LoadUnreadAsync(
        Guid managerId,
        Guid conversationId,
        CancellationToken cancellationToken);

    /// <summary>Finds the block one manager has placed on another, tracked, or null when there is none.</summary>
    /// <param name="blockerManagerId">The manager who blocked.</param>
    /// <param name="blockedManagerId">The manager who was blocked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ManagerBlock?> FindBlockAsync(
        Guid blockerManagerId,
        Guid blockedManagerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether a block exists between two managers in either direction, which is what stops a start or
    /// a reply (`COM-15`).
    /// </summary>
    /// <param name="firstManagerId">One manager.</param>
    /// <param name="secondManagerId">The other.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsBlockedBetweenAsync(
        Guid firstManagerId,
        Guid secondManagerId,
        CancellationToken cancellationToken);
}
