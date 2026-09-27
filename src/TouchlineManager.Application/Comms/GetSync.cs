using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.Comms;

namespace TouchlineManager.Application.Comms;

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
    private readonly IManagerRepository _managers;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetSync(IInboxQueries queries, IManagerRepository managers, IClock clock)
    {
        _queries = queries;
        _managers = managers;
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

        return new GetSyncResult(new SyncResponse(_clock.UtcNow, unread));
    }
}
