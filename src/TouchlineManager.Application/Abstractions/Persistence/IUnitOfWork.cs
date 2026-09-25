namespace TouchlineManager.Application.Abstractions.Persistence;

/// <summary>
/// Commits the unit of work for one request or job.
/// </summary>
/// <remarks>
/// <para>
/// All modules share one <c>DbContext</c>, so a workflow that spans modules (takeover, auction
/// resolution, matchday publication) is a single local transaction (ADR-0001). A use case performs
/// every state change it needs and then calls <see cref="SaveChangesAsync"/> once, which is what
/// makes those workflows all-or-nothing.
/// </para>
/// <para>
/// A workflow that must also serialise against a concurrent one begins an explicit transaction. Takeover
/// is the first: it holds a country-scoped advisory lock, and an advisory lock taken outside a transaction
/// is released at once, which is the same as not taking it at all (`PYR-3`, ADR-0005).
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Persists every pending change in one transaction.</summary>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens an explicit transaction. Every later <see cref="SaveChangesAsync"/> in the same scope joins
    /// it until it is committed or rolled back.
    /// </summary>
    /// <param name="isolation">The isolation level the workflow needs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IDatabaseTransaction> BeginTransactionAsync(
        TransactionIsolation isolation,
        CancellationToken cancellationToken);
}

/// <summary>The isolation levels the application asks for, named for what a workflow needs.</summary>
public enum TransactionIsolation
{
    /// <summary>PostgreSQL's default. Enough for a write that does not have to serialise against a peer.</summary>
    ReadCommitted = 0,

    /// <summary>
    /// For workflows that read a count, decide from it, and write a row that must not duplicate a peer's:
    /// club takeover, provisioning, and bid resolution (`CONC-1`).
    /// </summary>
    Serializable = 1,
}

/// <summary>An open database transaction.</summary>
/// <remarks>
/// Disposing without committing rolls back, which is the safe default for a transaction abandoned because
/// an exception unwound the call stack.
/// </remarks>
public interface IDatabaseTransaction : IAsyncDisposable
{
    /// <summary>Makes the transaction's changes durable.</summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Discards the transaction's changes.</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}
