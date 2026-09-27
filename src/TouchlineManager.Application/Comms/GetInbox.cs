using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>The result of reading a manager's inbox.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Inbox">The page, when the read succeeded.</param>
public sealed record GetInboxResult(CommsOutcome Outcome, InboxResponse? Inbox);

/// <summary>
/// Reads one page of the authenticated manager's inbox (master plan §10.7, F-41).
/// </summary>
/// <remarks>
/// The recipient is resolved from the account rather than named by the request, so a manager can only ever
/// read their own messages (`INT-1`, §10.9). A page is keyset-paginated because the inbox grows from the
/// top; the unread count comes back with it because the list and the badge are one screen.
/// </remarks>
public sealed class GetInbox
{
    private readonly IInboxQueries _queries;
    private readonly IWorldRepository _world;
    private readonly IManagerRepository _managers;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetInbox(
        IInboxQueries queries,
        IWorldRepository world,
        IManagerRepository managers,
        IClock clock)
    {
        _queries = queries;
        _world = world;
        _managers = managers;
        _clock = clock;
    }

    /// <summary>Reads a page of the manager's inbox.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cursor">Where to continue from, or null for the first page.</param>
    /// <param name="unreadOnly">Whether to return unread messages only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetInboxResult> ExecuteAsync(
        Guid userId,
        string? cursor,
        bool unreadOnly,
        CancellationToken cancellationToken)
    {
        if (!InboxCursor.TryDecode(cursor, out var before))
        {
            return new GetInboxResult(CommsOutcome.InvalidCursor, null);
        }

        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return new GetInboxResult(CommsOutcome.WorldNotSeeded, null);
        }

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return new GetInboxResult(CommsOutcome.NoManagerProfile, null);
        }

        var page = await _queries.GetInboxAsync(
            manager.Id,
            new InboxPageQuery(before, unreadOnly),
            cancellationToken);

        return new GetInboxResult(CommsOutcome.Ok, page.ToResponse(_clock.UtcNow));
    }
}
