using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The squad module's absences: what the matchday publication serves and opens (`TRN-12`, `DIS-5`).
/// </summary>
/// <remarks>
/// Tracked records, because the publication mutates them — an absence is served one fixture by the round
/// that published it — and a row copy would move <see cref="PlayerUnavailability.ServeFixture"/> out of the
/// domain. It stages and never saves, so the served absences commit with the results that served them.
/// </remarks>
internal sealed class AvailabilityRepository : IAvailabilityRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public AvailabilityRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerUnavailability>> LoadOpenAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken) =>
        clubIds.Count == 0
            ? []
            : await _dbContext.PlayerUnavailabilities
                .Where(record => record.ResolvedAt == null && clubIds.Contains(record.ClubId))

                // Ordered by identity, which for a UUIDv7 is the order the absences were opened, so the
                // records are served in a stable order regardless of the database's row order.
                .OrderBy(record => record.Id)
                .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(PlayerUnavailability record) => _dbContext.PlayerUnavailabilities.Add(record);
}
