using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Comms;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The inbox's reads (master plan §6.9, §10.7).
/// </summary>
/// <remarks>
/// <para>
/// A keyset walk rather than an offset: the inbox grows from the top, so an offset would skip or repeat
/// messages whenever one arrived between two pages. The page fetches one row more than it returns so the
/// presence of a next page is a fact rather than a guess, and the cursor is the last row it kept.
/// </para>
/// <para>
/// The unread count is read with the page because the list and the badge are one screen, and it is the one
/// read the shell's poll repeats — hence the partial index the query is written against.
/// </para>
/// </remarks>
internal sealed class InboxQueries : IInboxQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public InboxQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public int PageSize => 25;

    /// <inheritdoc />
    public async Task<InboxPage> GetInboxAsync(
        Guid managerId,
        InboxPageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var messages = _dbContext.InboxMessages
            .Where(message => message.RecipientManagerId == managerId);

        if (query.UnreadOnly)
        {
            messages = messages.Where(message => message.ReadAt == null);
        }

        if (query.Before is { } before)
        {
            // The identity breaks a same-instant tie, which a round produces: every message a publication
            // writes carries the one instant it ran at, so the instant alone is not a total order.
            messages = messages.Where(message =>
                message.CreatedAt < before.CreatedAt
                || (message.CreatedAt == before.CreatedAt && message.Id.CompareTo(before.Id) < 0));
        }

        var rows = await messages
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(PageSize + 1)
            .Select(message => new InboxMessageRow(
                message.Id,
                message.Category,
                message.TemplateKey,
                message.ParametersJson,
                message.RelatedEntityId,
                message.CreatedAt,
                message.ReadAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > PageSize;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new InboxPage(rows, await CountUnreadAsync(managerId, cancellationToken), hasMore);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(Guid managerId, CancellationToken cancellationToken) =>
        _dbContext.InboxMessages.CountAsync(
            message => message.RecipientManagerId == managerId && message.ReadAt == null,
            cancellationToken);
}
