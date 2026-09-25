using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// Club-account persistence. Opens accounts for generated clubs; the ledger arrives in Stage 9.
/// </summary>
internal sealed class ClubAccountRepository : IClubAccountRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public ClubAccountRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(ClubAccount account) => _dbContext.ClubAccounts.Add(account);

    /// <inheritdoc />
    public Task<ClubAccount?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
        _dbContext.ClubAccounts.SingleOrDefaultAsync(account => account.ClubId == clubId, cancellationToken);
}
