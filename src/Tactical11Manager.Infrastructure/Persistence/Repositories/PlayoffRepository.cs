using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The promotion play-off bracket's persistence: the ties a season draws and the match each is played in (`PR-4`).
/// </summary>
internal sealed class PlayoffRepository : IPlayoffRepository
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public PlayoffRepository(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void Add(PlayoffTie tie) => _dbContext.PlayoffTies.Add(tie);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayoffTie>> ListBySeasonAsync(Guid seasonId, CancellationToken cancellationToken) =>
        await _dbContext.PlayoffTies
            .Where(tie => tie.SeasonId == seasonId)
            .OrderBy(tie => tie.Slot)
            .ThenBy(tie => tie.ParentDivisionId)
            .ThenBy(tie => tie.FeederDivisionId)
            .ThenBy(tie => tie.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<PlayoffTie?> FindByFixtureAsync(Guid fixtureId, CancellationToken cancellationToken) =>
        await _dbContext.PlayoffTies
            .FirstOrDefaultAsync(tie => tie.FixtureId == fixtureId, cancellationToken);

    /// <inheritdoc />
    public async Task<int> CountUndecidedAsync(Guid seasonId, CancellationToken cancellationToken) =>
        await _dbContext.PlayoffTies
            .CountAsync(tie => tie.SeasonId == seasonId && tie.WinnerClubId == null, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> IsSeasonSettledAsync(Guid seasonId, CancellationToken cancellationToken)
    {
        var unpublished = await (
            from matchday in _dbContext.Matchdays
            join divisionSeason in _dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            where divisionSeason.SeasonId == seasonId
                && matchday.PublicationStatus != MatchdayPublicationStatus.Published
            select matchday.Id)
            .AnyAsync(cancellationToken);

        return !unpublished && await CountUndecidedAsync(seasonId, cancellationToken) == 0;
    }
}
