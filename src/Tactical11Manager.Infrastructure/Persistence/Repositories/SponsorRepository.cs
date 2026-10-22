using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The sponsor deals' and standing steps' staging port (`SPN-2`, `STD-3`).
/// </summary>
internal sealed class SponsorRepository : ISponsorRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public SponsorRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubSponsorDeal>> LoadDealsAsync(
        Guid seasonId,
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken) =>
        await _dbContext.SponsorDeals
            .Where(deal => deal.SeasonId == seasonId && clubIds.Contains(deal.ClubId))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void AddStandingEvent(ClubStandingEvent standingEvent) => _dbContext.ClubStandingEvents.Add(standingEvent);
}
