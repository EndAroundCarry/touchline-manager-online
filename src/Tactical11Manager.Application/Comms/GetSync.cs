using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.Comms;

namespace Tactical11Manager.Application.Comms;

/// <summary>The result of polling the synchronization summary.</summary>
/// <param name="Sync">The summary.</param>
public sealed record GetSyncResult(SyncResponse Sync);

/// <summary>
/// Reads the lightweight synchronization summary the shell polls (master plan §10.7, §11.2, ADR-0007).
/// </summary>
/// <remarks>
/// Deliberately lenient: an account with no manager profile, or a world that has not been seeded, has an
/// unread count of zero rather than a refusal. The poll runs in the background of every screen, and a
/// refusal there would surface as an error for a manager who simply has nothing in their inbox yet. That is
/// the opposite of the inbox read, which is a resource the caller asked for and is refused by name.
/// </remarks>
public sealed class GetSync
{
    private readonly IInboxQueries _queries;
    private readonly IDirectMessageQueries _directMessages;
    private readonly IManagerRepository _managers;
    private readonly IReadOnlyMode _readOnlyMode;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetSync(
        IInboxQueries queries,
        IDirectMessageQueries directMessages,
        IManagerRepository managers,
        IReadOnlyMode readOnlyMode,
        IClock clock)
    {
        _queries = queries;
        _directMessages = directMessages;
        _managers = managers;
        _readOnlyMode = readOnlyMode;
        _clock = clock;
    }

    /// <summary>Reads the summary for an account.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetSyncResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        var unread = manager is null
            ? 0
            : await _queries.CountUnreadAsync(manager.Id, cancellationToken);

        // Direct messages from other managers are counted apart from the game's own (`COM-16`), and the poll
        // asks the same way: no profile, no messages.
        var unreadDirect = manager is null
            ? 0
            : await _directMessages.CountUnreadAsync(manager.Id, cancellationToken);

        // Read-only mode travels on the same poll so the shell can disable commands between requests
        // (`F-51`); the server gate remains the backstop for a client that has not caught up.
        var readOnly = await _readOnlyMode.GetStateAsync(cancellationToken);

        return new GetSyncResult(new SyncResponse(
            _clock.UtcNow,
            unread,
            readOnly.Enabled,
            readOnly.Message,
            unreadDirect));
    }
}
