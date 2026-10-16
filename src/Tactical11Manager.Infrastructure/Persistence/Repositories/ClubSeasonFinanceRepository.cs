using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The season finance summary's staging port. Summaries are records and are never updated
/// (master plan §6.8).
/// </summary>
internal sealed class ClubSeasonFinanceRepository : IClubSeasonFinanceRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public ClubSeasonFinanceRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(ClubSeasonFinance summary) => _dbContext.ClubSeasonFinances.Add(summary);

    /// <inheritdoc />
    public void AddLine(ClubSeasonFinanceLine line) => _dbContext.ClubSeasonFinanceLines.Add(line);
}
