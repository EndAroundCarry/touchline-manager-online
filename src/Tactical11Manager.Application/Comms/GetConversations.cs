using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.Comms;

namespace Tactical11Manager.Application.Comms;

/// <summary>The result of reading the conversation list.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Conversations">The page, when the read succeeded.</param>
public sealed record GetConversationsResult(DirectMessageOutcome Outcome, ConversationListResponse? Conversations);

/// <summary>
/// Reads one page of the authenticated manager's conversations, most recently active first (`COM-10`).
/// </summary>
/// <remarks>
/// The reader is resolved from the account rather than named by the request, so a manager can only ever list
/// their own conversations (`INT-1`). The unread count comes back with the page because the list and the tab
/// badge are one screen.
/// </remarks>
public sealed class GetConversations
{
    private readonly IDirectMessageQueries _queries;
    private readonly IManagerRepository _managers;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetConversations(IDirectMessageQueries queries, IManagerRepository managers, IClock clock)
    {
        _queries = queries;
        _managers = managers;
        _clock = clock;
    }

    /// <summary>Reads a page of the manager's conversations.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cursor">Where to continue from, or null for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetConversationsResult> ExecuteAsync(
        Guid userId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!DirectMessageCursor.TryDecode(cursor, out var before))
        {
            return new GetConversationsResult(DirectMessageOutcome.InvalidCursor, null);
        }

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return new GetConversationsResult(DirectMessageOutcome.NoManagerProfile, null);
        }

        var page = await _queries.GetConversationsAsync(manager.Id, before, cancellationToken);
        var unread = await _queries.CountUnreadAsync(manager.Id, cancellationToken);

        var last = page.Conversations.Count == 0 ? null : page.Conversations[^1];

        return new GetConversationsResult(
            DirectMessageOutcome.Ok,
            new ConversationListResponse(
                [.. page.Conversations.Select(row => new ConversationSummaryResponse(
                    row.Id,
                    row.Subject,
                    row.Other.ToResponse(),
                    row.LastMessageAt,
                    row.LastMessagePreview,
                    row.UnreadCount,
                    row.BlockedByMe))],
                unread,
                page.HasMore && last is not null ? DirectMessageCursor.Encode(last.LastMessageAt, last.Id) : null,
                _clock.UtcNow));
    }
}
