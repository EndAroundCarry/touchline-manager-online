using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The outbox's persistence (`MOD-4`, master plan §6.9).
/// </summary>
/// <remarks>
/// A module of its own with a port of its own (`MOD-1`). It stages and never saves, so an outbox row commits
/// with the domain change that caused it; the load is a bounded read of the due rows, ordered so a dispatcher
/// drains them oldest first and a stuck row cannot starve the ones behind it.
/// </remarks>
internal sealed class OutboxRepository : IOutboxRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public OutboxRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(OutboxMessage message) => _dbContext.OutboxMessages.Add(message);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> LoadDueAsync(
        int batch,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await _dbContext.OutboxMessages
            .Where(message => message.Status == OutboxMessageStatus.Pending && message.DueAt <= now)
            .OrderBy(message => message.DueAt)
            .ThenBy(message => message.Id)
            .Take(batch)
            .ToListAsync(cancellationToken);
}
