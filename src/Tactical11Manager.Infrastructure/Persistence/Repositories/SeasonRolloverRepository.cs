using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for the season-rollover checkpoint (`PR-4`, ADR-0031).</summary>
internal sealed class SeasonRolloverRepository : ISeasonRolloverRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public SeasonRolloverRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

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
