using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The EF Core unit of work.
/// </summary>
/// <remarks>
/// EF Core already wraps the pending changes of one <c>SaveChanges</c> in a transaction, so this type's
/// job is twofold: translate a concurrency failure into the application's own exception type — keeping
/// EF Core out of the application layer's vocabulary (ADR-0009) — and open the explicit transaction a
/// workflow needs when it must hold a lock across more than one save.
/// </remarks>
internal sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the unit of work.</summary>
    public EfUnitOfWork(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                "The record changed after it was read. Reload and reapply the change.",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<IDatabaseTransaction> BeginTransactionAsync(
        TransactionIsolation isolation,
        CancellationToken cancellationToken)
    {
        var level = isolation == TransactionIsolation.Serializable
            ? IsolationLevel.Serializable
            : IsolationLevel.ReadCommitted;

        var transaction = await _dbContext.Database.BeginTransactionAsync(level, cancellationToken);

        return new EfDatabaseTransaction(transaction);
    }
}

/// <summary>An open EF Core transaction, exposed to the application as a plain commit-or-discard.</summary>
internal sealed class EfDatabaseTransaction : IDatabaseTransaction
{
    private readonly IDbContextTransaction _transaction;

    /// <summary>Initializes the wrapper.</summary>
    public EfDatabaseTransaction(IDbContextTransaction transaction) => _transaction = transaction;

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken) => _transaction.RollbackAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
