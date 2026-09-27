using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Abstractions.Comms;

/// <summary>One stored message, as the inbox read returns it.</summary>
/// <param name="Id">The message.</param>
/// <param name="Category">The shelf it sits on.</param>
/// <param name="TemplateKey">The stable template that renders it.</param>
/// <param name="ParametersJson">The template's parameters.</param>
/// <param name="RelatedEntityId">The entity it is about, or null.</param>
/// <param name="CreatedAt">When it was written.</param>
/// <param name="ReadAt">When it was read, or null while unread.</param>
public sealed record InboxMessageRow(
    Guid Id,
    InboxCategory Category,
    string TemplateKey,
    string ParametersJson,
    Guid? RelatedEntityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>Where a page of messages starts, which is the last message the previous page returned.</summary>
/// <param name="CreatedAt">The instant the message was written.</param>
/// <param name="Id">The message's identity, which breaks a same-instant tie.</param>
public sealed record InboxCursorPosition(DateTimeOffset CreatedAt, Guid Id);

/// <summary>What a page of the inbox asks for.</summary>
/// <param name="Before">Where to continue from, or null for the first page.</param>
/// <param name="UnreadOnly">Whether to return unread messages only.</param>
public sealed record InboxPageQuery(InboxCursorPosition? Before, bool UnreadOnly);

/// <summary>One page of a manager's inbox, with the count that never pages (`F-41`).</summary>
/// <param name="Messages">The page, newest first.</param>
/// <param name="UnreadCount">How many of the manager's messages are unread.</param>
/// <param name="HasMore">Whether a further page exists.</param>
public sealed record InboxPage(IReadOnlyList<InboxMessageRow> Messages, int UnreadCount, bool HasMore);

/// <summary>
/// The read side of the inbox (master plan §6.9, §10.7).
/// </summary>
/// <remarks>
/// A projection, not an aggregate: one query per screen, returning rows rather than tracked entities, so the
/// screen can change without widening what a command can reach (`MOD-3`). The unread count is returned with
/// the page because the navigation badge and the list are one read on the screen that shows both.
/// </remarks>
public interface IInboxQueries
{
    /// <summary>The number of messages a page holds.</summary>
    int PageSize { get; }

    /// <summary>Reads one page of a manager's messages, newest first.</summary>
    /// <param name="managerId">The recipient.</param>
    /// <param name="query">What the page asks for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<InboxPage> GetInboxAsync(
        Guid managerId,
        InboxPageQuery query,
        CancellationToken cancellationToken);

    /// <summary>Counts a manager's unread messages, which is all the badge and the sync poll need.</summary>
    /// <param name="managerId">The recipient.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountUnreadAsync(Guid managerId, CancellationToken cancellationToken);
}
