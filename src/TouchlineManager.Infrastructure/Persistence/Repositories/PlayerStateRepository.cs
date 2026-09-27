using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// A player's condition, fatigue, and morale: what the matchday publication applies a result's load to
/// (`TRN-11`, `TRN-13`).
/// </summary>
/// <remarks>
/// Tracked records, because the publication mutates them — a match consumes condition, adds fatigue, and
/// moves morale — and a row copy would move <see cref="PlayerState.ApplyMatchLoad"/> out of the domain. It
/// stages and never saves, so the loads commit with the results that produced them.
/// </remarks>
internal sealed class PlayerStateRepository : IPlayerStateRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public PlayerStateRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerState>> LoadAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken) =>
        playerIds.Count == 0
            ? []
            : await _dbContext.PlayerStates
                .Where(state => playerIds.Contains(state.PlayerId))
                .OrderBy(state => state.PlayerId)
                .ToListAsync(cancellationToken);
}
