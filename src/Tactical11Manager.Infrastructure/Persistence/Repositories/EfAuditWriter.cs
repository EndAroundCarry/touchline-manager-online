using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Infrastructure.Persistence.Entities;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stages audit rows in the current unit of work so they commit with the change they describe.
/// </summary>
/// <remarks>
/// Writing the audit row in the same transaction as the state change is what makes "an action with no
/// audit trail" impossible rather than merely discouraged (master plan §12.3).
/// </remarks>
internal sealed class EfAuditWriter : IAuditWriter
{
    private readonly Tactical11ManagerDbContext _dbContext;
    private readonly IClock _clock;

    /// <summary>Initializes the writer.</summary>
    public EfAuditWriter(Tactical11ManagerDbContext dbContext, IClock clock)
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
