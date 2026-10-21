using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Abstractions.World;

/// <summary>
/// Persistence for a club's snack bar and merchandise shop and their takings history (`STRC-1`).
/// </summary>
/// <remarks>
/// The world module owns <c>world.club_structures</c> and <c>world.club_structure_takings</c>, so every write
/// to them goes through here (`MOD-1`). The finance run's reads of the levels are projections in the finance
/// queries, not methods on this port, for the reason the module rules give (`MOD-3`).
/// </remarks>
public interface IStructureRepository
{
    /// <summary>Stages a newly opened building.</summary>
    void Add(ClubStructure building);

    /// <summary>Stages one line of a building's sales and upkeep history.</summary>
    void AddTakings(ClubStructureTakings takings);

    /// <summary>Finds a building by its identity, tracked, or null when there is none.</summary>
    /// <param name="buildingId">The building.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClubStructure?> FindAsync(Guid buildingId, CancellationToken cancellationToken);

    /// <summary>Finds one of a club's buildings, tracked, or null when it has not been opened.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="kind">Which building.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClubStructure?> FindByClubAsync(Guid clubId, StructureKind kind, CancellationToken cancellationToken);

    /// <summary>Lists a club's buildings, tracked, in the order a manager reads them.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClubStructure>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken);
}
