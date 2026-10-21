using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.Comms;

namespace Tactical11Manager.Application.Comms;

/// <summary>The result of reading a thread.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Conversation">The page, when the read succeeded.</param>
public sealed record GetConversationResult(DirectMessageOutcome Outcome, ConversationResponse? Conversation);

/// <summary>
/// Reads one page of a conversation's messages, with whether the reader can reply (`COM-10`, `COM-12`,
/// `COM-15`).
/// </summary>
/// <remarks>
/// <para>
/// A read changes nothing. Opening a thread marks its messages read through a command of its own
/// (`COM-16`), so a prefetch or a reload cannot clear an unread count by accident, and the read stays safe to
/// repeat.
/// </para>
/// <para>
/// A conversation the reader is not part of is answered as one that does not exist, never as forbidden, so a
/// guessed identity confirms nothing (threat I-5).
/// </para>
/// </remarks>
public sealed class GetConversation
{
    private readonly IDirectMessageQueries _queries;
    private readonly IManagerRepository _managers;
    private readonly IClubManagerLookup _clubManagers;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetConversation(
        IDirectMessageQueries queries,
        IManagerRepository managers,
        IClubManagerLookup clubManagers,
        IClock clock)
    {
        _queries = queries;
        _managers = managers;
        _clubManagers = clubManagers;
        _clock = clock;
    }

    /// <summary>Reads a page of the thread.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cursor">Where to continue towards older messages, or null for the newest page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetConversationResult> ExecuteAsync(
        Guid userId,
        Guid conversationId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!DirectMessageCursor.TryDecode(cursor, out var before))
        {
            return new GetConversationResult(DirectMessageOutcome.InvalidCursor, null);
        }

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return new GetConversationResult(DirectMessageOutcome.NoManagerProfile, null);
        }

        var page = await _queries.GetThreadAsync(manager.Id, conversationId, before, cancellationToken);

        if (page is null)
        {
            return new GetConversationResult(DirectMessageOutcome.ConversationNotFound, null);
        }

        var closing = await _clubManagers.IsAccountClosingAsync(page.Other.ManagerId, cancellationToken);

        // A block you placed is the reason you can act on, so it is named before one placed on you.
        var reason = page.BlockedByMe ? DirectMessageMapping.ReplyBlockedByMe
            : page.BlockedMe ? DirectMessageMapping.ReplyBlockedByThem
            : closing ? DirectMessageMapping.ReplyAccountClosing
            : null;

        var last = page.Messages.Count == 0 ? null : page.Messages[^1];

        return new GetConversationResult(
            DirectMessageOutcome.Ok,
            new ConversationResponse(
                page.Id,
                page.Subject,
                page.Other.ToResponse(),
                [.. page.Messages.Select(row => row.ToResponse())],
                page.HasMore && last is not null ? DirectMessageCursor.Encode(last.SentAt, last.Id) : null,
                page.BlockedByMe,
                reason is null,
                reason,
                _clock.UtcNow));
    }
}
