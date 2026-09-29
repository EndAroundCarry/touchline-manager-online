using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>
/// Persistence for the squad module's player records.
/// </summary>
/// <remarks>
/// <para>
/// A staging port, like the world module's repositories: it adds rows to the current unit of work and
/// never saves, so a workflow that stages several modules' rows still commits once.
/// </para>
/// <para>
/// The squad module owns these tables, but the world bootstrap is the one use case that creates them
/// before anyone plays: seeding a world necessarily creates its clubs and, with them, the squads the
/// claim inherits (`WORLD-9`). That is the same cross-module bootstrap the seeder already performs for
/// the competition and finance shells (`MOD-2`).
/// </para>
/// </remarks>
public interface ISquadRepository
{
    /// <summary>Stages a generated player.</summary>
    void AddPlayer(Player player);

    /// <summary>Stages a generated player's attributes.</summary>
    void AddPlayerAttributes(PlayerAttributes attributes);

    /// <summary>Stages a generated player's state.</summary>
    void AddPlayerState(PlayerState state);

    /// <summary>Stages a generated player's active contract (`SQ-6`).</summary>
    void AddPlayerContract(PlayerContract contract);

    /// <summary>Stages a generated player's active registration (`SQ-6`).</summary>
    void AddPlayerRegistration(PlayerRegistration registration);

    /// <summary>
    /// Finds one contract, tracked, so a renewal can close it and stage its replacement in one unit of work
    /// (`CON-3`, `CON-4`).
    /// </summary>
    /// <param name="contractId">The contract to find.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlayerContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a player's active contract, tracked, so a transfer can close it and stage the buyer's in one unit
    /// of work (`CON-5`, `TRF-10`).
    /// </summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlayerContract?> FindActiveContractAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a player's active registration, tracked, so a transfer can end it and stage the buyer's in one
    /// unit of work (`SQ-6`, `CON-5`).
    /// </summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlayerRegistration?> FindActiveRegistrationAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads several players, tracked, so the rollover can retire or release them in one unit of work
    /// (`CON-6`).
    /// </summary>
    /// <param name="playerIds">The players.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Player>> LoadPlayersAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the active contracts of several clubs, tracked, so the rollover can close and re-sign them in
    /// one unit of work (`CON-6`, `CON-8`).
    /// </summary>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PlayerContract>> LoadActiveContractsForClubsAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the active registrations of several players, tracked, so the rollover can end them in one unit
    /// of work (`SQ-6`, `CON-6`).
    /// </summary>
    /// <param name="playerIds">The players.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PlayerRegistration>> LoadActiveRegistrationsForPlayersAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken);
}
