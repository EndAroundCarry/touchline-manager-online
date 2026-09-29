using Microsoft.EntityFrameworkCore;
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

    /// <inheritdoc />
    public Task<PlayerContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken) =>
        _dbContext.PlayerContracts.SingleOrDefaultAsync(
            contract => contract.Id == contractId,
            cancellationToken);

    /// <inheritdoc />
    public Task<PlayerContract?> FindActiveContractAsync(Guid playerId, CancellationToken cancellationToken) =>
        _dbContext.PlayerContracts.SingleOrDefaultAsync(
            contract => contract.PlayerId == playerId && contract.Status == ContractStatus.Active,
            cancellationToken);

    /// <inheritdoc />
    public Task<PlayerRegistration?> FindActiveRegistrationAsync(
        Guid playerId,
        CancellationToken cancellationToken) =>
        _dbContext.PlayerRegistrations.SingleOrDefaultAsync(
            registration => registration.PlayerId == playerId
                && registration.Status == RegistrationStatus.Active,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Player>> LoadPlayersAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken) =>
        await _dbContext.Players
            .Where(player => playerIds.Contains(player.Id))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerContract>> LoadActiveContractsForClubsAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken) =>
        await _dbContext.PlayerContracts
            .Where(contract => clubIds.Contains(contract.ClubId) && contract.Status == ContractStatus.Active)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerRegistration>> LoadActiveRegistrationsForPlayersAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken) =>
        await _dbContext.PlayerRegistrations
            .Where(registration => playerIds.Contains(registration.PlayerId)
                && registration.Status == RegistrationStatus.Active)
            .ToListAsync(cancellationToken);
}
