using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The EF Core unit of work.
/// </summary>
/// <remarks>
/// EF Core already wraps all pending changes of one <c>SaveChanges</c> in a single transaction, so
/// this type's real job is translating a concurrency failure into the application's own exception
/// type — keeping EF Core out of the application layer's vocabulary (ADR-0009).
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
}
