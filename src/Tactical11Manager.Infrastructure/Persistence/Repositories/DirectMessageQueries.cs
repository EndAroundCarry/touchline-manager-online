using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The reads behind the Managers tab of the inbox: the conversation list, a thread, and the unread count
/// (`COM-10` to `COM-16`).
/// </summary>
/// <remarks>
/// <para>
/// Both lists are keyset walks for the reason the inbox's is: they grow at the top, so an offset would skip or
/// repeat rows whenever a message arrived between two pages. Each fetches one row more than it returns, and
/// the identity breaks a same-instant tie.
/// </para>
/// <para>
/// The page is read first and its names, clubs, previews, counts and blocks afterwards, each as one query over
/// the page's identities. That is a handful of small queries rather than one wide join, so the keyset query
/// stays a plain index walk and no part of it depends on how a window function over a join translates.
/// </para>
/// </remarks>
internal sealed class DirectMessageQueries : IDirectMessageQueries
{
    /// <summary>How much of the latest message the list shows.</summary>
    private const int PreviewLength = 120;

    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public DirectMessageQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<ConversationPage> GetConversationsAsync(
        Guid managerId,
        DirectMessageCursorPosition? before,
        CancellationToken cancellationToken)
    {
        var conversations = _dbContext.Conversations
            .Where(conversation => conversation.StartedByManagerId == managerId
                || conversation.RecipientManagerId == managerId);

        if (before is { } position)
        {
            conversations = conversations.Where(conversation =>
                conversation.LastMessageAt < position.At
                || (conversation.LastMessageAt == position.At && conversation.Id.CompareTo(position.Id) < 0));
        }

        var page = await conversations
            .OrderByDescending(conversation => conversation.LastMessageAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(DirectMessageRules.ConversationPageSize + 1)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > DirectMessageRules.ConversationPageSize;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        if (page.Count == 0)
        {
            return new ConversationPage([], HasMore: false);
        }

        var conversationIds = page.Select(conversation => conversation.Id).ToList();
        var otherIds = page.Select(conversation => conversation.OtherParticipant(managerId)).Distinct().ToList();

        var others = await ReadParticipantsAsync(otherIds, cancellationToken);
        var blockedByMe = await ReadBlockedByAsync(managerId, otherIds, cancellationToken);

        var unread = await _dbContext.DirectMessages
            .Where(message => message.RecipientManagerId == managerId
                && message.ReadAt == null
                && conversationIds.Contains(message.ConversationId))
            .GroupBy(message => message.ConversationId)
            .Select(group => new { ConversationId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ConversationId, row => row.Count, cancellationToken);

        var latest = await _dbContext.DirectMessages
            .Where(message => conversationIds.Contains(message.ConversationId))
            .GroupBy(message => message.ConversationId)
            .Select(group => group
                .OrderByDescending(message => message.SentAt)
                .ThenByDescending(message => message.Id)
                .Select(message => new { message.ConversationId, message.Body })
                .First())
            .ToDictionaryAsync(row => row.ConversationId, row => row.Body, cancellationToken);

        var rows = page
            .Select(conversation =>
            {
                var otherId = conversation.OtherParticipant(managerId);

                return new ConversationRow(
                    conversation.Id,
                    conversation.Subject,
                    others[otherId],
                    conversation.LastMessageAt,
                    latest.TryGetValue(conversation.Id, out var body) ? Preview(body) : null,
                    unread.GetValueOrDefault(conversation.Id),
                    blockedByMe.Contains(otherId));
            })
            .ToList();

        return new ConversationPage(rows, hasMore);
    }

    /// <inheritdoc />
    public async Task<ConversationThreadPage?> GetThreadAsync(
        Guid managerId,
        Guid conversationId,
        DirectMessageCursorPosition? before,
        CancellationToken cancellationToken)
    {
        var conversation = await _dbContext.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == conversationId
                    && (candidate.StartedByManagerId == managerId || candidate.RecipientManagerId == managerId),
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        var messages = _dbContext.DirectMessages.Where(message => message.ConversationId == conversationId);

        if (before is { } position)
        {
            messages = messages.Where(message =>
                message.SentAt < position.At
                || (message.SentAt == position.At && message.Id.CompareTo(position.Id) < 0));
        }

        var page = await messages
            .OrderByDescending(message => message.SentAt)
            .ThenByDescending(message => message.Id)
            .Take(DirectMessageRules.ThreadPageSize + 1)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > DirectMessageRules.ThreadPageSize;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var otherId = conversation.OtherParticipant(managerId);
        var other = (await ReadParticipantsAsync([otherId], cancellationToken))[otherId];

        var blocks = await _dbContext.ManagerBlocks
            .Where(block => (block.BlockerManagerId == managerId && block.BlockedManagerId == otherId)
                || (block.BlockerManagerId == otherId && block.BlockedManagerId == managerId))
            .Select(block => block.BlockerManagerId)
            .ToListAsync(cancellationToken);

        return new ConversationThreadPage(
            conversation.Id,
            conversation.Subject,
            other,
            page
                .Select(message => new DirectMessageRow(
                    message.Id,
                    message.SenderManagerId == managerId,
                    message.Body,
                    message.SentAt,
                    message.ReadAt))
                .ToList(),
            hasMore,
            BlockedByMe: blocks.Contains(managerId),
            BlockedMe: blocks.Contains(otherId));
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(Guid managerId, CancellationToken cancellationToken) =>
        _dbContext.DirectMessages.CountAsync(
            message => message.RecipientManagerId == managerId && message.ReadAt == null,
            cancellationToken);

    private static string Preview(string body)
    {
        // A preview is one line: the list shows it as a snippet, and a line break would push the rest down.
        var line = body.ReplaceLineEndings(" ");

        return line.Length <= PreviewLength ? line : line[..PreviewLength].TrimEnd() + "…";
    }

    /// <summary>Reads each manager's display name and the club they hold now.</summary>
    private async Task<Dictionary<Guid, DirectMessageParticipantRow>> ReadParticipantsAsync(
        IReadOnlyCollection<Guid> managerIds,
        CancellationToken cancellationToken)
    {
        var names = await (
            from manager in _dbContext.Managers
            join user in _dbContext.Users on manager.UserId equals user.Id
            where managerIds.Contains(manager.Id)
            select new { manager.Id, user.DisplayName })
            .ToListAsync(cancellationToken);

        // A manager holds at most one open tenure, but the newest wins if data ever says otherwise.
        var clubs = await (
            from tenure in _dbContext.ClubTenures
            join club in _dbContext.Clubs on tenure.ClubId equals club.Id
            where managerIds.Contains(tenure.ManagerId)
                && tenure.ControlStatus != ClubTenureControlStatus.Closed
            orderby tenure.StartedAt descending
            select new { tenure.ManagerId, ClubId = club.Id, ClubName = club.Name })
            .ToListAsync(cancellationToken);

        var clubByManager = clubs
            .GroupBy(row => row.ManagerId)
            .ToDictionary(group => group.Key, group => group.First());

        return names.ToDictionary(
            row => row.Id,
            row => clubByManager.TryGetValue(row.Id, out var club)
                ? new DirectMessageParticipantRow(row.Id, row.DisplayName, club.ClubId, club.ClubName)
                : new DirectMessageParticipantRow(row.Id, row.DisplayName, null, null));
    }

    /// <summary>Reads which of the managers the reader has blocked.</summary>
    private async Task<HashSet<Guid>> ReadBlockedByAsync(
        Guid blockerId,
        IReadOnlyCollection<Guid> managerIds,
        CancellationToken cancellationToken) =>
        (await _dbContext.ManagerBlocks
            .Where(block => block.BlockerManagerId == blockerId && managerIds.Contains(block.BlockedManagerId))
            .Select(block => block.BlockedManagerId)
            .ToListAsync(cancellationToken))
        .ToHashSet();
}
