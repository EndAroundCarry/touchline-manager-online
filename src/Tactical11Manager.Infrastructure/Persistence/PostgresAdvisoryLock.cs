using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.World;

namespace Tactical11Manager.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL advisory locks, scoped to the current transaction (`PYR-3`, ADR-0005), or held across one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AcquireAsync"/> uses <c>pg_advisory_xact_lock</c>, which is released automatically when the transaction ends, and is the one
/// to reach for. <see cref="HoldAsync"/> exists for the serializable transaction that must wait its turn before its snapshot is taken: it
/// holds a session lock on the unit of work's own connection, opened for the purpose, and releases it when disposed. A session lock that
/// leaked would outlive the work it guarded, so it is bounded twice over: the handle unlocks in its dispose, and a pooled connection is
/// reset with <c>DISCARD ALL</c>, which drops every advisory lock, before it is handed out again; a crashed process's connection closes
/// and takes its locks with it.
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
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the lock provider.</summary>
    public PostgresAdvisoryLock(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

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

    /// <inheritdoc />
    public async Task<IAsyncDisposable> HoldAsync(AdvisoryLockKey key, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A held lock must be taken before the transaction begins, or the transaction's snapshot predates the wait (ADR-0010).");
        }

        var scope = key.ToString();

        // Opened by hand, so EF Core keeps this one connection for the transaction that follows instead of closing it after the lock.
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await _dbContext.Database.ExecuteSqlAsync(
                $"select pg_advisory_lock(hashtextextended({scope}, 0))",
                cancellationToken);
        }
        catch
        {
            await _dbContext.Database.CloseConnectionAsync();

            throw;
        }

        return new HeldLock(_dbContext, scope);
    }

    /// <summary>A session lock held on the unit of work's connection, released when disposed.</summary>
    private sealed class HeldLock : IAsyncDisposable
    {
        private readonly Tactical11ManagerDbContext _dbContext;
        private readonly string _scope;

        public HeldLock(Tactical11ManagerDbContext dbContext, string scope)
        {
            _dbContext = dbContext;
            _scope = scope;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                // Not cancellable: a shutdown is exactly when the lock must still be given back.
                await _dbContext.Database.ExecuteSqlAsync(
                    $"select pg_advisory_unlock(hashtextextended({_scope}, 0))",
                    CancellationToken.None);
            }
            finally
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }
}
