using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Domain.Comms;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The comms module's staging of direct messages between managers (`COM-10` to `COM-16`).
/// </summary>
/// <remarks>
/// It stages and never saves, so a reply and the conversation's bumped timestamp commit together. Every
/// finder takes the participant, so a conversation belonging to two other managers cannot be loaded by a
/// guessed identity.
/// </remarks>
internal sealed class DirectMessageRepository : IDirectMessageRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public DirectMessageRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(Conversation conversation) => _dbContext.Conversations.Add(conversation);

    /// <inheritdoc />
    public void Add(DirectMessage message) => _dbContext.DirectMessages.Add(message);

    /// <inheritdoc />
    public void Add(ManagerBlock block) => _dbContext.ManagerBlocks.Add(block);

    /// <inheritdoc />
    public void Remove(ManagerBlock block) => _dbContext.ManagerBlocks.Remove(block);

    /// <inheritdoc />
    public Task<Conversation?> FindConversationAsync(
        Guid managerId,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        _dbContext.Conversations.FirstOrDefaultAsync(
            conversation => conversation.Id == conversationId
                && (conversation.StartedByManagerId == managerId || conversation.RecipientManagerId == managerId),
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectMessage>> LoadUnreadAsync(
        Guid managerId,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        await _dbContext.DirectMessages
            .Where(message => message.ConversationId == conversationId
                && message.RecipientManagerId == managerId
                && message.ReadAt == null)
            .OrderBy(message => message.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ManagerBlock?> FindBlockAsync(
        Guid blockerManagerId,
        Guid blockedManagerId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagerBlocks.FirstOrDefaultAsync(
            block => block.BlockerManagerId == blockerManagerId && block.BlockedManagerId == blockedManagerId,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsBlockedBetweenAsync(
        Guid firstManagerId,
        Guid secondManagerId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagerBlocks.AnyAsync(
            block => (block.BlockerManagerId == firstManagerId && block.BlockedManagerId == secondManagerId)
                || (block.BlockerManagerId == secondManagerId && block.BlockedManagerId == firstManagerId),
            cancellationToken);
}
