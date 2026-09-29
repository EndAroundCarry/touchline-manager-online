using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for the season-rollover checkpoint (`PR-4`, ADR-0031).</summary>
internal sealed class SeasonRolloverRepository : ISeasonRolloverRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public SeasonRolloverRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<SeasonRollover?> FindBySeasonAsync(
        Guid worldId,
        Guid seasonId,
        CancellationToken cancellationToken) =>
        _dbContext.SeasonRollovers.SingleOrDefaultAsync(
            rollover => rollover.WorldId == worldId && rollover.SeasonId == seasonId,
            cancellationToken);

    /// <inheritdoc />
    public Task<SeasonRollover?> FindByIdAsync(Guid rolloverId, CancellationToken cancellationToken) =>
        _dbContext.SeasonRollovers.SingleOrDefaultAsync(rollover => rollover.Id == rolloverId, cancellationToken);

    /// <inheritdoc />
    public void Add(SeasonRollover rollover) => _dbContext.SeasonRollovers.Add(rollover);
}
