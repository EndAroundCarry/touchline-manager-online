using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The squad module's write-side persistence: it stages generated players and their four satellite rows.
/// </summary>
/// <remarks>
/// Deliberately tiny for now. The seeded world is the only writer in Stage 4's first milestone; the read
/// projections and the commands that edit tactics, training, and contracts arrive with their own
/// milestones, and each will add its own port rather than widening this one.
/// </remarks>
internal sealed class SquadRepository : ISquadRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public SquadRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void AddPlayer(Player player) => _dbContext.Players.Add(player);

    /// <inheritdoc />
    public void AddPlayerAttributes(PlayerAttributes attributes) => _dbContext.PlayerAttributes.Add(attributes);

    /// <inheritdoc />
    public void AddPlayerState(PlayerState state) => _dbContext.PlayerStates.Add(state);

    /// <inheritdoc />
    public void AddPlayerContract(PlayerContract contract) => _dbContext.PlayerContracts.Add(contract);

    /// <inheritdoc />
    public void AddPlayerRegistration(PlayerRegistration registration) =>
        _dbContext.PlayerRegistrations.Add(registration);
}
