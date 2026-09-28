using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The club ledger's append-only write. Entries are staged and never edited (master plan §6.8, `FIN-12`).
/// </summary>
internal sealed class LedgerRepository : ILedgerRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public LedgerRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(LedgerEntry entry) => _dbContext.LedgerEntries.Add(entry);

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FindExistingCorrelationIdsAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken cancellationToken)
    {
        var found = await _dbContext.LedgerEntries
            .Where(entry => correlationIds.Contains(entry.CorrelationId))
            .Select(entry => entry.CorrelationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return found.ToHashSet(StringComparer.Ordinal);
    }
}
