namespace TouchlineManager.Application.Abstractions.Persistence;

/// <summary>
/// Commits the unit of work for one request or job.
/// </summary>
/// <remarks>
/// All modules share one <c>DbContext</c>, so a workflow that spans modules (takeover, auction
/// resolution, matchday publication) is a single local transaction (ADR-0001). A use case performs
/// every state change it needs and then calls <see cref="SaveChangesAsync"/> once, which is what
/// makes those workflows all-or-nothing.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Persists every pending change in one transaction.</summary>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
