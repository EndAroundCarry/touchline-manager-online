using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Application.Abstractions.Comms;

/// <summary>
/// The outbox's persistence: the intentions a domain transaction records, and the due ones a dispatcher
/// drains (`MOD-4`, `COM-4`).
/// </summary>
/// <remarks>
/// It stages and never saves, so an outbox row commits in the same transaction as the change that caused it.
/// The load is a bounded read of the due rows, ordered so a dispatcher drains them oldest first.
/// </remarks>
public interface IOutboxRepository
{
    /// <summary>Stages a message.</summary>
    /// <param name="message">The message.</param>
    void Add(OutboxMessage message);

    /// <summary>Loads the pending messages that are due, tracked, so a dispatcher can mark them.</summary>
    /// <param name="batch">The most rows to load in one pass.</param>
    /// <param name="now">The instant to compare <c>due_at</c> against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OutboxMessage>> LoadDueAsync(
        int batch,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
