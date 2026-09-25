using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.World;

namespace TouchlineManager.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL advisory locks, scoped to the current transaction (`PYR-3`, ADR-0005).
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>pg_advisory_xact_lock</c>, which is released automatically when the transaction ends. A
/// session-scoped lock would survive a crash until the pooled connection was closed, and a leaked lock that
/// nobody can see is worse than the race it was preventing.
/// </para>
/// <para>
/// The key is the 64-bit hash of a scope and an identity rather than a table row, so a lock can be taken
/// before the row it guards exists — which is exactly the case for a provisioning request created because a
/// tier filled up. Collisions between two different keys are possible in principle and harmless in practice:
/// the effect of a collision is that two unrelated decisions serialise, not that either is wrong.
/// </para>
/// </remarks>
internal sealed class PostgresAdvisoryLock : IAdvisoryLock
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the lock provider.</summary>
    public PostgresAdvisoryLock(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task AcquireAsync(AdvisoryLockKey key, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An advisory lock must be taken inside a transaction, or it is released as soon as it is "
                + "taken. Call IUnitOfWork.BeginTransactionAsync first (ADR-0005).");
        }

        var scope = key.ToString();

        await _dbContext.Database.ExecuteSqlAsync(
            $"select pg_advisory_xact_lock(hashtextextended({scope}, 0))",
            cancellationToken);
    }
}
