using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stages audit rows in the current unit of work so they commit with the change they describe.
/// </summary>
/// <remarks>
/// Writing the audit row in the same transaction as the state change is what makes "an action with no
/// audit trail" impossible rather than merely discouraged (master plan §12.3).
/// </remarks>
internal sealed class EfAuditWriter : IAuditWriter
{
    private readonly TouchlineManagerDbContext _dbContext;
    private readonly IClock _clock;

    /// <summary>Initializes the writer.</summary>
    public EfAuditWriter(TouchlineManagerDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _dbContext.AuditEntries.Add(new OpsAuditEntry
        {
            Id = Guid.CreateVersion7(),
            ActorType = entry.ActorType,
            ActorUserId = entry.ActorUserId,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            CorrelationId = entry.CorrelationId,
            OccurredAt = _clock.UtcNow,
            IpHash = entry.IpHash,
            Reason = entry.Reason,
        });
    }
}
